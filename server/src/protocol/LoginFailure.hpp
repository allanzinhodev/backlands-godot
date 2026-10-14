#pragma once

#include <cstdint>

namespace backlands::protocol {

// Motivo de S_LOGIN_FAILED, como vai no fio. Mora no protocolo, e não nas
// regras de jogo, porque um dos motivos (versão do protocolo diferente) nasce
// no handler, antes de existir qualquer entidade. Os demais espelham os
// valores de game::EntityRegistry::SpawnError; world/Replication confere isso
// em tempo de compilação.
enum class LoginFailure : std::uint8_t {
    None = 0,
    InvalidName = 1,
    NameInUse = 2,
    AlreadySpawned = 3,
    ProtocolMismatch = 4,
};

} // namespace backlands::protocol
