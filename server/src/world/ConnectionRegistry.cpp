#include "world/ConnectionRegistry.hpp"

namespace backlands::world {

void ConnectionRegistry::add(const ConnectionPtr& connection) {
    connections_[connection->id()] = connection;
}

bool ConnectionRegistry::remove(ConnectionId id) {
    return connections_.erase(id) > 0;
}

void ConnectionRegistry::broadcast(const std::vector<std::byte>& frame, ConnectionId except) {
    for (auto& [id, connection] : connections_) {
        if (id == except || connection->closed()) {
            continue;
        }
        connection->send(frame);
    }
}

std::vector<ConnectionId> ConnectionRegistry::take_closed() {
    std::vector<ConnectionId> closed;
    for (auto it = connections_.begin(); it != connections_.end();) {
        if (!it->second->closed()) {
            ++it;
            continue;
        }
        closed.push_back(it->first);
        it = connections_.erase(it);
    }
    return closed;
}

} // namespace backlands::world
