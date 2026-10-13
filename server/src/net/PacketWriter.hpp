#pragma once

#include "net/Protocol.hpp"

#include <cstdint>
#include <cstring>
#include <span>
#include <string_view>
#include <vector>

namespace backlands::net {

// Monta um frame completo (header + payload). O campo `length` do header só é
// conhecido no fim, então reservamos os 4 bytes na construção e os preenchemos
// em finish(). Sem include de Asio: testável sem socket.
class PacketWriter {
public:
    explicit PacketWriter(std::uint16_t opcode) {
        buffer_.reserve(64);
        write_u16(opcode);
        write_u16(0); // placeholder do length
    }

    void write_u8(std::uint8_t value) { write_integral(value); }
    void write_u16(std::uint16_t value) { write_integral(value); }
    void write_u32(std::uint32_t value) { write_integral(value); }
    void write_u64(std::uint64_t value) { write_integral(value); }

    void write_i8(std::int8_t value) { write_u8(static_cast<std::uint8_t>(value)); }
    void write_i16(std::int16_t value) { write_u16(static_cast<std::uint16_t>(value)); }
    void write_i32(std::int32_t value) { write_u32(static_cast<std::uint32_t>(value)); }

    void write_f32(float value) {
        std::uint32_t bits = 0;
        std::memcpy(&bits, &value, sizeof(bits));
        write_u32(bits);
    }

    // Strings são truncadas em kMaxStringLength; o contrário seria permitir que
    // um nome absurdo estourasse o frame.
    void write_string(std::string_view value) {
        const auto length = static_cast<std::uint16_t>(
            value.size() > kMaxStringLength ? kMaxStringLength : value.size());
        write_u16(length);
        const auto* first = reinterpret_cast<const std::byte*>(value.data());
        buffer_.insert(buffer_.end(), first, first + length);
    }

    // Preenche o header com o tamanho final e devolve o frame pronto para envio.
    [[nodiscard]] std::vector<std::byte> finish();

    [[nodiscard]] std::size_t size() const noexcept { return buffer_.size(); }

private:
    template <typename T>
    void write_integral(T value) {
        // Little-endian explícito, igual ao PacketReader.
        for (std::size_t i = 0; i < sizeof(T); ++i) {
            buffer_.push_back(static_cast<std::byte>((value >> (8U * i)) & 0xFFU));
        }
    }

    std::vector<std::byte> buffer_;
};

} // namespace backlands::net
