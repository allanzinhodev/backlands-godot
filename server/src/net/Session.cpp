#include "net/Session.hpp"

#include "core/Log.hpp"
#include "protocol/Opcodes.hpp"
#include "protocol/PacketReader.hpp"

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

Session::Session(SessionId id, asio::ip::tcp::socket socket, SessionHandler& handler)
    : id_(id),
      socket_(std::move(socket)),
      handler_(handler),
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

            protocol::PacketReader header(header_buffer_);
            const std::uint16_t opcode = header.read_u16();
            const std::uint16_t length = header.read_u16();

            // Validação do framing antes de qualquer alocação: um `length`
            // absurdo não pode nos fazer reservar memória arbitrária.
            if (length < protocol::kHeaderSize || length > protocol::kMaxPacketSize) {
                log::warn("sessão {}: length inválido {} (opcode 0x{:04X})", id_, length, opcode);
                close("length inválido");
                return;
            }
            if (!protocol::is_known_client_opcode(opcode)) {
                log::warn("sessão {}: opcode desconhecido 0x{:04X}", id_, opcode);
                close("opcode desconhecido");
                return;
            }

            read_body(opcode, length);
        });
}

void Session::read_body(std::uint16_t opcode, std::uint16_t length) {
    const std::size_t payload_size = static_cast<std::size_t>(length) - protocol::kHeaderSize;
    if (payload_size == 0) {
        body_buffer_.clear();
        if (!handler_.on_packet(*this, opcode, {})) {
            // close_after_flush e não close: o handler pode ter enfileirado o
            // motivo da recusa, e o client precisa recebê-lo antes do fim.
            close_after_flush("handler recusou o pacote");
            return;
        }
        if (!closing()) {
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
            if (!handler_.on_packet(*this, opcode, body_buffer_)) {
                close_after_flush("handler recusou o pacote");
                return;
            }
            if (!closing()) {
                read_header();
            }
        });
}

void Session::send(std::vector<std::byte> frame) {
    // Depois de decidir encerrar não aceitamos nova saída, mas o que já está
    // na fila continua válido e será drenado.
    if (closed_ || closing_ || frame.empty()) {
        return;
    }
    // push_back num deque não invalida referências a elementos existentes,
    // então enfileirar durante um async_write em voo é seguro.
    outgoing_.push_back(std::move(frame));
    // Só dispara na transição 0 -> 1; os demais encadeiam em write_next().
    if (!writing_) {
        write_next();
    }
}

void Session::write_next() {
    if (closed_ || outgoing_.empty()) {
        writing_ = false;
        // Fila drenou e já havíamos pedido o encerramento: fecha agora.
        if (closing_ && !closed_) {
            close(pending_close_reason_ != nullptr ? pending_close_reason_ : "encerrando");
        }
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
                          // Só agora o buffer do async_write pode ser liberado.
                          outgoing_.pop_front();
                          write_next();
                      });
}

void Session::close_after_flush(const char* reason) {
    if (closed_ || closing_) {
        return;
    }
    closing_ = true;
    pending_close_reason_ = reason;

    // Nada em voo nem na fila: não há o que esperar.
    if (!writing_ && outgoing_.empty()) {
        close(reason);
    }
}

void Session::close(const char* reason) {
    if (closed_) {
        return;
    }
    closed_ = true;

    // outgoing_ NÃO é limpo aqui de propósito: se há um async_write em voo,
    // ele aponta para o buffer da frente da fila, e destruí-lo agora é
    // use-after-free. A fila morre junto com a Session, que o shared_ptr das
    // continuações mantém viva até a última operação terminar.
    boost::system::error_code ec;
    socket_.shutdown(asio::ip::tcp::socket::shutdown_both, ec);
    socket_.close(ec);

    log::info("sessão {} ({}) encerrada: {}", id_, remote_, reason);
    handler_.on_session_closed(*this);
}

} // namespace backlands::net
