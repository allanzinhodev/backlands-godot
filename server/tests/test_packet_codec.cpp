#include "net/Opcodes.hpp"
#include "net/PacketReader.hpp"
#include "net/PacketWriter.hpp"
#include "net/Protocol.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstddef>
#include <cstdint>
#include <span>
#include <string>
#include <vector>

using namespace backlands::net;

namespace {

// Separa header e payload como a Session faz ao ler do socket.
struct DecodedFrame {
    std::uint16_t opcode;
    std::uint16_t length;
    std::span<const std::byte> payload;
};

DecodedFrame decode(const std::vector<std::byte>& frame) {
    PacketReader header(frame);
    const std::uint16_t opcode = header.read_u16();
    const std::uint16_t length = header.read_u16();
    return {opcode, length,
            std::span<const std::byte>(frame).subspan(kHeaderSize)};
}

} // namespace

TEST_CASE("header carrega opcode e tamanho total do frame") {
    PacketWriter writer(0x1234);
    writer.write_u32(0xDEADBEEF);
    const auto frame = writer.finish();

    REQUIRE(frame.size() == kHeaderSize + 4);

    const auto decoded = decode(frame);
    CHECK(decoded.opcode == 0x1234);
    // `length` é o tamanho TOTAL, header incluído — e não só o payload.
    CHECK(decoded.length == frame.size());
}

TEST_CASE("inteiros fazem round-trip em little-endian") {
    PacketWriter writer(0x0001);
    writer.write_u8(0x12);
    writer.write_u16(0x3456);
    writer.write_u32(0x789ABCDE);
    writer.write_u64(0x0102030405060708ULL);
    writer.write_i16(-2);
    writer.write_i32(-123456);
    const auto frame = writer.finish();

    SECTION("os bytes saem na ordem little-endian") {
        // u8 em 4, u16 em 5..6: 0x3456 vira 56 34.
        CHECK(static_cast<std::uint8_t>(frame[4]) == 0x12);
        CHECK(static_cast<std::uint8_t>(frame[5]) == 0x56);
        CHECK(static_cast<std::uint8_t>(frame[6]) == 0x34);
    }

    SECTION("a leitura devolve os mesmos valores") {
        auto reader = PacketReader(decode(frame).payload);
        CHECK(reader.read_u8() == 0x12);
        CHECK(reader.read_u16() == 0x3456);
        CHECK(reader.read_u32() == 0x789ABCDEU);
        CHECK(reader.read_u64() == 0x0102030405060708ULL);
        CHECK(reader.read_i16() == -2);
        CHECK(reader.read_i32() == -123456);
        CHECK(reader.ok());
        CHECK(reader.remaining() == 0);
    }
}

TEST_CASE("float faz round-trip bit a bit") {
    PacketWriter writer(0x0001);
    writer.write_f32(3.5F);
    writer.write_f32(-0.125F);
    const auto frame = writer.finish();

    auto reader = PacketReader(decode(frame).payload);
    CHECK(reader.read_f32() == 3.5F);
    CHECK(reader.read_f32() == -0.125F);
    CHECK(reader.ok());
}

TEST_CASE("strings fazem round-trip com prefixo de tamanho") {
    PacketWriter writer(0x0001);
    writer.write_string("Allan");
    writer.write_string("");
    writer.write_string("acentuação");
    const auto frame = writer.finish();

    auto reader = PacketReader(decode(frame).payload);
    CHECK(reader.read_string() == "Allan");
    CHECK(reader.read_string().empty());
    CHECK(reader.read_string() == "acentuação");
    CHECK(reader.ok());
}

TEST_CASE("string longa é truncada em kMaxStringLength") {
    const std::string oversized(kMaxStringLength + 50, 'x');

    PacketWriter writer(0x0001);
    writer.write_string(oversized);
    const auto frame = writer.finish();

    auto reader = PacketReader(decode(frame).payload);
    const auto decoded_value = reader.read_string();
    CHECK(reader.ok());
    CHECK(decoded_value.size() == kMaxStringLength);
}

