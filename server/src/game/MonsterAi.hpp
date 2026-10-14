#pragma once

#include "game/Entity.hpp"
#include "game/EntityRegistry.hpp"
#include "game/MapGrid.hpp"
#include "game/Rng.hpp"

#include <cstdint>
#include <vector>

namespace backlands::game {

// Intervalo entre decisões de um monstro, equivalente ao MIN_MOBTHINKTIME do
// eAthena. Com tick de 50 ms, é um pensamento a cada dois ticks.
inline constexpr std::uint32_t kMonsterThinkIntervalMs = 100;

// Só monstros com algum jogador dentro desta distância pensam. Sem este portão
// o custo da IA cresceria com o tamanho do mapa, não com o que está em jogo.
inline constexpr int kMonsterActiveRange = 18;

// Passeio ocioso: espera entre passeios e alcance do destino sorteado.
inline constexpr std::uint32_t kIdleWanderIntervalMs = 4000;
inline constexpr std::uint32_t kIdleWanderJitterMs = 1000;
inline constexpr int kIdleWanderRadius = 7;

// Um destino que o monstro quer alcançar. A IA só DECIDE; quem move e replica
// é o World, que tem a rede — assim a IA é testável sem socket. O mover em si
// é o start_wander_walks, abaixo, que o World chama antes de replicar.
struct WanderIntent {
    EntityId id = kNoEntity;
    Cell destination;
};

// Avança a IA de todos os monstros: adquire e larga alvos, e devolve os
// passeios ociosos para o chamador executar.
[[nodiscard]] std::vector<WanderIntent> advance_monster_ai(EntityRegistry& registry,
                                                           const MapGrid& grid,
                                                           std::uint32_t now_ms, Rng& rng);

// Executa os passeios decididos pela IA: quem já anda é deixado em paz, e
// destino sem rota é descartado. Devolve quem começou a andar, para quem chama
// replicar a caminhada.
[[nodiscard]] std::vector<EntityId> start_wander_walks(EntityRegistry& registry,
                                                       const MapGrid& grid,
                                                       const std::vector<WanderIntent>& intents,
                                                       std::uint32_t now_ms);

// Jogador vivo mais próximo com linha de visão, dentro do raio, ou nullptr.
// Exposto para teste.
[[nodiscard]] const Entity* find_nearest_player(const EntityRegistry& registry,
                                                const MapGrid& grid, Cell from, int max_distance);

} // namespace backlands::game
