#include "protocol/PacketWriter.hpp"

#include <stdexcept>

namespace backlands::protocol {

std::vector<std::byte> PacketWriter::finish() {
    // Estourar o teto aqui é bug nosso (pacote montado pelo servidor), não
    // entrada hostil: falha alto em vez de enviar um frame que o peer vai
    // rejeitar. Quem precisa enviar listas grandes deve fatiar em vários frames.
    if (buffer_.size() > kMaxPacketSize) {
        throw std::length_error("PacketWriter: frame excede kMaxPacketSize");
    }

    const auto length = static_cast<std::uint16_t>(buffer_.size());
    buffer_[2] = static_cast<std::byte>(length & 0xFFU);
    buffer_[3] = static_cast<std::byte>((length >> 8U) & 0xFFU);

    return std::move(buffer_);
}

} // namespace backlands::protocol
