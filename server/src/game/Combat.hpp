#pragma once

#include "game/Entity.hpp"
#include "game/EntityRegistry.hpp"
#include "game/MapGrid.hpp"
#include "game/MonsterAi.hpp"

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

    // 0 significa erro de ataque. Não há pacote separado para "miss": é o que
    // o Ragnarok faz, e o client mostra "Miss" ao ver dano zero.
    std::int32_t damage = 0;
    Cell cell;           // posição relevante (respawn)
    std::int32_t hp = 0; // HP do alvo após o evento
};

// Distância de Chebyshev em células: num grid com diagonais, mover-se na
// diagonal cobre uma célula em cada eixo, então o alcance tem de ser medido
// assim — usar distância euclidiana faria o alcance diagonal parecer maior do
// que o movimento permite.
[[nodiscard]] int cell_distance(Cell a, Cell b) noexcept;

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

} // namespace backlands::game
