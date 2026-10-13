#include "game/World.hpp"

#include "core/Log.hpp"
#include "game/Combat.hpp"
#include "game/Movement.hpp"
#include "game/MonsterAi.hpp"
#include "game/Pathfinder.hpp"
#include "net/Opcodes.hpp"
#include "net/PacketWriter.hpp"

#include <iterator>
#include <utility>

namespace backlands::game {
namespace {

// S_ENTITY_SPAWN: id, célula, direção, tipo, vida e nome.
std::vector<std::byte> make_spawn_frame(const Entity& entity) {
    net::PacketWriter writer(net::to_raw(net::ServerOpcode::EntitySpawn));
    writer.write_u32(entity.id);
    writer.write_u16(entity.cell.x);
    writer.write_u16(entity.cell.y);
    writer.write_u8(static_cast<std::uint8_t>(entity.facing));
    writer.write_u8(static_cast<std::uint8_t>(entity.kind));
    writer.write_i32(entity.stats.hp);
    writer.write_i32(entity.stats.max_hp);
    writer.write_string(entity.name);
    return writer.finish();
}

// S_ENTITY_ATTACK: quem bateu, em quem, quanto, e a vida que sobrou.
// O HP resultante vai junto para o client não precisar acumular dano — se um
// pacote se perdesse, a barra de vida ficaria errada para sempre.
std::vector<std::byte> make_attack_frame(const CombatEvent& event) {
    net::PacketWriter writer(net::to_raw(net::ServerOpcode::EntityAttack));
    writer.write_u32(event.source);
    writer.write_u32(event.target);
    writer.write_i32(event.damage);
    writer.write_i32(event.hp);
    return writer.finish();
}

std::vector<std::byte> make_died_frame(const CombatEvent& event) {
    net::PacketWriter writer(net::to_raw(net::ServerOpcode::EntityDied));
    writer.write_u32(event.target);
    writer.write_u32(event.source);
    return writer.finish();
}

std::vector<std::byte> make_respawn_frame(const Entity& entity) {
    net::PacketWriter writer(net::to_raw(net::ServerOpcode::EntityRespawn));
    writer.write_u32(entity.id);
    writer.write_u16(entity.cell.x);
    writer.write_u16(entity.cell.y);
    writer.write_i32(entity.stats.hp);
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
    spawn_map_monsters();
}

void World::spawn_map_monsters() {
    int total = 0;

    for (const MonsterSpawn& spawn : map_.spawns) {
        const MonsterTemplate* tmpl = map_.find_monster(spawn.species);
        if (tmpl == nullptr) {
            continue; // o carregador já teria recusado; defesa redundante
        }

        for (std::uint16_t i = 0; i < spawn.count; ++i) {
            // Espalha em torno do ponto para não nascerem empilhados. Se o
            // sorteio cair em água ou parede, usa a célula do ponto, que o
            // carregador já garantiu ser caminhável.
            Cell cell = spawn.cell;
            for (int attempt = 0; attempt < 8; ++attempt) {
                const int dx = rng_.range(-static_cast<int>(spawn.spread), spawn.spread);
                const int dy = rng_.range(-static_cast<int>(spawn.spread), spawn.spread);
                const int x = static_cast<int>(spawn.cell.x) + dx;
                const int y = static_cast<int>(spawn.cell.y) + dy;
                if (x < 0 || y < 0) {
                    continue;
                }
                const Cell candidate{static_cast<std::uint16_t>(x), static_cast<std::uint16_t>(y)};
                if (map_.grid.is_walkable(candidate)) {
                    cell = candidate;
                    break;
                }
            }

            Entity& monster = entities_.spawn_monster(tmpl->name, cell);
            monster.stats.max_hp = tmpl->max_hp;
            monster.stats.hp = tmpl->max_hp;
            monster.stats.attack_min = tmpl->attack_min;
            monster.stats.attack_max = tmpl->attack_max;
            monster.stats.defense_percent = tmpl->defense_percent;
            monster.stats.defense_flat = tmpl->defense_flat;
            monster.stats.hit = tmpl->hit;
            monster.stats.flee = tmpl->flee;
            monster.stats.attack_motion_ms = tmpl->attack_motion_ms;
            monster.stats.attack_range = tmpl->attack_range;
            monster.stats.attack_delay_ms = tmpl->attack_delay_ms;
            monster.walk.speed_ms_per_cell = tmpl->walk_speed_ms;

            monster.brain.species = tmpl->species;
            monster.brain.home = cell;
            monster.brain.view_range = tmpl->view_range;
            monster.brain.chase_range = tmpl->chase_range;
            monster.brain.respawn_delay_ms = tmpl->respawn_delay_ms;

            ++total;
        }
    }

    if (total > 0) {
        log::info("{} monstro(s) nasceram em {} ponto(s) de spawn", total, map_.spawns.size());
    }
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
    writer.write_i32(entity->stats.hp);
    writer.write_i32(entity->stats.max_hp);
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
        log::info("sessão {} / entidade {}: destino ({}, {}) recusado", session.id(), entity->id,
                  destination.x, destination.y);
        // O client já tem o grid e pode dar o feedback visual. Mandar o stop
        // devolve a posição autoritativa, caso ele tenha começado a animar
        // otimisticamente.
        session.send(make_stop_frame(*entity));
        return true;
    }

