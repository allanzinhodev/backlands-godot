#pragma once

#include "net/Protocol.hpp"

#include <cstdint>
#include <cstring>
#include <span>
#include <string>
#include <string_view>

namespace backlands::net {

// Leitor de payload com bounds-check em toda operação. Nunca lança: se algo não
// cabe, marca `failed()` e devolve zero. O handler checa `ok()` uma única vez no
// fim e derruba a sessão se a leitura falhou — pacote adulterado não vira crash.
//
// Sem nenhum include de Asio por design: testável sem socket.
class PacketReader {
public:
    explicit PacketReader(std::span<const std::byte> payload) noexcept
        : payload_(payload) {}

    [[nodiscard]] bool ok() const noexcept { return !failed_; }
    [[nodiscard]] bool failed() const noexcept { return failed_; }
    [[nodiscard]] std::size_t remaining() const noexcept { return payload_.size() - cursor_; }
    [[nodiscard]] std::size_t position() const noexcept { return cursor_; }

    std::uint8_t read_u8() noexcept { return read_integral<std::uint8_t>(); }
    std::uint16_t read_u16() noexcept { return read_integral<std::uint16_t>(); }
    std::uint32_t read_u32() noexcept { return read_integral<std::uint32_t>(); }
    std::uint64_t read_u64() noexcept { return read_integral<std::uint64_t>(); }

    std::int8_t read_i8() noexcept { return static_cast<std::int8_t>(read_u8()); }
    std::int16_t read_i16() noexcept { return static_cast<std::int16_t>(read_u16()); }
    std::int32_t read_i32() noexcept { return static_cast<std::int32_t>(read_u32()); }

    float read_f32() noexcept {
        const std::uint32_t bits = read_u32();
        float value = 0.0F;
        std::memcpy(&value, &bits, sizeof(value));
        return value;
    }

    // Strings no fio: uint16 length + bytes UTF-8, sem terminador nulo.
    std::string read_string() noexcept {
        const std::uint16_t length = read_u16();
        if (failed_) {
            return {};
        }
        if (length > kMaxStringLength || length > remaining()) {
            failed_ = true;
            return {};
        }
        const auto* first = reinterpret_cast<const char*>(payload_.data() + cursor_);
        cursor_ += length;
        return std::string(first, length);
    }

private:
    template <typename T>
    T read_integral() noexcept {
        if (failed_ || remaining() < sizeof(T)) {
            failed_ = true;
            return T{};
        }
        T value = T{};
        // Little-endian explícito: não dependemos da endianness da máquina.
        for (std::size_t i = 0; i < sizeof(T); ++i) {
            value |= static_cast<T>(static_cast<T>(std::to_integer<std::uint8_t>(
                         payload_[cursor_ + i]))
                                    << (8U * i));
        }
        cursor_ += sizeof(T);
        return value;
    }

    std::span<const std::byte> payload_;
    std::size_t cursor_ = 0;
    bool failed_ = false;
};

} // namespace backlands::net
