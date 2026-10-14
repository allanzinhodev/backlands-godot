#include "handlers/PacketDispatcher.hpp"

#include "core/Log.hpp"
#include "net/Session.hpp"
#include "world/World.hpp"

#include <utility>

namespace backlands::handlers {

PacketDispatcher::PacketDispatcher(world::World& world) : world_(world) {}

void PacketDispatcher::register_handler(protocol::ClientOpcode opcode, Handler handler) {
    handlers_[protocol::to_raw(opcode)] = std::move(handler);
}

void PacketDispatcher::on_session_opened(const net::SessionPtr& session) {
    world_.add_connection(session);
}

bool PacketDispatcher::on_packet(net::Session& session, std::uint16_t opcode,
                                 std::span<const std::byte> payload) {
    const auto it = handlers_.find(opcode);
    if (it == handlers_.end()) {
        log::warn("sessão {}: nenhum handler para 0x{:04X}", session.id(), opcode);
        return false;
    }

    protocol::PacketReader reader(payload);
    if (!it->second(session, reader)) {
        // Inclui o opcode: só "handler recusou" não diz o que investigar.
        log::warn("sessão {}: {} recusado ({} bytes de payload{})", session.id(),
                  protocol::client_opcode_name(opcode), payload.size(),
                  reader.failed() ? ", leitura truncada" : "");
        return false;
    }

    // Payload curto demais para o que o handler esperava: a leitura falhou no
    // meio. Tratamos como pacote adulterado.
    if (reader.failed()) {
        log::warn("sessão {}: payload truncado em {} ({} bytes)", session.id(),
                  protocol::client_opcode_name(opcode), payload.size());
        return false;
    }
    return true;
}

void PacketDispatcher::on_session_closed(net::Session& session) {
    world_.remove_connection(session.id());
}

} // namespace backlands::handlers
