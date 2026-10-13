#pragma once

#include "net/Opcodes.hpp"
#include "net/Protocol.hpp"

#include <cstddef>
#include <cstdint>
#include <functional>
#include <span>
#include <unordered_map>

namespace backlands::game {
class World;
}

namespace backlands::net {

class Session;
class PacketReader;

// Roteia pacotes recebidos para os handlers. Um handler devolve false para
// pedir o encerramento da sessão (entrada inválida).
class PacketDispatcher {
public:
    using Handler = std::function<bool(Session&, PacketReader&)>;

    explicit PacketDispatcher(game::World& world);

    void register_handler(ClientOpcode opcode, Handler handler);

    // Devolve false se a sessão deve ser encerrada: opcode desconhecido,
    // payload malformado ou handler recusando a requisição.
    [[nodiscard]] bool dispatch(Session& session, std::uint16_t opcode,
                                std::span<const std::byte> payload);

    void on_session_closed(Session& session);

    [[nodiscard]] game::World& world() noexcept { return world_; }

private:
    game::World& world_;
    std::unordered_map<std::uint16_t, Handler> handlers_;
};

// Registra os handlers de todos os opcodes conhecidos.
void install_handlers(PacketDispatcher& dispatcher);

} // namespace backlands::net
