#include "game/Chase.hpp"

#include "game/Combat.hpp"
#include "game/Geometry.hpp"
#include "game/Movement.hpp"
#include "game/Pathfinder.hpp"

#include <utility>

namespace backlands::game {

std::vector<ChaseEvent> advance_chasing(EntityRegistry& registry, const MapGrid& grid,
                                        std::uint32_t now_ms) {
    std::vector<ChaseEvent> events;

    for (Entity* entity : registry.all_mutable()) {
        const EntityId target_id = entity->combat.target;
        if (target_id == kNoEntity || !entity->alive()) {
            continue;
        }

        // Chegou ao alcance no meio do caminho: corta o resto da rota e avisa
        // todos. Sem isto o atacante andaria até a célula onde o alvo ESTAVA,
        // passando por ele sem golpear.
        if (entity->walk.walking()) {
            const Entity* chasing = registry.find(target_id);
            if (chasing != nullptr && chasing->alive() && in_attack_range(*entity, *chasing)) {
                entity->walk.clear();
                // Vira para o alvo antes de anunciar a parada: sem isto a
                // entidade fica encarando a última direção em que andou até o
                // primeiro golpe corrigir.
                entity->facing = direction_towards(entity->cell, chasing->cell);
                events.push_back(ChaseEvent{entity->id, ChaseEvent::Type::ReachedTarget});
            }
            continue;
        }

        const Entity* target = registry.find(target_id);
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
        WalkPath path = find_path(grid, entity->cell, destination);
        if (path.empty()) {
            for (std::uint8_t i = 0; i < kDirectionCount && path.empty(); ++i) {
                const Cell adjacent = step(target->cell, static_cast<Direction>(i));
                if (grid.is_walkable(adjacent)) {
                    path = find_path(grid, entity->cell, adjacent);
                    destination = adjacent;
                }
            }
        }
        if (path.empty()) {
            continue;
        }

        if (begin_walk(*entity, std::move(path), destination, now_ms)) {
            events.push_back(ChaseEvent{entity->id, ChaseEvent::Type::StartedWalk});
        }
    }

    return events;
}

} // namespace backlands::game
