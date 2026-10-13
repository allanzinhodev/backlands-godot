#include "game/World.hpp"

#include "core/Log.hpp"
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

} // namespace

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
    const auto result = entities_.spawn_player(session.id(), name, kSpawnCell);

    if (!result.ok()) {
        net::PacketWriter writer(net::to_raw(net::ServerOpcode::LoginFailed));
        writer.write_u8(static_cast<std::uint8_t>(result.error));
        session.send(writer.finish());

        log::warn("sessão {}: login recusado (motivo {})", session.id(),
                  static_cast<int>(result.error));
        return result.error;
    }

    const Entity* entity = entities_.find(result.id);

    // S_ENTER_WORLD: a identidade e a posição que o client deve assumir. Quem
    // decide a célula é o servidor, nunca o client.
    net::PacketWriter writer(net::to_raw(net::ServerOpcode::EnterWorld));
    writer.write_u32(entity->id);
    writer.write_u16(entity->cell.x);
    writer.write_u16(entity->cell.y);
    writer.write_u8(static_cast<std::uint8_t>(entity->facing));
    writer.write_string(kMapName);
    writer.write_string(entity->name);
    session.send(writer.finish());

    // Quem entra precisa ver quem já estava; quem já estava precisa ver quem
    // entrou. São os dois lados do mesmo estado.
    send_existing_entities(session, entity->id);
    broadcast(make_spawn_frame(*entity), session.id());

    log::info("entidade {} ('{}') entrou em {} na célula ({}, {})", entity->id, entity->name,
              kMapName, entity->cell.x, entity->cell.y);
    return EntityRegistry::SpawnError::None;
}

void World::send_existing_entities(net::Session& session, EntityId skip_id) {
    for (const Entity* entity : entities_.all()) {
        if (entity->id == skip_id) {
            continue;
        }
        session.send(make_spawn_frame(*entity));
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

void World::tick(std::uint32_t /*delta_ms*/) {
    ++tick_count_;

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
