#pragma once

#include <cstddef>
#include <cstdint>

// Constantes do framing. Contrato espelhado em docs/protocol.md e no client C#.
namespace backlands::net {

// Header: uint16 opcode + uint16 length (tamanho TOTAL, header incluído), little-endian.
inline constexpr std::size_t kHeaderSize = 4;

// Teto defensivo: qualquer pacote maior é entrada hostil ou bug. Derruba a sessão.
inline constexpr std::size_t kMaxPacketSize = 2048;

inline constexpr std::size_t kMaxPayloadSize = kMaxPacketSize - kHeaderSize;

// Limite para strings no fio, para um pacote malicioso não forçar alocação grande.
inline constexpr std::size_t kMaxStringLength = 255;

// Versão do contrato. Sobe a cada mudança incompatível de pacote; o servidor
// recusa client de versão diferente com motivo próprio, em vez de deixá-lo
// cair depois com "payload truncado", que não diz nada a quem está jogando.
inline constexpr std::uint16_t kProtocolVersion = 2;

struct PacketHeader {
    std::uint16_t opcode = 0;
    std::uint16_t length = 0;
};

} // namespace backlands::net
