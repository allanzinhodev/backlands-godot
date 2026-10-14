#include "world/World.hpp"

#include "core/Log.hpp"
#include "game/Chase.hpp"
#include "game/Combat.hpp"
#include "game/MapLoader.hpp"
#include "game/MonsterAi.hpp"
#include "game/MonsterSpawner.hpp"
#include "game/Movement.hpp"
#include "game/Pathfinder.hpp"
#include "game/Respawn.hpp"
#include "world/Replication.hpp"

#include <vector>

namespace backlands::world {

void World::load_map(const std::string& lua_path) {
    map_ = game::load_map_from_lua(lua_path);
    log::info("mapa '{}' carregado: {}x{}, spawn em ({}, {})", map_.name, map_.grid.width(),
              map_.grid.height(), map_.spawn.x, map_.spawn.y);
    game::spawn_map_monsters(entities_, map_, rng_);
}

void World::add_connection(const ConnectionPtr& connection) {
    connections_.add(connection);
    log::info("sessão {} ({}) conectou — {} conectada(s)", connection->id(), connection->remote(),
              connections_.size());
}

void World::remove_connection(ConnectionId id) {
    // A entidade sai do mundo junto com a sessão, e os outros jogadores
    // precisam saber — senão fica um personagem fantasma na tela deles.
    const game::EntityId entity_id = entities_.despawn_session(id);
    if (entity_id != game::kNoEntity) {
        connections_.broadcast(make_despawn_frame(entity_id), id);
        log::info("entidade {} saiu do mundo", entity_id);
    }

    if (connections_.remove(id)) {
        log::info("sessão {} desconectou — {} conectada(s)", id, connections_.size());
    }
}

game::EntityRegistry::SpawnError World::try_login(Connection& connection, std::string_view name) {
    const auto result = entities_.spawn_player(connection.id(), name, map_.spawn);

    if (!result.ok()) {
        connection.send(make_login_failed_frame(to_login_failure(result.error)));

        log::warn("sessão {}: login recusado (motivo {})", connection.id(),
                  static_cast<int>(result.error));
        return result.error;
    }

    game::Entity* spawned = entities_.find(result.id);
    spawned->sprite = player_sprite_;

    const game::Entity* entity = spawned;

    // O mapa vai antes do resto: o client precisa do grid para desenhar o
    // cenário e para dar feedback de célula inválida sob o cursor.
    send_map(connection);

    // S_ENTER_WORLD: a identidade e a posição que o client deve assumir. Quem
    // decide a célula é o servidor, nunca o client.
    connection.send(make_enter_world_frame(*entity, map_.name));

    // Quem entra precisa ver quem já estava; quem já estava precisa ver quem
    // entrou. São os dois lados do mesmo estado.
    send_existing_entities(connection, entity->id);
    connections_.broadcast(make_spawn_frame(*entity), connection.id());

    log::info("entidade {} ('{}') entrou em {} na célula ({}, {})", entity->id, entity->name,
              map_.name, entity->cell.x, entity->cell.y);
    return game::EntityRegistry::SpawnError::None;
}

bool World::request_walk(Connection& connection, game::Cell destination) {
    game::Entity* entity = entities_.find_by_session(connection.id());
    if (entity == nullptr) {
        // Pedir para andar sem ter entrado no mundo é violação de protocolo:
        // nenhum client correto faz isso.
        log::warn("sessão {}: C_WALK_TO antes de entrar no mundo", connection.id());
        return false;
    }

    // Andar cancela o ataque em curso, como no Ragnarok: mover é uma
    // decisão do jogador que substitui a anterior.
    entity->combat.clear_target();

    // Toda a validação está no find_path: destino fora do mapa, destino em
    // água ou parede, e rota longa demais devolvem caminho vazio. O client não
    // tem como forçar posição — ele só sugere um destino.
    game::WalkPath path = game::find_path(map_.grid, entity->cell, destination);
    if (path.empty()) {
        log::info("sessão {} / entidade {}: destino ({}, {}) recusado", connection.id(),
                  entity->id, destination.x, destination.y);
        // O client já tem o grid e pode dar o feedback visual. Mandar o stop
        // devolve a posição autoritativa, caso ele tenha começado a animar
        // otimisticamente.
        connection.send(make_stop_frame(*entity));
        return true;
    }

    const std::size_t steps = path.size();
    if (!game::begin_walk(*entity, std::move(path), destination, world_time_ms_)) {
        return true;
    }

    log::info("sessão {} / entidade {}: ({}, {}) -> ({}, {}) em {} passo(s)", connection.id(),
              entity->id, entity->cell.x, entity->cell.y, destination.x, destination.y, steps);

    // Todos veem a caminhada, inclusive quem pediu: a origem do movimento é
    // sempre a posição autoritativa do servidor.
    connections_.broadcast(make_walk_frame(*entity));
    return true;
}

void World::send_map(Connection& connection) {
    connection.send(make_map_info_frame(map_.grid, map_.name));

    // Uma linha por pacote: mantém cada frame bem abaixo do teto e escala para
    // mapas maiores sem mudar o protocolo.
    for (std::uint16_t y = 0; y < map_.grid.height(); ++y) {
        connection.send(make_map_row_frame(map_.grid, y));
    }
}

void World::send_existing_entities(Connection& connection, game::EntityId skip_id) {
    for (const game::Entity* entity : entities_.all()) {
        if (entity->id == skip_id) {
            continue;
        }
        connection.send(make_spawn_frame(*entity));

        // Quem já estava andando precisa aparecer andando, senão ficaria
        // parado na tela de quem entrou até terminar o trajeto.
        if (entity->walk.walking()) {
            connection.send(make_walk_frame(*entity));
        }
    }
}

bool World::request_attack(Connection& connection, game::EntityId target_id) {
    game::Entity* attacker = entities_.find_by_session(connection.id());
    if (attacker == nullptr) {
        log::warn("sessão {}: C_ATTACK antes de entrar no mundo", connection.id());
        return false;
    }

    const game::Entity* target = entities_.find(target_id);

    // Valida com alcance frouxo aqui: o atacante pode estar longe e querer
    // perseguir, que é o modelo do Ragnarok. O alcance de verdade é checado a
    // cada golpe, no tick.
    const game::AttackRejection rejection = game::validate_attack_target(*attacker, target);

    if (rejection != game::AttackRejection::None) {
        connection.send(make_attack_rejected_frame(rejection));
        return true;
    }

    attacker->combat.target = target_id;
    log::info("entidade {} mira na entidade {}", attacker->id, target_id);
    return true;
}

void World::tick(std::uint32_t delta_ms) {
    ++tick_count_;
    world_time_ms_ += delta_ms;

    // Ordem fixa: cada etapa enxerga o que a anterior decidiu neste mesmo
    // tick — o monstro que acabou de apanhar já pensa em revidar, e o alvo
    // que a IA acabou de escolher já começa a ser perseguido.
    publish_movement_events();
    publish_combat_events();
    publish_monster_walks();
    publish_chase_events();
    publish_respawns();
    sweep_closed_connections();
}

void World::publish_movement_events() {
    for (const game::MovementEvent& event :
         game::advance_movement(entities_, map_.grid, world_time_ms_)) {
        if (event.outcome != game::MovementOutcome::Blocked) {
            // Chegada não precisa de pacote: o client percorreu o mesmo
            // caminho e para na mesma célula.
            continue;
        }
        const game::Entity* entity = entities_.find(event.id);
        if (entity != nullptr) {
            connections_.broadcast(make_stop_frame(*entity));
            log::info("entidade {} parou em ({}, {}): caminho bloqueado", entity->id,
                      entity->cell.x, entity->cell.y);
        }
    }
}

void World::publish_combat_events() {
    const std::vector<game::CombatEvent> events =
        game::advance_combat(entities_, world_time_ms_, rng_);

    for (const game::CombatEvent& event : events) {
        switch (event.type) {
            case game::CombatEvent::Type::Hit:
                connections_.broadcast(make_attack_frame(event));
                break;

            case game::CombatEvent::Type::Died:
                connections_.broadcast(make_died_frame(event));
                break;

            case game::CombatEvent::Type::Respawn:
                break;
        }
    }

    // As consequências de jogo (revide, respawn agendado, alvos largados) vêm
    // depois de replicar todos os eventos. Os frames saem só dos dados do
    // evento, e não do estado das entidades, então os bytes e a ordem dos
    // pacotes são os mesmos de aplicar evento a evento.
    game::react_to_combat(entities_, events, world_time_ms_);
}

void World::publish_monster_walks() {
    const std::vector<game::WanderIntent> intents =
        game::advance_monster_ai(entities_, map_.grid, world_time_ms_, rng_);

    for (const game::EntityId id :
         game::start_wander_walks(entities_, map_.grid, intents, world_time_ms_)) {
        if (const game::Entity* monster = entities_.find(id)) {
            connections_.broadcast(make_walk_frame(*monster));
        }
    }
}

void World::publish_chase_events() {
    // Cada entidade aparece no máximo uma vez e só ela é mexida na sua vez,
    // então replicar depois da rodada inteira dá os mesmos frames de replicar
    // no meio dela.
    for (const game::ChaseEvent& event :
         game::advance_chasing(entities_, map_.grid, world_time_ms_)) {
        const game::Entity* entity = entities_.find(event.id);
        if (entity == nullptr) {
            continue;
        }
        switch (event.type) {
            case game::ChaseEvent::Type::ReachedTarget:
                connections_.broadcast(make_stop_frame(*entity));
                break;

            case game::ChaseEvent::Type::StartedWalk:
                connections_.broadcast(make_walk_frame(*entity));
                break;
        }
    }
}

void World::publish_respawns() {
    for (const game::EntityId id : game::advance_respawns(entities_, map_.spawn, world_time_ms_)) {
        if (const game::Entity* entity = entities_.find(id)) {
            connections_.broadcast(make_respawn_frame(*entity));
        }
    }
}

void World::sweep_closed_connections() {
    // Sessões fechadas são varridas aqui, e não durante a iteração de
    // broadcast, para não invalidar iteradores no meio de um envio.
    for (const ConnectionId id : connections_.take_closed()) {
        const game::EntityId entity_id = entities_.despawn_session(id);
        if (entity_id != game::kNoEntity) {
            connections_.broadcast(make_despawn_frame(entity_id), id);
            log::info("entidade {} removida (sessão {} fechada)", entity_id, id);
        }
    }
}

} // namespace backlands::world
