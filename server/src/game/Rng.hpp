#pragma once

#include <cstdint>

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

} // namespace backlands::game
