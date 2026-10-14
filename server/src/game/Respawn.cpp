#include "game/Respawn.hpp"

#include "core/Log.hpp"
#include "game/MonsterAi.hpp"

namespace backlands::game {

void schedule_respawn(Entity& dead, std::uint32_t now_ms) noexcept {
    const std::uint32_t delay = dead.is_monster() ? dead.brain.respawn_delay_ms
                                                  : kPlayerRespawnDelayMs;
    dead.combat.respawn_at_ms = now_ms + delay;
}

std::vector<EntityId> advance_respawns(EntityRegistry& registry, Cell player_spawn,
                                       std::uint32_t now_ms) {
    std::vector<EntityId> respawned;

    for (Entity* entity : registry.all_mutable()) {
        if (entity->alive() || entity->combat.respawn_at_ms == 0) {
            continue;
        }
        if (now_ms < entity->combat.respawn_at_ms) {
            continue;
        }

        entity->combat.respawn_at_ms = 0;
        entity->stats.hp = entity->stats.max_hp;
        entity->stats.sp = entity->stats.max_sp;
        entity->cell = entity->is_monster() ? entity->brain.home : player_spawn;
        entity->facing = Direction::South;
        entity->walk.clear();
        entity->combat.clear_target();

        if (entity->is_monster()) {
            // Zera o rancor: sem isto um monstro passivo renasce já atacando
            // quem o matou antes, sem ter sido provocado de novo.
            entity->brain.retaliate_against = kNoEntity;
            entity->brain.next_wander_at_ms = now_ms + kIdleWanderIntervalMs;
        }

        respawned.push_back(entity->id);
        log::info("entidade {} ('{}') renasceu em ({}, {})", entity->id, entity->name,
                  entity->cell.x, entity->cell.y);
    }

    return respawned;
}

} // namespace backlands::game