// --- Entrada hostil: o leitor nunca pode estourar o buffer ------------------

TEST_CASE("ler além do payload marca falha em vez de estourar o buffer") {
    PacketWriter writer(0x0001);
    writer.write_u16(7);
    const auto frame = writer.finish();

    auto reader = PacketReader(decode(frame).payload);
    CHECK(reader.read_u16() == 7);
    REQUIRE(reader.ok());

    // Não há mais nada no payload: a leitura falha e devolve zero.
    CHECK(reader.read_u32() == 0);
    CHECK(reader.failed());
}

TEST_CASE("payload vazio falha na primeira leitura") {
    PacketReader reader(std::span<const std::byte>{});
    CHECK(reader.remaining() == 0);
    CHECK(reader.read_u8() == 0);
    CHECK(reader.failed());
}

TEST_CASE("inteiro cortado no meio falha em vez de ler lixo") {
    // Payload com 3 bytes, mas o handler espera um u32.
    const std::vector<std::byte> payload{std::byte{0x01}, std::byte{0x02}, std::byte{0x03}};

    PacketReader reader(payload);
    CHECK(reader.read_u32() == 0);
    CHECK(reader.failed());
}

TEST_CASE("string com tamanho maior que o payload é rejeitada") {
    // Declara 1000 bytes de string, mas só envia 2.
    const std::vector<std::byte> payload{std::byte{0xE8}, std::byte{0x03}, std::byte{'a'},
                                         std::byte{'b'}};

    PacketReader reader(payload);
    CHECK(reader.read_string().empty());
    CHECK(reader.failed());
}

TEST_CASE("string acima de kMaxStringLength é rejeitada na leitura") {
    // Mesmo que os bytes estivessem presentes, um tamanho acima do teto é
    // recusado: impede que um pacote force alocação grande.
    std::vector<std::byte> payload{std::byte{0x00}, std::byte{0x02}}; // 512
    payload.resize(2 + 512, std::byte{'x'});

    PacketReader reader(payload);
    CHECK(reader.read_string().empty());
    CHECK(reader.failed());
}

TEST_CASE("falha é persistente: leituras seguintes não voltam a ter sucesso") {
    const std::vector<std::byte> payload{std::byte{0x01}};

    PacketReader reader(payload);
    CHECK(reader.read_u32() == 0);
    REQUIRE(reader.failed());

    // Havia 1 byte sobrando, mas depois de falhar o leitor não lê mais nada:
    // um handler que ignore o erro não consegue agir sobre dados parciais.
    CHECK(reader.read_u8() == 0);
    CHECK(reader.failed());
}

// --- Opcodes ----------------------------------------------------------------

TEST_CASE("apenas opcodes de client conhecidos são aceitos") {
    CHECK(is_known_client_opcode(to_raw(ClientOpcode::Ping)));

    CHECK_FALSE(is_known_client_opcode(0x0000));
    CHECK_FALSE(is_known_client_opcode(0xFFFF));
    // Opcode de servidor enviado pelo client é entrada inválida.
    CHECK_FALSE(is_known_client_opcode(to_raw(ServerOpcode::Pong)));
}

TEST_CASE("nome do opcode serve para log") {
    CHECK(std::string(client_opcode_name(to_raw(ClientOpcode::Ping))) == "C_PING");
    CHECK(std::string(client_opcode_name(0xFFFF)) == "UNKNOWN");
}

TEST_CASE("frame de PING/PONG casa com o contrato do protocolo") {
    // O que o client manda em C_PING.
    PacketWriter request(to_raw(ClientOpcode::Ping));
    request.write_u32(0x11223344);
    const auto frame = request.finish();

    CHECK(frame.size() == 8);
    const auto decoded = decode(frame);
    CHECK(decoded.opcode == 0x0001);
    CHECK(decoded.length == 8);

    auto reader = PacketReader(decoded.payload);
    CHECK(reader.read_u32() == 0x11223344U);
    CHECK(reader.ok());
}
