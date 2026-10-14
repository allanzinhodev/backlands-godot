#include "game/Entity.hpp"
#include "game/EntityRegistry.hpp"
#include "protocol/Opcodes.hpp"
#include "protocol/PacketReader.hpp"
#include "protocol/Protocol.hpp"
#include "world/Connection.hpp"
#include "world/ConnectionRegistry.hpp"
#include "world/World.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstddef>
#include <cstdint>
#include <memory>
#include <span>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

using backlands::game::Cell;
using backlands::game::Direction;
using backlands::game::Entity;
using backlands::game::EntityId;
using backlands::game::kNoEntity;
using backlands::protocol::PacketReader;
using backlands::protocol::ServerOpcode;
using backlands::world::Connection;
using backlands::world::ConnectionId;
using backlands::world::ConnectionRegistry;
using backlands::world::World;
using SpawnError = backlands::game::EntityRegistry::SpawnError;

namespace {

using Frame = std::vector<std::byte>;

// Conexão falsa: guarda cada frame que o mundo mandaria pelo socket. É o que
// a interface Connection permite — o World inteiro roda sem Asio.
class FakeConnection final : public Connection {
public:
    explicit FakeConnection(ConnectionId id) : id_(id) {}

    [[nodiscard]] ConnectionId id() const noexcept override { return id_; }
    [[nodiscard]] const std::string& remote() const noexcept override { return remote_; }
    [[nodiscard]] bool closed() const noexcept override { return is_closed; }

    void send(Frame frame) override { frames.push_back(std::move(frame)); }

    std::vector<Frame> frames;
    bool is_closed = false;

private:
    ConnectionId id_;
    std::string remote_ = "teste:0";
};

std::uint16_t opcode_of(const Frame& frame) {
    PacketReader header(frame);
    return header.read_u16();
}

PacketReader payload_of(const Frame& frame) {
    return PacketReader(std::span<const std::byte>(frame).subspan(backlands::protocol::kHeaderSize));
}

constexpr std::uint16_t raw(ServerOpcode opcode) {
    return backlands::protocol::to_raw(opcode);
}

// O mapa real do repositório (spawn em (6, 16), rio em x = 14..16) com duas
// conexões abertas, nenhuma ainda no mundo.
struct Fixture {
    World world;
    std::shared_ptr<FakeConnection> first = std::make_shared<FakeConnection>(1);
    std::shared_ptr<FakeConnection> second = std::make_shared<FakeConnection>(2);

    Fixture() {
        world.load_map(BACKLANDS_REPO_MAP);
        world.add_connection(first);
        world.add_connection(second);
    }

    EntityId login(FakeConnection& connection, std::string_view name) {
        REQUIRE(world.try_login(connection, name) == SpawnError::None);
        const Entity* entity = world.entities().find_by_session(connection.id());
        REQUIRE(entity != nullptr);
        return entity->id;
    }

    void forget_frames() {
        first->frames.clear();
        second->frames.clear();
    }
};

} // namespace

TEST_CASE("login manda mapa, identidade e quem já estava, nesta ordem") {
    Fixture f;
    const EntityId id = f.login(*f.first, "Primeiro");
    const auto& frames = f.first->frames;
    const std::uint16_t height = f.world.grid().height();

    // O mapa vem antes de tudo: sem o grid o client não desenha o cenário.
    REQUIRE(frames.size() > static_cast<std::size_t>(height) + 1);
    REQUIRE(opcode_of(frames[0]) == raw(ServerOpcode::MapInfo));
    auto info = payload_of(frames[0]);
    CHECK(info.read_u16() == f.world.grid().width());
    CHECK(info.read_u16() == height);
    CHECK(info.read_string() == "starter_fields");

    // Uma linha por pacote, em ordem.
    for (std::uint16_t y = 0; y < height; ++y) {
        const Frame& row = frames[1 + y];
        REQUIRE(opcode_of(row) == raw(ServerOpcode::MapRow));
        auto reader = payload_of(row);
        CHECK(reader.read_u16() == y);
        CHECK(reader.read_u16() == f.world.grid().width());
    }

    const Frame& enter = frames[1 + height];
    REQUIRE(opcode_of(enter) == raw(ServerOpcode::EnterWorld));
    auto reader = payload_of(enter);
    CHECK(reader.read_u32() == id);
    CHECK(reader.read_u16() == 6);
    CHECK(reader.read_u16() == 16);

    // Depois, um S_ENTITY_SPAWN para cada entidade que já estava (os monstros
    // do mapa); ninguém anda antes do primeiro tick, então não há S_ENTITY_WALK.
    const std::size_t already_there = f.world.entities().size() - 1;
    CHECK(already_there > 0);
    REQUIRE(frames.size() == 2 + height + already_there);
    for (std::size_t i = 2 + height; i < frames.size(); ++i) {
        CHECK(opcode_of(frames[i]) == raw(ServerOpcode::EntitySpawn));
        CHECK(payload_of(frames[i]).read_u32() != id);
    }
}

