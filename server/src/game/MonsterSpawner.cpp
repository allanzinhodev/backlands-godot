#include "game/MonsterSpawner.hpp"

#include "core/Log.hpp"

#include <cstdint>

namespace backlands::game {

int spawn_map_monsters(EntityRegistry& registry, const MapDefinition& map, Rng& rng) {
    int total = 0;

    for (const MonsterSpawn& spawn : map.spawns) {
        const MonsterTemplate* tmpl = map.find_monster(spawn.species);
        if (tmpl == nullptr) {
            continue; // o carregador já teria recusado; defesa redundante
        }

        for (std::uint16_t i = 0; i < spawn.count; ++i) {
            // Espalha em torno do ponto para não nascerem empilhados. Se o
            // sorteio cair em água ou parede, usa a célula do ponto, que o
            // carregador já garantiu ser caminhável.
            Cell cell = spawn.cell;
            for (int attempt = 0; attempt < 8; ++attempt) {
                const int dx = rng.range(-static_cast<int>(spawn.spread), spawn.spread);
                const int dy = rng.range(-static_cast<int>(spawn.spread), spawn.spread);
                const int x = static_cast<int>(spawn.cell.x) + dx;
                const int y = static_cast<int>(spawn.cell.y) + dy;
                if (x < 0 || y < 0) {
                    continue;
                }
                const Cell candidate{static_cast<std::uint16_t>(x), static_cast<std::uint16_t>(y)};
                if (map.grid.is_walkable(candidate)) {
                    cell = candidate;
                    break;
                }
            }

            Entity& monster = registry.spawn_monster(tmpl->name, cell);
            apply_monster_template(monster, *tmpl, cell);

            ++total;
        }
    }

    if (total > 0) {
        log::info("{} monstro(s) nasceram em {} ponto(s) de spawn", total, map.spawns.size());
    }
    return total;
}

void apply_monster_template(Entity& monster, const MonsterTemplate& tmpl, Cell home) {
    monster.stats.max_hp = tmpl.max_hp;
    monster.stats.hp = tmpl.max_hp;
    monster.stats.max_sp = tmpl.max_sp;
    monster.stats.sp = tmpl.max_sp;
    monster.stats.attack_min = tmpl.attack_min;
    monster.stats.attack_max = tmpl.attack_max;
    monster.stats.defense_percent = tmpl.defense_percent;
    monster.stats.defense_flat = tmpl.defense_flat;
    monster.stats.hit = tmpl.hit;
    monster.stats.flee = tmpl.flee;
    monster.stats.attack_motion_ms = tmpl.attack_motion_ms;
    monster.stats.attack_range = tmpl.attack_range;
    monster.stats.attack_delay_ms = tmpl.attack_delay_ms;
    monster.walk.speed_ms_per_cell = tmpl.walk_speed_ms;

    monster.sprite = tmpl.sprite.empty() ? tmpl.species : tmpl.sprite;
    monster.brain.species = tmpl.species;
    monster.brain.home = home;
    monster.brain.view_range = tmpl.view_range;
    monster.brain.chase_range = tmpl.chase_range;
    monster.brain.respawn_delay_ms = tmpl.respawn_delay_ms;
}

} // namespace backlands::game
