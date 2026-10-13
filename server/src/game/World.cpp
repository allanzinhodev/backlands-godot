#include "game/World.hpp"

#include "core/Log.hpp"

#include <iterator>

namespace backlands::game {

void World::add_session(const net::SessionPtr& session) {
    sessions_[session->id()] = session;
    log::info("sessão {} ({}) entrou — {} conectada(s)", session->id(), session->remote(),
              sessions_.size());
}

void World::remove_session(net::SessionId id) {
    if (sessions_.erase(id) > 0) {
        log::info("sessão {} saiu — {} conectada(s)", id, sessions_.size());
    }
}

net::Session* World::find_session(net::SessionId id) noexcept {
    const auto it = sessions_.find(id);
    return it == sessions_.end() ? nullptr : it->second.get();
}

void World::broadcast(const std::vector<std::byte>& frame, net::SessionId except) {
    for (auto& [id, session] : sessions_) {
        if (id == except || session->closed()) {
            continue;
        }
        session->send(frame);
    }
}

void World::tick(std::uint32_t /*delta_ms*/) {
    ++tick_count_;

    // Sessões fechadas são removidas aqui, e não durante a iteração de
    // broadcast, para não invalidar iteradores no meio de um envio.
    for (auto it = sessions_.begin(); it != sessions_.end();) {
        it = it->second->closed() ? sessions_.erase(it) : std::next(it);
    }
}

} // namespace backlands::game
