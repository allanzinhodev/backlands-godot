#pragma once

#include <cstdint>

// Tabela de opcodes. Contrato único em docs/protocol.md; o client C# espelha
// estes valores. C_* = client -> server (0x0001-0x0FFF).
//                    S_* = server -> client (0x1001-0x1FFF).
namespace backlands::net {

enum class ClientOpcode : std::uint16_t {
    Ping = 0x0001,
    Login = 0x0002,
};

enum class ServerOpcode : std::uint16_t {
    Pong = 0x1001,
    EnterWorld = 0x1002,
    LoginFailed = 0x1003,
    EntitySpawn = 0x1004,
    EntityDespawn = 0x1005,
};

[[nodiscard]] constexpr std::uint16_t to_raw(ClientOpcode opcode) noexcept {
    return static_cast<std::uint16_t>(opcode);
}

[[nodiscard]] constexpr std::uint16_t to_raw(ServerOpcode opcode) noexcept {
    return static_cast<std::uint16_t>(opcode);
}

// Opcode desconhecido derruba a sessão, então esta checagem é a primeira linha
// de defesa contra lixo no fio.
[[nodiscard]] bool is_known_client_opcode(std::uint16_t raw) noexcept;

// Nome legível para log. Devolve "UNKNOWN" se não reconhecido.
[[nodiscard]] const char* client_opcode_name(std::uint16_t raw) noexcept;

} // namespace backlands::net
