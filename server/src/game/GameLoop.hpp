#pragma once

#include <boost/asio/io_context.hpp>
#include <boost/asio/steady_timer.hpp>

#include <chrono>
#include <cstdint>

namespace backlands::game {

class World;

// Tick de taxa fixa sobre o io_context (sem thread dedicada).
//
// Dois cuidados que evitam os erros clássicos:
//  - deadline ABSOLUTO (next_ += período) em vez de expires_after, senão o
//    tempo de processamento de cada tick vira drift acumulado;
//  - se o loop atrasar, roda vários passos de uma vez, mas com teto, senão um
//    engasgo longo viraria um burst de simulação sem fim.
class GameLoop {
public:
    static constexpr int kMaxCatchUpSteps = 5;

    GameLoop(boost::asio::io_context& io, World& world, std::uint32_t ticks_per_second);

    void start();
    void stop();

    [[nodiscard]] std::uint32_t ticks_per_second() const noexcept { return ticks_per_second_; }
    [[nodiscard]] std::chrono::milliseconds tick_period() const noexcept { return tick_period_; }

private:
    void arm();

    boost::asio::steady_timer timer_;
    World& world_;
    std::uint32_t ticks_per_second_;
    std::chrono::milliseconds tick_period_;
    std::chrono::steady_clock::time_point next_;
    bool running_ = false;
};

} // namespace backlands::game
