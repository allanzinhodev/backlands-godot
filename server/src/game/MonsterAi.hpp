#pragma once

#include "game/Entity.hpp"
#include "game/EntityRegistry.hpp"
#include "game/MapGrid.hpp"

#include <cstdint>
#include <vector>

namespace backlands::game {

// Gerador pseudoaleatório pequeno e próprio (xorshift32).
//
// Com semente explícita em vez de rand(): a IA precisa ser reproduzível nos
// testes, e um gerador global compartilhado tornaria o resultado dependente da
// ordem em que outros sistemas sorteiam.
class Rng {
public:
    explicit Rng(std::uint32_t seed = 0x9E3779B9u) : state_(seed == 0 ? 1u : seed) {}

    std::uint32_t next() noexcept {
        state_ ^= state_ << 13;
        state_ ^= state_ >> 17;
        state_ ^= state_ << 5;
        return state_;
    }

    // Inteiro em [lo, hi]. Devolve lo se a faixa for inválida.
    int range(int lo, int hi) noexcept {
        if (hi <= lo) {
            return lo;
        }
        return lo + static_cast<int>(next() % static_cast<std::uint32_t>(hi - lo + 1));
    }

private:
    std::uint32_t state_;
};

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
// é o World, que tem a rede — assim a IA é testável sem socket.
struct WanderIntent {
    EntityId id = kNoEntity;
    Cell destination;
};

// Avança a IA de todos os monstros: adquire e larga alvos, e devolve os
// passeios ociosos para o chamador executar.
[[nodiscard]] std::vector<WanderIntent> advance_monster_ai(EntityRegistry& registry,
                                                           const MapGrid& grid,
                                                           std::uint32_t now_ms, Rng& rng);

// Jogador vivo mais próximo com linha de visão, dentro do raio, ou nullptr.
// Exposto para teste.
[[nodiscard]] const Entity* find_nearest_player(const EntityRegistry& registry,
                                                const MapGrid& grid, Cell from, int max_distance);

} // namespace backlands::game
