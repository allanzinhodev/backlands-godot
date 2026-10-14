#pragma once

#include "world/Connection.hpp"

#include <cstddef>
#include <unordered_map>
#include <vector>

namespace backlands::world {

// As conexões abertas, por id. Concentra o "mandar para todos", que é a
// operação de replicação mais comum, e a varredura das que fecharam.
class ConnectionRegistry {
public:
    void add(const ConnectionPtr& connection);

    // Devolve se havia uma conexão com este id.
    bool remove(ConnectionId id);

    [[nodiscard]] std::size_t size() const noexcept { return connections_.size(); }

    // Envia o frame a todas as sessões, opcionalmente pulando uma. As já
    // fechadas também são puladas: o frame não teria para onde ir.
    void broadcast(const std::vector<std::byte>& frame, ConnectionId except = 0);

    // Remove as conexões fechadas e devolve os ids delas, na ordem em que
    // estavam, para quem chama tirar as entidades do mundo.
    [[nodiscard]] std::vector<ConnectionId> take_closed();

private:
    std::unordered_map<ConnectionId, ConnectionPtr> connections_;
};

} // namespace backlands::world
