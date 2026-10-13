#include "handlers/Dispatch.hpp"

#include "core/Log.hpp"
#include "game/World.hpp"
#include "net/PacketReader.hpp"
#include "net/PacketWriter.hpp"
#include "net/Session.hpp"

#include <string>
#include <utility>

namespace backlands::net {

PacketDispatcher::PacketDispatcher(game::World& world) : world_(world) {}

void PacketDispatcher::register_handler(ClientOpcode opcode, Handler handler) {
    handlers_[to_raw(opcode)] = std::move(handler);
}

bool PacketDispatcher::dispatch(Session& session, std::uint16_t opcode,
                                std::span<const std::byte> payload) {
    const auto it = handlers_.find(opcode);
    if (it == handlers_.end()) {
        log::warn("sessão {}: nenhum handler para 0x{:04X}", session.id(), opcode);
        return false;
    }

    PacketReader reader(payload);
    if (!it->second(session, reader)) {
        // Inclui o opcode: só "handler recusou" não diz o que investigar.
        log::warn("sessão {}: {} recusado ({} bytes de payload{})", session.id(),
                  client_opcode_name(opcode), payload.size(),
                  reader.failed() ? ", leitura truncada" : "");
        return false;
    }

    // Payload curto demais para o que o handler esperava: a leitura falhou no
    // meio. Tratamos como pacote adulterado.
    if (reader.failed()) {
        log::warn("sessão {}: payload truncado em {} ({} bytes)", session.id(),
                  client_opcode_name(opcode), payload.size());
        return false;
    }
    return true;
}

void PacketDispatcher::on_session_closed(Session& session) {
    world_.remove_session(session.id());
}

void install_handlers(PacketDispatcher& dispatcher) {
    // Capturado por referência: o World é criado antes do dispatcher em main()
    // e destruído depois dele.
    game::World& world = dispatcher.world();

    // C_PING -> S_PONG. Devolve o nonce para o client medir RTT.
    dispatcher.register_handler(ClientOpcode::Ping, [](Session& session, PacketReader& reader) {
        const std::uint32_t nonce = reader.read_u32();
        if (reader.failed()) {
            return false;
        }

        PacketWriter writer(to_raw(ServerOpcode::Pong));
        writer.write_u32(nonce);
        session.send(writer.finish());
        return true;
    });

    // C_LOGIN -> S_ENTER_WORLD (+ S_ENTITY_SPAWN aos demais) ou S_LOGIN_FAILED.
    dispatcher.register_handler(ClientOpcode::Login,
                                [&world](Session& session, PacketReader& reader) {
        const std::string name = reader.read_string();
        if (reader.failed()) {
            return false;
        }

        const auto error = world.try_login(session, name);

        // Nome inválido ou em uso é erro de usuário: mantém a conexão para ele
        // tentar outro nome. Já estar no mundo é erro de protocolo — nenhum
        // client correto manda dois logins — então a sessão cai.
        return error != game::EntityRegistry::SpawnError::AlreadySpawned;
    });

    // C_WALK_TO: o client manda apenas o DESTINO. Quem calcula e valida o
    // caminho é o servidor, que então replica o trajeto a todos.
    dispatcher.register_handler(ClientOpcode::WalkTo,
                                [&world](Session& session, PacketReader& reader) {
        const std::uint16_t x = reader.read_u16();
        const std::uint16_t y = reader.read_u16();
        if (reader.failed()) {
            return false;
        }

        return world.request_walk(session, game::Cell{x, y});
    });
}

} // namespace backlands::net
