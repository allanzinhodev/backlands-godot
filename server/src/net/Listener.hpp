#pragma once

#include "net/Session.hpp"
#include "net/SessionHandler.hpp"

#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/tcp.hpp>

#include <cstdint>

namespace backlands::net {

// Aceita conexões e cria uma Session por cliente.
class Listener {
public:
    Listener(boost::asio::io_context& io, std::uint16_t port, SessionHandler& handler);

    void start();
    void stop();

    [[nodiscard]] std::uint16_t port() const noexcept { return port_; }

private:
    void accept_next();

    boost::asio::ip::tcp::acceptor acceptor_;
    SessionHandler& handler_;
    std::uint16_t port_;
    SessionId next_session_id_ = 1;
};

} // namespace backlands::net
