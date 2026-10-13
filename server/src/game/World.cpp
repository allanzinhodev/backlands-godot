#include "game/World.hpp"

#include "core/Log.hpp"
#include "game/Movement.hpp"
#include "game/Pathfinder.hpp"
#include "net/Opcodes.hpp"
#include "net/PacketWriter.hpp"

#include <iterator>
#include <utility>

namespace backlands::game {
namespace {

// S_ENTITY_SPAWN: id, célula, direção e nome.
std::vector<std::byte> make_spawn_frame(const Entity& entity) {
    net::PacketWriter writer(net::to_raw(net::ServerOpcode::EntitySpawn));
    writer.write_u32(entity.id);
    writer.write_u16(entity.cell.x);
    writer.write_u16(entity.cell.y);
    writer.write_u8(static_cast<std::uint8_t>(entity.facing));
    writer.write_string(entity.name);
    return writer.finish();
}

std::vector<std::byte> make_despawn_frame(EntityId id) {
    net::PacketWriter writer(net::to_raw(net::ServerOpcode::EntityDespawn));
    writer.write_u32(id);
    return writer.finish();
}

// S_ENTITY_WALK: a partir de onde, a que velocidade e por quais direções.
// Enviar o caminho inteiro em um pacote (e não célula por célula) é o modelo
// do Ragnarok: o client interpola localmente as mesmas células que o servidor
// vai percorrer, sem tráfego por passo.
std::vector<std::byte> make_walk_frame(const Entity& entity) {
    const WalkState& walk = entity.walk;
    const std::size_t remaining = walk.path.size() - walk.next_step;

    net::PacketWriter writer(net::to_raw(net::ServerOpcode::EntityWalk));
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
std::vector<std::byte> make_stop_frame(const Entity& entity) {
    net::PacketWriter writer(net::to_raw(net::ServerOpcode::EntityStop));
    writer.write_u32(entity.id);
    writer.write_u16(entity.cell.x);
    writer.write_u16(entity.cell.y);
    writer.write_u8(static_cast<std::uint8_t>(entity.facing));
    return writer.finish();
}

} // namespace

void World::load_map(const std::string& lua_path) {
    map_ = load_map_from_lua(lua_path);
    log::info("mapa '{}' carregado: {}x{}, spawn em ({}, {})", map_.name, map_.grid.width(),
              map_.grid.height(), map_.spawn.x, map_.spawn.y);
}

void World::add_session(const net::SessionPtr& session) {
    sessions_[session->id()] = session;
    log::info("sessão {} ({}) conectou — {} conectada(s)", session->id(), session->remote(),
              sessions_.size());
}

void World::remove_session(net::SessionId id) {
    // A entidade sai do mundo junto com a sessão, e os outros jogadores
    // precisam saber — senão fica um personagem fantasma na tela deles.
    const EntityId entity_id = entities_.despawn_session(id);
    if (entity_id != kNoEntity) {
        broadcast(make_despawn_frame(entity_id), id);
        log::info("entidade {} saiu do mundo", entity_id);
    }

    if (sessions_.erase(id) > 0) {
        log::info("sessão {} desconectou — {} conectada(s)", id, sessions_.size());
    }
}

net::Session* World::find_session(net::SessionId id) noexcept {
    const auto it = sessions_.find(id);
    return it == sessions_.end() ? nullptr : it->second.get();
}

EntityRegistry::SpawnError World::try_login(net::Session& session, std::string_view name) {
    const auto result = entities_.spawn_player(session.id(), name, map_.spawn);

    if (!result.ok()) {
        net::PacketWriter writer(net::to_raw(net::ServerOpcode::LoginFailed));
        writer.write_u8(static_cast<std::uint8_t>(result.error));
        session.send(writer.finish());

        log::warn("sessão {}: login recusado (motivo {})", session.id(),
                  static_cast<int>(result.error));
        return result.error;
    }

    const Entity* entity = entities_.find(result.id);

    // O mapa vai antes do resto: o client precisa do grid para desenhar o
    // cenário e para dar feedback de célula inválida sob o cursor.
    send_map(session);

    // S_ENTER_WORLD: a identidade e a posição que o client deve assumir. Quem
    // decide a célula é o servidor, nunca o client.
    net::PacketWriter writer(net::to_raw(net::ServerOpcode::EnterWorld));
    writer.write_u32(entity->id);
    writer.write_u16(entity->cell.x);
    writer.write_u16(entity->cell.y);
    writer.write_u8(static_cast<std::uint8_t>(entity->facing));
    writer.write_string(map_.name);
    writer.write_string(entity->name);
    session.send(writer.finish());

    // Quem entra precisa ver quem já estava; quem já estava precisa ver quem
    // entrou. São os dois lados do mesmo estado.
    send_existing_entities(session, entity->id);
    broadcast(make_spawn_frame(*entity), session.id());

    log::info("entidade {} ('{}') entrou em {} na célula ({}, {})", entity->id, entity->name,
              map_.name, entity->cell.x, entity->cell.y);
    return EntityRegistry::SpawnError::None;
}

bool World::request_walk(net::Session& session, Cell destination) {
    Entity* entity = entities_.find_by_session(session.id());
    if (entity == nullptr) {
        // Pedir para andar sem ter entrado no mundo é violação de protocolo:
        // nenhum client correto faz isso.
        log::warn("sessão {}: C_WALK_TO antes de entrar no mundo", session.id());
        return false;
    }

    // Toda a validação está no find_path: destino fora do mapa, destino em
    // água ou parede, e rota longa demais devolvem caminho vazio. O client não
    // tem como forçar posição — ele só sugere um destino.
    WalkPath path = find_path(map_.grid, entity->cell, destination);
    if (path.empty()) {
        // Recusa silenciosa: o client já sabe o grid e pode dar o feedback.
        // Mandar o stop devolve a posição autoritativa, caso ele tenha
        // começado a animar otimisticamente.
        session.send(make_stop_frame(*entity));
        return true;
    }

    if (!begin_walk(*entity, std::move(path), destination, world_time_ms_)) {
        return true;
    }

    // Todos veem a caminhada, inclusive quem pediu: a origem do movimento é
    // sempre a posição autoritativa do servidor.
    broadcast(make_walk_frame(*entity));
    return true;
}

void World::send_map(net::Session& session) {
    net::PacketWriter info(net::to_raw(net::ServerOpcode::MapInfo));
    info.write_u16(map_.grid.width());
    info.write_u16(map_.grid.height());
    info.write_string(map_.name);
    session.send(info.finish());

    // Uma linha por pacote: mantém cada frame bem abaixo do teto e escala para
    // mapas maiores sem mudar o protocolo.
    for (std::uint16_t y = 0; y < map_.grid.height(); ++y) {
        const auto cells = map_.grid.row_bytes(y);

        net::PacketWriter row(net::to_raw(net::ServerOpcode::MapRow));
        row.write_u16(y);
        row.write_u16(static_cast<std::uint16_t>(cells.size()));
        for (const std::uint8_t cell : cells) {
            row.write_u8(cell);
        }
        session.send(row.finish());
    }
}

void World::send_existing_entities(net::Session& session, EntityId skip_id) {
    for (const Entity* entity : entities_.all()) {
        if (entity->id == skip_id) {
            continue;
        }
        session.send(make_spawn_frame(*entity));

        // Quem já estava andando precisa aparecer andando, senão ficaria
        // parado na tela de quem entrou até terminar o trajeto.
        if (entity->walk.walking()) {
            session.send(make_walk_frame(*entity));
        }
    }
}

void World::broadcast(const std::vector<std::byte>& frame, net::SessionId except) {
    for (auto& [id, session] : sessions_) {
        if (id == except || session->closed()) {
            continue;
        }
        session->send(frame);
    }
}

void World::tick(std::uint32_t delta_ms) {
    ++tick_count_;
    world_time_ms_ += delta_ms;

    for (const MovementEvent& event : advance_movement(entities_, map_.grid, world_time_ms_)) {
        if (event.outcome != MovementOutcome::Blocked) {
            // Chegada não precisa de pacote: o client percorreu o mesmo
            // caminho e para na mesma célula.
            continue;
        }
        const Entity* entity = entities_.find(event.id);
        if (entity != nullptr) {
            broadcast(make_stop_frame(*entity));
            log::info("entidade {} parou em ({}, {}): caminho bloqueado", entity->id,
                      entity->cell.x, entity->cell.y);
        }
    }

    sweep_closed_sessions();
}

void World::sweep_closed_sessions() {
    // Sessões fechadas são varridas aqui, e não durante a iteração de
    // broadcast, para não invalidar iteradores no meio de um envio.
    for (auto it = sessions_.begin(); it != sessions_.end();) {
        if (!it->second->closed()) {
            ++it;
            continue;
        }
        const net::SessionId id = it->first;
        it = sessions_.erase(it);

        const EntityId entity_id = entities_.despawn_session(id);
        if (entity_id != kNoEntity) {
            broadcast(make_despawn_frame(entity_id), id);
            log::info("entidade {} removida (sessão {} fechada)", entity_id, id);
        }
    }
}

} // namespace backlands::game
