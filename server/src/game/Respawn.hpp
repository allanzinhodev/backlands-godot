#pragma once

#include "game/Entity.hpp"
#include "game/EntityRegistry.hpp"
#include "game/Geometry.hpp"

#include <cstdint>
#include <vector>

namespace backlands::game {

// Quanto tempo um jogador fica caído antes de voltar ao ponto inicial.
inline constexpr std::uint32_t kPlayerRespawnDelayMs = 5000;

// Marca quando o morto volta: monstro usa o atraso da espécie, jogador o
// atraso fixo acima.
void schedule_respawn(Entity& dead, std::uint32_t now_ms) noexcept;

// Traz de volta quem já cumpriu o tempo de chão: vida cheia, na casa (monstro)
// ou no ponto inicial do mapa (jogador), sem caminhada nem alvo. Devolve os
// ids na ordem em que renasceram, para quem chama replicar.
[[nodiscard]] std::vector<EntityId> advance_respawns(EntityRegistry& registry, Cell player_spawn,
                                                     std::uint32_t now_ms);

} // namespace backlands::game