TEST_CASE("quem já estava no mundo recebe o spawn de quem entrou") {
    Fixture f;
    f.login(*f.first, "Primeiro");
    f.forget_frames();

    const EntityId second_id = f.login(*f.second, "Segundo");

    REQUIRE(f.first->frames.size() == 1);
    CHECK(opcode_of(f.first->frames[0]) == raw(ServerOpcode::EntitySpawn));
    CHECK(payload_of(f.first->frames[0]).read_u32() == second_id);
}

TEST_CASE("nome inválido recusa o login com motivo 1 e mantém a conexão") {
    Fixture f;

    CHECK(f.world.try_login(*f.first, "ab") == SpawnError::InvalidName);

    REQUIRE(f.first->frames.size() == 1);
    CHECK(opcode_of(f.first->frames[0]) == raw(ServerOpcode::LoginFailed));
    CHECK(payload_of(f.first->frames[0]).read_u8() == 1);

    // Erro de usuário: a conexão continua no mundo e pode tentar outro nome.
    CHECK(f.world.connection_count() == 2);
    CHECK(f.world.try_login(*f.first, "Valido") == SpawnError::None);
}

TEST_CASE("pedir caminhada antes de entrar no mundo é violação de protocolo") {
    Fixture f;

    CHECK_FALSE(f.world.request_walk(*f.first, Cell{8, 16}));
    CHECK(f.first->frames.empty());
    CHECK(f.second->frames.empty());
}

TEST_CASE("destino na água devolve a parada só a quem pediu") {
    Fixture f;
    const EntityId id = f.login(*f.first, "Primeiro");
    f.login(*f.second, "Segundo");
    f.forget_frames();

    CHECK(f.world.request_walk(*f.first, Cell{15, 10}));

    REQUIRE(f.first->frames.size() == 1);
    CHECK(opcode_of(f.first->frames[0]) == raw(ServerOpcode::EntityStop));
    auto reader = payload_of(f.first->frames[0]);
    CHECK(reader.read_u32() == id);
    CHECK(reader.read_u16() == 6);
    CHECK(reader.read_u16() == 16);

    CHECK(f.second->frames.empty());
}

TEST_CASE("destino válido replica a caminhada a todos") {
    Fixture f;
    const EntityId id = f.login(*f.first, "Primeiro");
    f.login(*f.second, "Segundo");
    f.forget_frames();

    CHECK(f.world.request_walk(*f.first, Cell{8, 16}));

    for (const auto* connection : {f.first.get(), f.second.get()}) {
        REQUIRE(connection->frames.size() == 1);
        CHECK(opcode_of(connection->frames[0]) == raw(ServerOpcode::EntityWalk));
        auto reader = payload_of(connection->frames[0]);
        CHECK(reader.read_u32() == id);
        CHECK(reader.read_u16() == 6);
        CHECK(reader.read_u16() == 16);
        CHECK(reader.read_u16() == backlands::game::kDefaultWalkSpeedMs);
        REQUIRE(reader.read_u8() == 2);
        CHECK(reader.read_u8() == static_cast<std::uint8_t>(Direction::East));
        CHECK(reader.read_u8() == static_cast<std::uint8_t>(Direction::East));
    }
}

TEST_CASE("andar cancela o ataque em curso") {
    Fixture f;
    const EntityId id = f.login(*f.first, "Primeiro");
    const EntityId other = f.login(*f.second, "Segundo");

    Entity* entity = f.world.entities().find(id);
    REQUIRE(entity != nullptr);
    entity->combat.target = other;

    // Vale até para destino recusado: mover é uma decisão que substitui a
    // anterior, ande ou não.
    CHECK(f.world.request_walk(*f.first, Cell{15, 10}));
    CHECK(entity->combat.target == kNoEntity);
}

TEST_CASE("sair do mundo avisa os outros com S_ENTITY_DESPAWN") {
    Fixture f;
    const EntityId id = f.login(*f.first, "Primeiro");
    f.login(*f.second, "Segundo");
    f.forget_frames();

    f.world.remove_connection(f.first->id());

    CHECK(f.first->frames.empty());
    REQUIRE(f.second->frames.size() == 1);
    CHECK(opcode_of(f.second->frames[0]) == raw(ServerOpcode::EntityDespawn));
    CHECK(payload_of(f.second->frames[0]).read_u32() == id);

    CHECK(f.world.connection_count() == 1);
    CHECK(f.world.entities().find(id) == nullptr);
}

