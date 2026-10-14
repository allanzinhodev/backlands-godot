#pragma once

#include "game/Entity.hpp"
#include "game/EntityRegistry.hpp"
#include "game/MapGrid.hpp"

#include <cstdint>
#include <vector>

namespace backlands::game {

// O que a perseguição fez com uma entidade neste tick. Quem chama transforma
// isto em pacote: a regra de quando parar ou sair andando fica aqui, testável
// sem rede.
struct ChaseEvent {
    enum class Type : std::uint8_t {
        ReachedTarget, // chegou ao alcance no meio do caminho e parou
        StartedWalk,   // começou a andar em direção ao alvo
    };

    EntityId id = kNoEntity;
    Type type = Type::StartedWalk;
};

// Leva quem tem alvo até o alcance do golpe, no modelo do Ragnarok: o pedido
// de ataque só escolhe o alvo, e a aproximação acontece aqui, tick a tick.
[[nodiscard]] std::vector<ChaseEvent> advance_chasing(EntityRegistry& registry,
                                                      const MapGrid& grid, std::uint32_t now_ms);

} // namespace backlands::game
