#include "net/Listener.hpp"

#include "core/Log.hpp"
#include "game/World.hpp"
#include "handlers/Dispatch.hpp"

#include <utility>

namespace backlands::net {
namespace asio = boost::asio;

Listener::Listener(asio::io_context& io, std::uint16_t port, PacketDispatcher& dispatcher)
    : acceptor_(io), dispatcher_(dispatcher), port_(port) {}

void Listener::start() {
    const asio::ip::tcp::endpoint endpoint(asio::ip::tcp::v4(), port_);

    acceptor_.open(endpoint.protocol());
    acceptor_.set_option(asio::ip::tcp::acceptor::reuse_address(true));
    acceptor_.bind(endpoint);
    acceptor_.listen();

    log::info("escutando em 0.0.0.0:{}", port_);
    accept_next();
}

void Listener::stop() {
    boost::system::error_code ec;
    acceptor_.close(ec);
}

void Listener::accept_next() {
    acceptor_.async_accept([this](const boost::system::error_code& ec,
                                  asio::ip::tcp::socket socket) {
        if (ec) {
            // operation_aborted é o caminho normal de shutdown.
            if (ec != asio::error::operation_aborted) {
                log::error("falha no accept: {}", ec.message());
                accept_next();
            }
            return;
        }

        const SessionId id = next_session_id_++;
        auto session = std::make_shared<Session>(id, std::move(socket), dispatcher_);
        dispatcher_.world().add_session(session);
        session->start();

        accept_next();
    });
}

} // namespace backlands::net
