#include "net/Opcodes.hpp"

namespace backlands::net {

bool is_known_client_opcode(std::uint16_t raw) noexcept {
    switch (static_cast<ClientOpcode>(raw)) {
        case ClientOpcode::Ping:
        case ClientOpcode::Login:
            return true;
    }
    return false;
}

const char* client_opcode_name(std::uint16_t raw) noexcept {
    switch (static_cast<ClientOpcode>(raw)) {
        case ClientOpcode::Ping:
            return "C_PING";
        case ClientOpcode::Login:
            return "C_LOGIN";
    }
    return "UNKNOWN";
}

} // namespace backlands::net
