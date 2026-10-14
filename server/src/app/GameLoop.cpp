#include "app/GameLoop.hpp"

#include "core/Log.hpp"

#include <utility>

namespace backlands::app {

GameLoop::GameLoop(boost::asio::io_context& io, TickCallback on_tick,
                   std::uint32_t ticks_per_second)
    : timer_(io),
      on_tick_(std::move(on_tick)),
      ticks_per_second_(ticks_per_second == 0 ? 1 : ticks_per_second),
      tick_period_(std::chrono::milliseconds(1000 / ticks_per_second_)) {}

void GameLoop::start() {
    if (running_) {
        return;
    }
    running_ = true;
    next_ = std::chrono::steady_clock::now();
    log::info("game loop a {} Hz ({} ms por tick)", ticks_per_second_, tick_period_.count());
    arm();
}

void GameLoop::stop() {
    running_ = false;
    timer_.cancel();
}

void GameLoop::arm() {
    if (!running_) {
        return;
    }

    next_ += tick_period_;
    timer_.expires_at(next_);
    timer_.async_wait([this](const boost::system::error_code& ec) {
        if (ec || !running_) {
            return;
        }

        const auto delta_ms = static_cast<std::uint32_t>(tick_period_.count());
        on_tick_(delta_ms);

        // Atrasou? Recupera em passos fixos, até o teto. Passado o teto,
        // abandona o tempo perdido para não entrar em espiral.
        int catch_up = 0;
        const auto now = std::chrono::steady_clock::now();
        while (next_ + tick_period_ <= now && catch_up < kMaxCatchUpSteps) {
            next_ += tick_period_;
            on_tick_(delta_ms);
            ++catch_up;
        }
        if (catch_up == kMaxCatchUpSteps) {
            log::warn("game loop atrasado: descartando tempo acumulado");
            next_ = now;
        }

        arm();
    });
}

} // namespace backlands::app
