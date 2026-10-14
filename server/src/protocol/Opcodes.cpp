#include "protocol/Opcodes.hpp"

namespace backlands::protocol {
namespace {

struct ClientOpcodeEntry {
    ClientOpcode opcode;
    const char* name;
};

// Fonte única dos opcodes que o client pode mandar: aceitar e nomear para log
// leem a mesma tabela, então acrescentar um opcode é uma linha aqui, e as duas
// consultas não têm como divergir.
constexpr ClientOpcodeEntry kClientOpcodes[] = {
    {ClientOpcode::Ping, "C_PING"},
    {ClientOpcode::Login, "C_LOGIN"},
    {ClientOpcode::WalkTo, "C_WALK_TO"},
    {ClientOpcode::Attack, "C_ATTACK"},
};

const ClientOpcodeEntry* find_client_opcode(std::uint16_t raw) noexcept {
    for (const ClientOpcodeEntry& entry : kClientOpcodes) {
        if (to_raw(entry.opcode) == raw) {
            return &entry;
        }
    }
    return nullptr;
}

} // namespace

bool is_known_client_opcode(std::uint16_t raw) noexcept {
    return find_client_opcode(raw) != nullptr;
}

const char* client_opcode_name(std::uint16_t raw) noexcept {
    const ClientOpcodeEntry* entry = find_client_opcode(raw);
    return entry != nullptr ? entry->name : "UNKNOWN";
}

} // namespace backlands::protocol
