#pragma once

#include "net/Protocol.hpp"

#include <boost/asio/ip/tcp.hpp>

#include <array>
#include <cstddef>
#include <cstdint>
#include <deque>
#include <memory>
#include <span>
#include <string>
#include <vector>

namespace backlands::net {

class PacketDispatcher;

using SessionId = std::uint32_t;

// Uma conexão TCP. Máquina de estados de leitura (header -> body) e fila de
// escrita própria. Vive em shared_ptr porque as continuações do Asio precisam
// manter o objeto vivo enquanto há I/O pendente.
//
// Thread-safety: nenhuma. Tudo roda no único thread do io_context, igual ao
// modelo do eAthena — sem locks, sem strands.
class Session : public std::enable_shared_from_this<Session> {
public:
    Session(SessionId id, boost::asio::ip::tcp::socket socket, PacketDispatcher& dispatcher);

    [[nodiscard]] SessionId id() const noexcept { return id_; }
    [[nodiscard]] bool closed() const noexcept { return closed_; }
    [[nodiscard]] const std::string& remote() const noexcept { return remote_; }

    void start();

    // Enfileira um frame já pronto (saído de PacketWriter::finish()).
    void send(std::vector<std::byte> frame);

    // Encerra a sessão. `reason` só aparece no log.
    void close(const char* reason);

private:
    void read_header();
    void read_body(std::uint16_t opcode, std::uint16_t length);
    void write_next();

    SessionId id_;
    boost::asio::ip::tcp::socket socket_;
    PacketDispatcher& dispatcher_;
    std::string remote_;

    std::array<std::byte, kHeaderSize> header_buffer_{};
    std::vector<std::byte> body_buffer_;

    // async_write nunca pode se sobrepor no mesmo socket: a fila garante um
    // write em voo por vez, encadeando o próximo na conclusão.
    std::deque<std::vector<std::byte>> outgoing_;
    bool writing_ = false;
    bool closed_ = false;
};

using SessionPtr = std::shared_ptr<Session>;

} // namespace backlands::net
