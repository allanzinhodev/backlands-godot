#pragma once

#include "net/SessionHandler.hpp"
#include "protocol/Opcodes.hpp"
#include "protocol/PacketReader.hpp"
#include "world/Connection.hpp"

#include <cstddef>
#include <cstdint>
#include <functional>
#include <span>
#include <unordered_map>

namespace backlands::world {
class World;
}

namespace backlands::handlers {

// Roteia pacotes recebidos para os handlers. Um handler devolve false para
// pedir o encerramento da sessão (entrada inválida).
//
// É o net::SessionHandler do servidor: o transporte entrega sessões e pacotes
// aqui, e sessões abrindo e fechando viram conexões entrando e saindo do mundo.
class PacketDispatcher final : public net::SessionHandler {
public:
    using Handler = std::function<bool(world::Connection&, protocol::PacketReader&)>;

    explicit PacketDispatcher(world::World& world);

    void register_handler(protocol::ClientOpcode opcode, Handler handler);

    void on_session_opened(const net::SessionPtr& session) override;

    // Devolve false se a sessão deve ser encerrada: opcode desconhecido,
    // payload malformado ou handler recusando a requisição.
    [[nodiscard]] bool on_packet(net::Session& session, std::uint16_t opcode,
                                 std::span<const std::byte> payload) override;

    void on_session_closed(net::Session& session) override;

private:
    world::World& world_;
    std::unordered_map<std::uint16_t, Handler> handlers_;
};

} // namespace backlands::handlers
