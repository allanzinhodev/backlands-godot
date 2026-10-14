#pragma once

#include <boost/asio/io_context.hpp>
#include <boost/asio/steady_timer.hpp>

#include <chrono>
#include <cstdint>
#include <functional>

namespace backlands::app {

// Tick de taxa fixa sobre o io_context (sem thread dedicada).
//
// Dois cuidados que evitam os erros clássicos:
//  - deadline ABSOLUTO (next_ += período) em vez de expires_after, senão o
//    tempo de processamento de cada tick vira drift acumulado;
//  - se o loop atrasar, roda vários passos de uma vez, mas com teto, senão um
//    engasgo longo viraria um burst de simulação sem fim.
//
// Só sabe marcar o tempo: o que acontece a cada passo é o `on_tick` que quem
// compõe o servidor entrega (hoje, World::tick).
class GameLoop {
public:
    static constexpr int kMaxCatchUpSteps = 5;

    using TickCallback = std::function<void(std::uint32_t delta_ms)>;

    GameLoop(boost::asio::io_context& io, TickCallback on_tick, std::uint32_t ticks_per_second);

    void start();
    void stop();

    [[nodiscard]] std::uint32_t ticks_per_second() const noexcept { return ticks_per_second_; }
    [[nodiscard]] std::chrono::milliseconds tick_period() const noexcept { return tick_period_; }

private:
    void arm();

    boost::asio::steady_timer timer_;
    TickCallback on_tick_;
    std::uint32_t ticks_per_second_;
    std::chrono::milliseconds tick_period_;
    std::chrono::steady_clock::time_point next_;
    bool running_ = false;
};

} // namespace backlands::app
