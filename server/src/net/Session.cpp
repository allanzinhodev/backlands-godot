#include "net/Session.hpp"

#include "core/Log.hpp"
#include "handlers/Dispatch.hpp"
#include "net/Opcodes.hpp"
#include "net/PacketReader.hpp"

#include <boost/asio/read.hpp>
#include <boost/asio/write.hpp>

#include <utility>

namespace backlands::net {
namespace asio = boost::asio;

namespace {
std::string describe_endpoint(const asio::ip::tcp::socket& socket) {
    boost::system::error_code ec;
    const auto endpoint = socket.remote_endpoint(ec);
    if (ec) {
        return "?";
    }
    return endpoint.address().to_string() + ":" + std::to_string(endpoint.port());
}
} // namespace

Session::Session(SessionId id, asio::ip::tcp::socket socket, PacketDispatcher& dispatcher)
    : id_(id),
      socket_(std::move(socket)),
      dispatcher_(dispatcher),
      remote_(describe_endpoint(socket_)) {}

void Session::start() {
    // Latência importa mais que throughput: pacotes são pequenos e frequentes.
    boost::system::error_code ec;
    socket_.set_option(asio::ip::tcp::no_delay(true), ec);
    if (ec) {
        log::warn("sessão {}: não foi possível desabilitar Nagle: {}", id_, ec.message());
    }
    read_header();
}

void Session::read_header() {
    auto self = shared_from_this();
    asio::async_read(
        socket_, asio::buffer(header_buffer_),
        [this, self](const boost::system::error_code& ec, std::size_t) {
            if (ec) {
                close(ec == asio::error::eof ? "peer encerrou" : "erro de leitura");
                return;
            }

            PacketReader header(header_buffer_);
            const std::uint16_t opcode = header.read_u16();
            const std::uint16_t length = header.read_u16();

            // Validação do framing antes de qualquer alocação: um `length`
            // absurdo não pode nos fazer reservar memória arbitrária.
            if (length < kHeaderSize || length > kMaxPacketSize) {
                log::warn("sessão {}: length inválido {} (opcode 0x{:04X})", id_, length, opcode);
                close("length inválido");
                return;
            }
            if (!is_known_client_opcode(opcode)) {
                log::warn("sessão {}: opcode desconhecido 0x{:04X}", id_, opcode);
                close("opcode desconhecido");
                return;
            }

            read_body(opcode, length);
        });
}

void Session::read_body(std::uint16_t opcode, std::uint16_t length) {
    const std::size_t payload_size = static_cast<std::size_t>(length) - kHeaderSize;
    if (payload_size == 0) {
        body_buffer_.clear();
        if (!dispatcher_.dispatch(*this, opcode, {})) {
            close("handler recusou o pacote");
            return;
        }
        if (!closed_) {
            read_header();
        }
        return;
    }

    body_buffer_.resize(payload_size);
    auto self = shared_from_this();
    asio::async_read(
        socket_, asio::buffer(body_buffer_),
        [this, self, opcode](const boost::system::error_code& ec, std::size_t) {
            if (ec) {
                close("erro de leitura do payload");
                return;
            }
            if (!dispatcher_.dispatch(*this, opcode, body_buffer_)) {
                close("handler recusou o pacote");
                return;
            }
            if (!closed_) {
                read_header();
            }
        });
}

void Session::send(std::vector<std::byte> frame) {
    if (closed_ || frame.empty()) {
        return;
    }
    outgoing_.push_back(std::move(frame));
    // Só dispara na transição 0 -> 1; os demais encadeiam em write_next().
    if (!writing_) {
        write_next();
    }
}

void Session::write_next() {
    if (outgoing_.empty()) {
        writing_ = false;
        return;
    }

    writing_ = true;
    auto self = shared_from_this();
    asio::async_write(socket_, asio::buffer(outgoing_.front()),
                      [this, self](const boost::system::error_code& ec, std::size_t) {
                          if (ec) {
                              writing_ = false;
                              close("erro de escrita");
                              return;
                          }
                          outgoing_.pop_front();
                          write_next();
                      });
}

void Session::close(const char* reason) {
    if (closed_) {
        return;
    }
    closed_ = true;
    outgoing_.clear();

    boost::system::error_code ec;
    socket_.shutdown(asio::ip::tcp::socket::shutdown_both, ec);
    socket_.close(ec);

    log::info("sessão {} ({}) encerrada: {}", id_, remote_, reason);
    dispatcher_.on_session_closed(*this);
}

} // namespace backlands::net