    const std::size_t steps = path.size();
    if (!begin_walk(*entity, std::move(path), destination, world_time_ms_)) {
        return true;
    }

    log::info("sessão {} / entidade {}: ({}, {}) -> ({}, {}) em {} passo(s)", session.id(),
              entity->id, entity->cell.x, entity->cell.y, destination.x, destination.y, steps);

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

bool World::request_attack(net::Session& session, EntityId target_id) {
    Entity* attacker = entities_.find_by_session(session.id());
    if (attacker == nullptr) {
        log::warn("sessão {}: C_ATTACK antes de entrar no mundo", session.id());
        return false;
    }

    const Entity* target = entities_.find(target_id);

    // Valida com alcance frouxo aqui: o atacante pode estar longe e querer
    // perseguir, que é o modelo do Ragnarok. O alcance de verdade é checado a
    // cada golpe, no tick.
    AttackRejection rejection = AttackRejection::None;
    if (target == nullptr) {
        rejection = AttackRejection::NoSuchTarget;
    }
    else if (target->id == attacker->id) {
        rejection = AttackRejection::SelfTarget;
    }
    else if (!attacker->alive()) {
        rejection = AttackRejection::AttackerDead;
    }
    else if (!target->alive()) {
        rejection = AttackRejection::TargetDead;
    }

    if (rejection != AttackRejection::None) {
        net::PacketWriter writer(net::to_raw(net::ServerOpcode::AttackRejected));
        writer.write_u8(static_cast<std::uint8_t>(rejection));
        session.send(writer.finish());
        return true;
    }

    attacker->combat.target = target_id;
    log::info("entidade {} mira na entidade {}", attacker->id, target_id);
    return true;
}

void World::advance_chasing() {
    for (Entity* entity : entities_.all_mutable()) {
        const EntityId target_id = entity->combat.target;
        if (target_id == kNoEntity || !entity->alive()) {
            continue;
        }

        // Chegou ao alcance no meio do caminho: corta o resto da rota e avisa
        // todos. Sem isto o atacante andaria até a célula onde o alvo ESTAVA,
        // passando por ele sem golpear.
        if (entity->walk.walking()) {
            const Entity* chasing = entities_.find(target_id);
            if (chasing != nullptr && chasing->alive() && in_attack_range(*entity, *chasing)) {
                entity->walk.clear();
                broadcast(make_stop_frame(*entity));
            }
            continue;
        }

        const Entity* target = entities_.find(target_id);
        if (target == nullptr || !target->alive()) {
            continue;
        }
        if (in_attack_range(*entity, *target)) {
            continue;
        }

        // Nota: quem já está andando não entra aqui (checado acima). Isso é
        // deliberado — recalcular o caminho a cada tick daria um movimento
        // nervoso; o atacante se compromete com a rota e só reavalia ao chegar.

        // Anda até uma célula vizinha do alvo, não até a célula DELE: duas
        // entidades não ocupam a mesma célula, e pedir a célula ocupada daria
        // caminho vazio toda vez.
        Cell destination = target->cell;
        WalkPath path = find_path(map_.grid, entity->cell, destination);
        if (path.empty()) {
            for (std::uint8_t i = 0; i < kDirectionCount && path.empty(); ++i) {
                const Cell adjacent = step(target->cell, static_cast<Direction>(i));
                if (map_.grid.is_walkable(adjacent)) {
                    path = find_path(map_.grid, entity->cell, adjacent);
                    destination = adjacent;
                }
            }
        }
        if (path.empty()) {
            continue;
        }

        if (begin_walk(*entity, std::move(path), destination, world_time_ms_)) {
            broadcast(make_walk_frame(*entity));
        }
    }
}

void World::publish_combat_events() {
    for (const CombatEvent& event : advance_combat(entities_, world_time_ms_, rng_)) {
        switch (event.type) {
            case CombatEvent::Type::Hit: {
                broadcast(make_attack_frame(event));

                // Monstro que apanha passa a revidar, inclusive os passivos:
                // é o que diferencia "não me procure" de "não reaja a nada".
                Entity* victim = entities_.find(event.target);
                if (victim != nullptr && victim->is_monster() && victim->alive()) {
                    victim->brain.retaliate_against = event.source;
                }
                break;
            }

            case CombatEvent::Type::Died: {
                broadcast(make_died_frame(event));

                Entity* dead = entities_.find(event.target);
                if (dead != nullptr) {
                    const std::uint32_t delay = dead->is_monster()
                                                    ? dead->brain.respawn_delay_ms
                                                    : kPlayerRespawnDelayMs;
                    dead->combat.respawn_at_ms = world_time_ms_ + delay;
                    log::info("entidade {} ('{}') morreu para a entidade {}", dead->id,
                              dead->name, event.source);
                }

                // Quem estava mirando no morto para de mirar, senão ficaria
                // perseguindo um cadáver até ele renascer.
                for (Entity* other : entities_.all_mutable()) {
                    if (other->combat.target == event.target) {
                        other->combat.clear_target();
                    }
                }
                break;
            }

            case CombatEvent::Type::Respawn:
                break;
        }
    }
}

void World::advance_monsters() {
    for (const WanderIntent& intent : advance_monster_ai(entities_, map_.grid, world_time_ms_,
                                                         rng_)) {
        Entity* monster = entities_.find(intent.id);
        if (monster == nullptr || monster->walk.walking()) {
            continue;
        }

        WalkPath path = find_path(map_.grid, monster->cell, intent.destination);
        if (path.empty()) {
            continue;
        }
        if (begin_walk(*monster, std::move(path), intent.destination, world_time_ms_)) {
            broadcast(make_walk_frame(*monster));
        }
    }
}

void World::advance_respawns() {
    for (Entity* entity : entities_.all_mutable()) {
        if (entity->alive() || entity->combat.respawn_at_ms == 0) {
            continue;
        }
        if (world_time_ms_ < entity->combat.respawn_at_ms) {
            continue;
        }

        entity->combat.respawn_at_ms = 0;
        entity->stats.hp = entity->stats.max_hp;
        entity->cell = entity->is_monster() ? entity->brain.home : map_.spawn;
        entity->facing = Direction::South;
        entity->walk.clear();
        entity->combat.clear_target();

        if (entity->is_monster()) {
            // Zera o rancor: sem isto um monstro passivo renasce já atacando
            // quem o matou antes, sem ter sido provocado de novo.
            entity->brain.retaliate_against = kNoEntity;
            entity->brain.next_wander_at_ms = world_time_ms_ + kIdleWanderIntervalMs;
        }

        broadcast(make_respawn_frame(*entity));
        log::info("entidade {} ('{}') renasceu em ({}, {})", entity->id, entity->name,
                  entity->cell.x, entity->cell.y);
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

    publish_combat_events();
    advance_monsters();
    advance_chasing();
    advance_respawns();
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
