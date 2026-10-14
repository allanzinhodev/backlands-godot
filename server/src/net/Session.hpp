#pragma once

#include "net/SessionHandler.hpp"
#include "protocol/Protocol.hpp"
#include "world/Connection.hpp"

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

using SessionId = world::ConnectionId;

// Uma conexão TCP. Máquina de estados de leitura (header -> body) e fila de
// escrita própria. Vive em shared_ptr porque as continuações do Asio precisam
// manter o objeto vivo enquanto há I/O pendente.
//
// Thread-safety: nenhuma. Tudo roda no único thread do io_context, igual ao
// modelo do eAthena — sem locks, sem strands.
//
// Para o mundo, é só uma world::Connection: o World nunca vê o socket.
class Session final : public world::Connection, public std::enable_shared_from_this<Session> {
public:
    Session(SessionId id, boost::asio::ip::tcp::socket socket, SessionHandler& handler);

    [[nodiscard]] SessionId id() const noexcept override { return id_; }
    [[nodiscard]] bool closed() const noexcept override { return closed_; }
    [[nodiscard]] const std::string& remote() const noexcept override { return remote_; }

    void start();

    // Enfileira um frame já pronto (saído de PacketWriter::finish()).
    void send(std::vector<std::byte> frame) override;

    // Encerra a sessão imediatamente, descartando o que ainda não foi enviado.
    // Para erro de socket ou framing inválido, onde não há nada a explicar.
    void close(const char* reason);

    // Para de ler e encerra assim que a fila de escrita drenar. Usado quando
    // já enfileiramos uma explicação (por exemplo S_LOGIN_FAILED) e queremos
    // que ela chegue antes de o socket morrer.
    void close_after_flush(const char* reason);

    [[nodiscard]] bool closing() const noexcept { return closing_ || closed_; }

private:
    void read_header();
    void read_body(std::uint16_t opcode, std::uint16_t length);
    void write_next();

    SessionId id_;
    boost::asio::ip::tcp::socket socket_;
    SessionHandler& handler_;
    std::string remote_;

    std::array<std::byte, protocol::kHeaderSize> header_buffer_{};
    std::vector<std::byte> body_buffer_;

    // async_write nunca pode se sobrepor no mesmo socket: a fila garante um
    // write em voo por vez, encadeando o próximo na conclusão.
    //
    // ATENÇÃO: enquanto `writing_` é true, o async_write em voo aponta para o
    // buffer de outgoing_.front(). Destruir esse elemento (clear/pop) antes da
    // conclusão é use-after-free — a fila só pode ser liberada junto com a
    // Session, que vive até a última operação assíncrona terminar.
    std::deque<std::vector<std::byte>> outgoing_;
    bool writing_ = false;
    bool closed_ = false;
    bool closing_ = false;
    const char* pending_close_reason_ = nullptr;
};

} // namespace backlands::net
