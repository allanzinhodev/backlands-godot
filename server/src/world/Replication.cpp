#include "world/Replication.hpp"

#include "protocol/Opcodes.hpp"
#include "protocol/PacketWriter.hpp"

namespace backlands::world {
namespace {

using game::EntityRegistry;
using protocol::LoginFailure;

// O cliente recebe o motivo como byte: o que o registro produz e o que vai no
// fio precisam ter o mesmo valor, senão a mensagem mostrada estaria errada.
static_assert(static_cast<int>(EntityRegistry::SpawnError::None) ==
              static_cast<int>(LoginFailure::None));
static_assert(static_cast<int>(EntityRegistry::SpawnError::InvalidName) ==
              static_cast<int>(LoginFailure::InvalidName));
static_assert(static_cast<int>(EntityRegistry::SpawnError::NameInUse) ==
              static_cast<int>(LoginFailure::NameInUse));
static_assert(static_cast<int>(EntityRegistry::SpawnError::AlreadySpawned) ==
              static_cast<int>(LoginFailure::AlreadySpawned));

} // namespace

// S_ENTER_WORLD: a identidade e a posição que o client deve assumir.
std::vector<std::byte> make_enter_world_frame(const game::Entity& entity,
                                              std::string_view map_name) {
    protocol::PacketWriter writer(protocol::to_raw(protocol::ServerOpcode::EnterWorld));
    writer.write_u32(entity.id);
    writer.write_u16(entity.cell.x);
    writer.write_u16(entity.cell.y);
    writer.write_u8(static_cast<std::uint8_t>(entity.facing));
    writer.write_i32(entity.stats.hp);
    writer.write_i32(entity.stats.max_hp);
    // SP só viaja aqui: é recurso do próprio jogador, os outros não o veem.
    writer.write_i32(entity.stats.sp);
    writer.write_i32(entity.stats.max_sp);
    writer.write_string(entity.sprite);
    writer.write_string(map_name);
    writer.write_string(entity.name);
    return writer.finish();
}

// S_ENTITY_SPAWN: id, célula, direção, tipo, vida e nome.
std::vector<std::byte> make_spawn_frame(const game::Entity& entity) {
    protocol::PacketWriter writer(protocol::to_raw(protocol::ServerOpcode::EntitySpawn));
    writer.write_u32(entity.id);
    writer.write_u16(entity.cell.x);
    writer.write_u16(entity.cell.y);
    writer.write_u8(static_cast<std::uint8_t>(entity.facing));
    writer.write_u8(static_cast<std::uint8_t>(entity.kind));
    writer.write_i32(entity.stats.hp);
    writer.write_i32(entity.stats.max_hp);
    writer.write_string(entity.sprite);
    writer.write_string(entity.name);
    return writer.finish();
}

std::vector<std::byte> make_despawn_frame(game::EntityId id) {
    protocol::PacketWriter writer(protocol::to_raw(protocol::ServerOpcode::EntityDespawn));
    writer.write_u32(id);
    return writer.finish();
}

// S_MAP_INFO: dimensões e nome; as células vêm depois, em S_MAP_ROW.
std::vector<std::byte> make_map_info_frame(const game::MapGrid& grid, std::string_view map_name) {
    protocol::PacketWriter info(protocol::to_raw(protocol::ServerOpcode::MapInfo));
    info.write_u16(grid.width());
    info.write_u16(grid.height());
    info.write_string(map_name);
    return info.finish();
}

// S_MAP_ROW: uma linha do grid, um byte por célula.
std::vector<std::byte> make_map_row_frame(const game::MapGrid& grid, std::uint16_t y) {
    const auto cells = grid.row_bytes(y);

    protocol::PacketWriter row(protocol::to_raw(protocol::ServerOpcode::MapRow));
    row.write_u16(y);
    row.write_u16(static_cast<std::uint16_t>(cells.size()));
    for (const std::uint8_t cell : cells) {
        row.write_u8(cell);
    }
    return row.finish();
}

// S_ENTITY_WALK: a partir de onde, a que velocidade e por quais direções.
// Enviar o caminho inteiro em um pacote (e não célula por célula) é o modelo
// do Ragnarok: o client interpola localmente as mesmas células que o servidor
// vai percorrer, sem tráfego por passo.
std::vector<std::byte> make_walk_frame(const game::Entity& entity) {
    const game::WalkState& walk = entity.walk;
    const std::size_t remaining = walk.path.size() - walk.next_step;

    protocol::PacketWriter writer(protocol::to_raw(protocol::ServerOpcode::EntityWalk));
    writer.write_u32(entity.id);
    writer.write_u16(entity.cell.x);
    writer.write_u16(entity.cell.y);
    writer.write_u16(walk.speed_ms_per_cell);
    writer.write_u8(static_cast<std::uint8_t>(remaining));
    for (std::size_t i = walk.next_step; i < walk.path.size(); ++i) {
        writer.write_u8(static_cast<std::uint8_t>(walk.path[i]));
    }
    return writer.finish();
}

// S_ENTITY_STOP: posição autoritativa em que a entidade realmente parou.
// Sem isto, um client que interpolava o caminho antigo ficaria dessincronizado
// quando o servidor interrompe a caminhada.
std::vector<std::byte> make_stop_frame(const game::Entity& entity) {
    protocol::PacketWriter writer(protocol::to_raw(protocol::ServerOpcode::EntityStop));
    writer.write_u32(entity.id);
    writer.write_u16(entity.cell.x);
    writer.write_u16(entity.cell.y);
    writer.write_u8(static_cast<std::uint8_t>(entity.facing));
    return writer.finish();
}

// S_ENTITY_ATTACK: quem bateu, em quem, quanto, e a vida que sobrou.
// O HP resultante vai junto para o client não precisar acumular dano — se um
// pacote se perdesse, a barra de vida ficaria errada para sempre.
std::vector<std::byte> make_attack_frame(const game::CombatEvent& event) {
    protocol::PacketWriter writer(protocol::to_raw(protocol::ServerOpcode::EntityAttack));
    writer.write_u32(event.source);
    writer.write_u32(event.target);
    // Posição e direção do atacante: o client trata isto como parada implícita
    // e para de percorrer o trajeto que o servidor já abandonou ao golpear.
    writer.write_u16(event.source_cell.x);
    writer.write_u16(event.source_cell.y);
    writer.write_u8(static_cast<std::uint8_t>(event.source_facing));
    // Duração real do golpe, para a animação caber no tempo certo em vez de
    // usar uma taxa fixa que só serviria para um tipo de entidade.
    writer.write_u16(event.attack_motion_ms);
    writer.write_i32(event.damage);
    writer.write_i32(event.hp);
    return writer.finish();
}

std::vector<std::byte> make_died_frame(const game::CombatEvent& event) {
    protocol::PacketWriter writer(protocol::to_raw(protocol::ServerOpcode::EntityDied));
    writer.write_u32(event.target);
    writer.write_u32(event.source);
    // Onde o corpo caiu. Morrer no meio de uma caminhada não gera parada, e
    // sem isto o cadáver continuaria deslizando até o destino antigo.
    writer.write_u16(event.cell.x);
    writer.write_u16(event.cell.y);
    return writer.finish();
}

std::vector<std::byte> make_respawn_frame(const game::Entity& entity) {
    protocol::PacketWriter writer(protocol::to_raw(protocol::ServerOpcode::EntityRespawn));
    writer.write_u32(entity.id);
    writer.write_u16(entity.cell.x);
    writer.write_u16(entity.cell.y);
    writer.write_u8(static_cast<std::uint8_t>(entity.facing));
    writer.write_i32(entity.stats.hp);
    return writer.finish();
}

std::vector<std::byte> make_login_failed_frame(protocol::LoginFailure reason) {
    protocol::PacketWriter writer(protocol::to_raw(protocol::ServerOpcode::LoginFailed));
    writer.write_u8(static_cast<std::uint8_t>(reason));
    return writer.finish();
}

std::vector<std::byte> make_attack_rejected_frame(game::AttackRejection rejection) {
    protocol::PacketWriter writer(protocol::to_raw(protocol::ServerOpcode::AttackRejected));
    writer.write_u8(static_cast<std::uint8_t>(rejection));
    return writer.finish();
}

// S_PONG: devolve o nonce do C_PING.
std::vector<std::byte> make_pong_frame(std::uint32_t nonce) {
    protocol::PacketWriter writer(protocol::to_raw(protocol::ServerOpcode::Pong));
    writer.write_u32(nonce);
    return writer.finish();
}

protocol::LoginFailure to_login_failure(game::EntityRegistry::SpawnError error) noexcept {
    return static_cast<protocol::LoginFailure>(error);
}

} // namespace backlands::world