TEST_CASE("conexão fechada é varrida no tick e a entidade dela some") {
    Fixture f;
    f.login(*f.first, "Primeiro");
    const EntityId second_id = f.login(*f.second, "Segundo");
    f.forget_frames();

    f.second->is_closed = true;
    f.world.tick(50);

    CHECK(f.world.connection_count() == 1);
    CHECK(f.world.entities().find(second_id) == nullptr);
    // A conexão fechada não recebe mais nada.
    CHECK(f.second->frames.empty());

    bool saw_despawn = false;
    for (const Frame& frame : f.first->frames) {
        if (opcode_of(frame) == raw(ServerOpcode::EntityDespawn) &&
            payload_of(frame).read_u32() == second_id) {
            saw_despawn = true;
        }
    }
    CHECK(saw_despawn);
}

TEST_CASE("atacar a si mesmo é recusado com motivo 4") {
    Fixture f;
    const EntityId id = f.login(*f.first, "Primeiro");
    f.forget_frames();

    CHECK(f.world.request_attack(*f.first, id));

    REQUIRE(f.first->frames.size() == 1);
    CHECK(opcode_of(f.first->frames[0]) == raw(ServerOpcode::AttackRejected));
    CHECK(payload_of(f.first->frames[0]).read_u8() == 4);
    CHECK(f.world.entities().find(id)->combat.target == kNoEntity);
}

TEST_CASE("atacar antes de entrar no mundo é violação de protocolo") {
    Fixture f;

    CHECK_FALSE(f.world.request_attack(*f.first, 1));
    CHECK(f.first->frames.empty());
}

TEST_CASE("matar um monstro replica o golpe e depois a morte, e agenda o renascer") {
    Fixture f;
    const EntityId id = f.login(*f.first, "Primeiro");
    f.forget_frames();

    Entity* monster = nullptr;
    for (Entity* entity : f.world.entities().all_mutable()) {
        if (entity->is_monster()) {
            monster = entity;
            break;
        }
    }
    REQUIRE(monster != nullptr);

    // Monstro colado no jogador e um golpe que certamente acerta e mata.
    Entity* player = f.world.entities().find(id);
    REQUIRE(player != nullptr);
    monster->cell = Cell{7, 16};
    monster->stats.hp = 1;
    monster->stats.flee = 0;
    monster->stats.defense_percent = 0;
    monster->stats.defense_flat = 0;
    player->stats.hit = 10000;
    player->stats.attack_min = 50;
    player->stats.attack_max = 50;
    player->combat.target = monster->id;

    f.world.tick(50);

    std::size_t attack_at = 0;
    std::size_t died_at = 0;
    for (std::size_t i = 0; i < f.first->frames.size(); ++i) {
        const Frame& frame = f.first->frames[i];
        if (opcode_of(frame) == raw(ServerOpcode::EntityAttack)) {
            auto reader = payload_of(frame);
            CHECK(reader.read_u32() == id);
            CHECK(reader.read_u32() == monster->id);
            reader.read_u16(); // célula e direção do atacante
            reader.read_u16();
            reader.read_u8();
            reader.read_u16(); // duração do golpe
            CHECK(reader.read_i32() == 50);
            CHECK(reader.read_i32() == 0); // a vida que sobrou, do próprio golpe
            attack_at = i + 1;
        }
        else if (opcode_of(frame) == raw(ServerOpcode::EntityDied)) {
            auto reader = payload_of(frame);
            CHECK(reader.read_u32() == monster->id);
            CHECK(reader.read_u32() == id);
            CHECK(reader.read_u16() == 7);
            CHECK(reader.read_u16() == 16);
            died_at = i + 1;
        }
    }
    REQUIRE(attack_at != 0);
    REQUIRE(died_at != 0);
    CHECK(attack_at < died_at);

    // As consequências de jogo também saíram neste tick.
    CHECK(monster->combat.respawn_at_ms == f.world.now_ms() + monster->brain.respawn_delay_ms);
    CHECK(player->combat.target == kNoEntity);
}

TEST_CASE("o registro de conexões pula a excluída e as fechadas, e varre só as fechadas") {
    ConnectionRegistry registry;
    const auto a = std::make_shared<FakeConnection>(1);
    const auto b = std::make_shared<FakeConnection>(2);
    const auto c = std::make_shared<FakeConnection>(3);
    registry.add(a);
    registry.add(b);
    registry.add(c);

    b->is_closed = true;
    registry.broadcast(Frame{std::byte{0x2A}}, c->id());

    CHECK(a->frames.size() == 1);
    CHECK(b->frames.empty());
    CHECK(c->frames.empty());

    const std::vector<ConnectionId> closed = registry.take_closed();
    REQUIRE(closed.size() == 1);
    CHECK(closed[0] == 2);
    CHECK(registry.size() == 2);
    CHECK(registry.take_closed().empty());

    CHECK(registry.remove(1));
    CHECK_FALSE(registry.remove(1));
    CHECK(registry.size() == 1);
}
