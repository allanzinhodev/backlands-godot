#pragma once

#include "protocol/PacketReader.hpp"
#include "world/Connection.hpp"

namespace backlands::world {
class World;
}

// Um handler por opcode de client. Cada um só decodifica o payload e delega ao
// World; a regra de jogo mora lá. A exceção é a versão do protocolo, checada
// no login: ela é regra do fio, não do jogo.
//
// Todos devolvem false para pedir o encerramento da sessão.
namespace backlands::handlers {

class PacketDispatcher;

[[nodiscard]] bool handle_ping(world::Connection& connection, protocol::PacketReader& reader);

[[nodiscard]] bool handle_login(world::World& world, world::Connection& connection,
                                protocol::PacketReader& reader);

[[nodiscard]] bool handle_walk_to(world::World& world, world::Connection& connection,
                                  protocol::PacketReader& reader);

[[nodiscard]] bool handle_attack(world::World& world, world::Connection& connection,
                                 protocol::PacketReader& reader);

// Registra os handlers de todos os opcodes conhecidos.
void install_game_handlers(PacketDispatcher& dispatcher, world::World& world);

} // namespace backlands::handlers
