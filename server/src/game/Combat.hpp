#pragma once

#include "game/Entity.hpp"
#include "game/EntityRegistry.hpp"
#include "game/Rng.hpp"

#include <cstdint>
#include <vector>

namespace backlands::game {

// Por que um pedido de ataque foi recusado. O client usa isto só para dar
// feedback; a decisão é sempre do servidor.
enum class AttackRejection : std::uint8_t {
    None = 0,
    NoSuchTarget = 1,
    TargetDead = 2,
    AttackerDead = 3,
    SelfTarget = 4,
    OutOfRange = 5,
    OnCooldown = 6,
};

// Resultado de um golpe resolvido.
struct DamageResult {
    std::int32_t damage = 0;
    bool missed = false;
    bool killed = false;
};

struct CombatEvent {
    enum class Type : std::uint8_t {
        Hit,      // alguém bateu em alguém (dano 0 = errou)
        Died,     // alvo chegou a 0 de HP
        Respawn,  // entidade voltou
    };

    Type type = Type::Hit;
    EntityId source = kNoEntity;
    EntityId target = kNoEntity;

    // Posição e direção do ATACANTE no instante do golpe. Vão no pacote para
    // o client parar de interpolar o trajeto abandonado: golpear limpa a
    // caminhada no servidor sem emitir parada, e sem isto o atacante desliza
    // pelo chão enquanto ataca.
    Cell source_cell;
    Direction source_facing = Direction::South;
    std::uint16_t attack_motion_ms = 0;

    // 0 significa erro de ataque. Não há pacote separado para "miss": é o que
    // o Ragnarok faz, e o client mostra "Miss" ao ver dano zero.
    std::int32_t damage = 0;
    Cell cell;           // posição relevante (respawn)
    std::int32_t hp = 0; // HP do alvo após o evento
};

[[nodiscard]] bool in_attack_range(const Entity& attacker, const Entity& target) noexcept;

// O golpe acertou? Taxa = 80 + hit - flee, limitada a [kMinHitRate, kMaxHitRate].
inline constexpr int kBaseHitRate = 80;
inline constexpr int kMinHitRate = 5;
inline constexpr int kMaxHitRate = 100;

[[nodiscard]] int hit_rate(const Stats& attacker, const Stats& defender) noexcept;

// Dano de um golpe físico simples, no modelo pré-renewal: sorteia na faixa de
// ataque, aplica a defesa percentual, subtrai a defesa plana e garante o
// mínimo de 1. Isolada de propósito: é o ponto único a mexer no balanceamento.
[[nodiscard]] std::int32_t compute_damage(const Stats& attacker, const Stats& defender,
                                          Rng& rng) noexcept;

// Resolve um golpe completo (acerto e dano). Não aplica nada.
[[nodiscard]] DamageResult roll_attack(const Stats& attacker, const Stats& defender,
                                       Rng& rng) noexcept;

// A parte da validação que não depende de alcance nem de relógio: o alvo
// existe, não é o próprio atacante, e os dois estão vivos (nesta ordem). É o
// que se checa ao SELECIONAR um alvo, quando o atacante ainda pode estar longe.
[[nodiscard]] AttackRejection validate_attack_target(const Entity& attacker,
                                                     const Entity* target) noexcept;

// Valida o pedido sem aplicar nada. Separado da aplicação para ser testável e
// para o handler poder responder ao client sem efeitos colaterais.
[[nodiscard]] AttackRejection validate_attack(const Entity& attacker, const Entity* target,
                                              std::uint32_t now_ms) noexcept;

// Aplica dano, marca morte e devolve o resultado. Não emite pacotes.
DamageResult apply_damage(Entity& target, std::int32_t damage) noexcept;

// Resolve os ataques pendentes de todas as entidades com alvo, respeitando
// alcance e cooldown. Devolve os eventos para quem chama replicar.
[[nodiscard]] std::vector<CombatEvent> advance_combat(EntityRegistry& registry,
                                                      std::uint32_t now_ms, Rng& rng);

// Consequências de jogo dos eventos de combate, evento a evento e na ordem em
// que vieram: monstro atingido passa a revidar; quem morreu agenda o respawn e
// deixa de ser alvo de quem mirava nele. Não emite pacotes — replicar os
// eventos é com quem chama.
void react_to_combat(EntityRegistry& registry, const std::vector<CombatEvent>& events,
                     std::uint32_t now_ms);

} // namespace backlands::game
