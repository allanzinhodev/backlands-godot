#include "game/Combat.hpp"

#include "core/Log.hpp"
#include "game/Geometry.hpp"
#include "game/Respawn.hpp"

#include <algorithm>

namespace backlands::game {

bool in_attack_range(const Entity& attacker, const Entity& target) noexcept {
    return cell_distance(attacker.cell, target.cell) <= attacker.stats.attack_range;
}

int hit_rate(const Stats& attacker, const Stats& defender) noexcept {
    const int rate = kBaseHitRate + attacker.hit - defender.flee;
    return std::clamp(rate, kMinHitRate, kMaxHitRate);
}

std::int32_t compute_damage(const Stats& attacker, const Stats& defender, Rng& rng) noexcept {
    // Sorteia na faixa do golpe, como os campos Attack/Attack2 do mob_db.
    std::int32_t damage = rng.range(attacker.attack_min, attacker.attack_max);

    // Defesa percentual PRIMEIRO e subtrativa DEPOIS. Inverter a ordem faria a
    // defesa percentual quase não importar em alvos com defesa plana alta.
    const std::int32_t percent = std::clamp(defender.defense_percent, 0, 100);
    damage = damage * (100 - percent) / 100;
    damage -= defender.defense_flat;

    // Um golpe que acerta sempre tira algo: sem o mínimo, um alvo muito
    // defensivo ficaria imortal e o combate travaria sem avisar o jogador.
    return std::max(damage, 1);
}

DamageResult roll_attack(const Stats& attacker, const Stats& defender, Rng& rng) noexcept {
    DamageResult result;

    if (rng.range(0, 99) >= hit_rate(attacker, defender)) {
        result.missed = true;
        result.damage = 0;
        return result;
    }

    result.damage = compute_damage(attacker, defender, rng);
    return result;
}

AttackRejection validate_attack_target(const Entity& attacker, const Entity* target) noexcept {
    if (target == nullptr) {
        return AttackRejection::NoSuchTarget;
    }
    if (target->id == attacker.id) {
        return AttackRejection::SelfTarget;
    }
    if (!attacker.alive()) {
        return AttackRejection::AttackerDead;
    }
    if (!target->alive()) {
        return AttackRejection::TargetDead;
    }
    return AttackRejection::None;
}

AttackRejection validate_attack(const Entity& attacker, const Entity* target,
                                std::uint32_t now_ms) noexcept {
    if (const AttackRejection rejection = validate_attack_target(attacker, target);
        rejection != AttackRejection::None) {
        return rejection;
    }
    if (!in_attack_range(attacker, *target)) {
        return AttackRejection::OutOfRange;
    }
    // O cooldown é o que impede um client modificado de bater a cada pacote:
    // o pedido chega, mas o golpe não sai antes da hora.
    if (now_ms < attacker.combat.next_attack_at_ms) {
        return AttackRejection::OnCooldown;
    }
    return AttackRejection::None;
}

DamageResult apply_damage(Entity& target, std::int32_t damage) noexcept {
    DamageResult result;
    result.damage = damage;

    target.stats.hp -= damage;
    if (target.stats.hp <= 0) {
        target.stats.hp = 0;
        result.killed = true;

        // Um morto não anda nem persegue; deixar o estado para trás faria o
        // cadáver continuar caminhando até o destino antigo.
        target.walk.clear();
        target.combat.clear_target();
    }
    return result;
}

std::vector<CombatEvent> advance_combat(EntityRegistry& registry, std::uint32_t now_ms,
                                        Rng& rng) {
    std::vector<CombatEvent> events;

    for (Entity* attacker : registry.all_mutable()) {
        if (attacker->combat.target == kNoEntity) {
            continue;
        }

        Entity* target = registry.find(attacker->combat.target);

        // Alvo que sumiu ou morreu encerra o ataque contínuo, senão a entidade
        // ficaria presa atacando um id morto para sempre.
        if (target == nullptr || !target->alive() || !attacker->alive()) {
            attacker->combat.clear_target();
            continue;
        }

        // Fora de alcance não é erro: no modelo do Ragnarok o atacante
        // persegue. Quem move é o chamador (World), que tem o grid. A
        // aproximação em si é o advance_chasing, em game/Chase.cpp.
        if (!in_attack_range(*attacker, *target)) {
            continue;
        }
        if (now_ms < attacker->combat.next_attack_at_ms) {
            continue;
        }

        // Parar de andar ao golpear: atacar enquanto desliza para o próximo
        // tile deixaria a posição do golpe ambígua.
        attacker->walk.clear();
        attacker->facing = direction_towards(attacker->cell, target->cell);
        attacker->combat.next_attack_at_ms = now_ms + attacker->stats.attack_delay_ms;

        // Um monstro não repensa durante a animação do golpe: sem isto ele
        // treme e recalcula caminho no meio do ataque.
        if (attacker->is_monster()) {
            attacker->brain.next_think_at_ms =
                std::max(attacker->brain.next_think_at_ms,
                         now_ms + attacker->stats.attack_motion_ms);
        }

        auto result = roll_attack(attacker->stats, target->stats, rng);
        if (!result.missed) {
            const auto applied = apply_damage(*target, result.damage);
            result.killed = applied.killed;
        }

        CombatEvent hit;
        hit.type = CombatEvent::Type::Hit;
        hit.source = attacker->id;
        hit.target = target->id;
        hit.damage = result.damage;
        hit.cell = target->cell;
        hit.hp = target->stats.hp;
        hit.source_cell = attacker->cell;
        hit.source_facing = attacker->facing;
        hit.attack_motion_ms = attacker->stats.attack_motion_ms;
        events.push_back(hit);

        if (result.killed) {
            CombatEvent died;
            died.type = CombatEvent::Type::Died;
            died.source = attacker->id;
            died.target = target->id;
            died.cell = target->cell;
            events.push_back(died);

            // Quem matou não continua golpeando o cadáver.
            attacker->combat.clear_target();
        }
    }

    return events;
}

void react_to_combat(EntityRegistry& registry, const std::vector<CombatEvent>& events,
                     std::uint32_t now_ms) {
    for (const CombatEvent& event : events) {
        switch (event.type) {
            case CombatEvent::Type::Hit: {
                // Monstro que apanha passa a revidar, inclusive os passivos:
                // é o que diferencia "não me procure" de "não reaja a nada".
                Entity* victim = registry.find(event.target);
                if (victim != nullptr && victim->is_monster() && victim->alive()) {
                    victim->brain.retaliate_against = event.source;
                }
                break;
            }

            case CombatEvent::Type::Died: {
                Entity* dead = registry.find(event.target);
                if (dead != nullptr) {
                    schedule_respawn(*dead, now_ms);
                    log::info("entidade {} ('{}') morreu para a entidade {}", dead->id,
                              dead->name, event.source);
                }

                // Quem estava mirando no morto para de mirar, senão ficaria
                // perseguindo um cadáver até ele renascer.
                for (Entity* other : registry.all_mutable()) {
                    if (other->combat.target == event.target) {
                        other->combat.clear_target();
                    }
                }
                break;
            }

            case CombatEvent::Type::Respawn:
                break;
        }
    }
}

} // namespace backlands::game
