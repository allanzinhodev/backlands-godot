#pragma once

#include <cstddef>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace backlands::world {

using ConnectionId = std::uint32_t;

// O que o mundo precisa de uma conexão: saber quem é, se ainda está aberta e
// mandar um frame. O World depende só disto, e não da sessão TCP: o
// transporte (net::Session) implementa esta interface, e os testes do mundo
// usam uma conexão falsa que só guarda os frames, sem abrir socket.
class Connection {
public:
    virtual ~Connection() = default;

    [[nodiscard]] virtual ConnectionId id() const noexcept = 0;
    [[nodiscard]] virtual const std::string& remote() const noexcept = 0;
    [[nodiscard]] virtual bool closed() const noexcept = 0;

    // Enfileira um frame já pronto (saído de PacketWriter::finish()).
    virtual void send(std::vector<std::byte> frame) = 0;
};

using ConnectionPtr = std::shared_ptr<Connection>;

} // namespace backlands::world
