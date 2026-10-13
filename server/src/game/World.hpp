#pragma once

#include "net/Session.hpp"

#include <cstddef>
#include <cstdint>
#include <unordered_map>
#include <vector>

namespace backlands::game {

// Estado do mundo. Por ora só o registro de sessões conectadas; entidades,
// grid e combate entram nos marcos seguintes.
//
// Dono do estado é o thread único do io_context: sem locks por design.
class World {
public:
    void add_session(const net::SessionPtr& session);
    void remove_session(net::SessionId id);

    [[nodiscard]] net::Session* find_session(net::SessionId id) noexcept;
    [[nodiscard]] std::size_t session_count() const noexcept { return sessions_.size(); }

    // Envia o frame a todas as sessões, opcionalmente pulando uma (normalmente
    // a que originou a ação).
    void broadcast(const std::vector<std::byte>& frame,
                   net::SessionId except = 0);

    // Chamado pelo GameLoop a cada tick. `delta_ms` é o passo fixo configurado.
    void tick(std::uint32_t delta_ms);

    [[nodiscard]] std::uint64_t tick_count() const noexcept { return tick_count_; }

private:
    std::unordered_map<net::SessionId, net::SessionPtr> sessions_;
    std::uint64_t tick_count_ = 0;
};

} // namespace backlands::game
