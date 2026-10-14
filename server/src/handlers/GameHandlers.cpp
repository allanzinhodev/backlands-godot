#include "handlers/GameHandlers.hpp"

#include "core/Log.hpp"
#include "handlers/PacketDispatcher.hpp"
#include "protocol/LoginFailure.hpp"
#include "protocol/Opcodes.hpp"
#include "protocol/Protocol.hpp"
#include "world/Replication.hpp"
#include "world/World.hpp"

#include <cstdint>
#include <string>

namespace backlands::handlers {

// C_PING -> S_PONG. Devolve o nonce para o client medir RTT.
bool handle_ping(world::Connection& connection, protocol::PacketReader& reader) {
    const std::uint32_t nonce = reader.read_u32();
    if (reader.failed()) {
        return false;
    }

    connection.send(world::make_pong_frame(nonce));
    return true;
}

// C_LOGIN -> S_ENTER_WORLD (+ S_ENTITY_SPAWN aos demais) ou S_LOGIN_FAILED.
bool handle_login(world::World& world, world::Connection& connection,
                  protocol::PacketReader& reader) {
    const std::uint16_t version = reader.read_u16();
    const std::string name = reader.read_string();
    if (reader.failed()) {
        return false;
    }

    // Versão diferente é recusa explícita, não queda silenciosa: sem isto
    // um client velho só descobriria o problema como "payload truncado",
    // que não diz nada a quem está jogando.
    if (version != protocol::kProtocolVersion) {
        log::warn("sessão {}: protocolo {} != {}", connection.id(), version,
                  protocol::kProtocolVersion);
        connection.send(world::make_login_failed_frame(protocol::LoginFailure::ProtocolMismatch));
        return false;
    }

    const auto error = world.try_login(connection, name);

    // Nome inválido ou em uso é erro de usuário: mantém a conexão para ele
    // tentar outro nome. Já estar no mundo é erro de protocolo — nenhum
    // client correto manda dois logins — então a sessão cai.
    return error != game::EntityRegistry::SpawnError::AlreadySpawned;
}

// C_WALK_TO: o client manda apenas o DESTINO. Quem calcula e valida o
// caminho é o servidor, que então replica o trajeto a todos.
bool handle_walk_to(world::World& world, world::Connection& connection,
                    protocol::PacketReader& reader) {
    const std::uint16_t x = reader.read_u16();
    const std::uint16_t y = reader.read_u16();
    if (reader.failed()) {
        return false;
    }
    return world.request_walk(connection, game::Cell{x, y});
}

// C_ATTACK: o client só escolhe o ALVO. Quando o golpe sai, se sai, e
// quanto tira é decisão do servidor no tick.
bool handle_attack(world::World& world, world::Connection& connection,
                   protocol::PacketReader& reader) {
    const std::uint32_t target_id = reader.read_u32();
    if (reader.failed()) {
        return false;
    }
    return world.request_attack(connection, target_id);
}

void install_game_handlers(PacketDispatcher& dispatcher, world::World& world) {
    dispatcher.register_handler(protocol::ClientOpcode::Ping, handle_ping);

    // Capturado por referência: o World é criado antes do dispatcher em main()
    // e destruído depois dele.
    dispatcher.register_handler(
        protocol::ClientOpcode::Login,
        [&world](world::Connection& connection, protocol::PacketReader& reader) {
            return handle_login(world, connection, reader);
        });

    dispatcher.register_handler(
        protocol::ClientOpcode::WalkTo,
        [&world](world::Connection& connection, protocol::PacketReader& reader) {
            return handle_walk_to(world, connection, reader);
        });

    dispatcher.register_handler(
        protocol::ClientOpcode::Attack,
        [&world](world::Connection& connection, protocol::PacketReader& reader) {
            return handle_attack(world, connection, reader);
        });
}

} // namespace backlands::handlers
