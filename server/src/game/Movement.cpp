#include "game/Movement.hpp"

#include <utility>

namespace backlands::game {

std::uint32_t step_duration_ms(std::uint16_t speed_ms_per_cell, Direction dir) noexcept {
    const std::uint32_t base = speed_ms_per_cell;
    // Diagonal percorre ~1.4 célula de distância, então leva mais tempo. A
    // razão 14/10 é a mesma do eAthena, em inteiros para não introduzir
    // ponto flutuante no caminho crítico.
    return is_diagonal(dir) ? (base * 14U) / 10U : base;
}

bool begin_walk(Entity& entity, WalkPath path, Cell destination, std::uint32_t now_ms) {
    if (path.empty()) {
        return false;
    }

    entity.walk.path = std::move(path.steps);
    entity.walk.next_step = 0;
    entity.walk.destination = destination;
    entity.walk.next_step_at_ms =
        now_ms + step_duration_ms(entity.walk.speed_ms_per_cell, entity.walk.path.front());
    return true;
}

void stop_walk(Entity& entity) noexcept {
    entity.walk.clear();
}

std::vector<MovementEvent> advance_movement(EntityRegistry& registry, const MapGrid& grid,
                                            std::uint32_t now_ms) {
    std::vector<MovementEvent> events;

    for (Entity* entity : registry.all_mutable()) {
        WalkState& walk = entity->walk;
        if (!walk.walking()) {
            continue;
        }

        int steps = 0;
        while (walk.walking() && walk.next_step_at_ms <= now_ms && steps < kMaxStepsPerAdvance) {
            const Direction dir = walk.path[walk.next_step];
            const Cell next = step(entity->cell, dir);

            // Revalidação: o caminho pode ter sido calculado antes de o
            // terreno mudar. Sem isto, um obstáculo novo seria atravessado.
            if (!grid.is_walkable(next) || !can_step_diagonally(grid, entity->cell, dir)) {
                walk.clear();
                events.push_back(
                    MovementEvent{entity->id, entity->cell, entity->facing,
                                  MovementOutcome::Blocked});
                break;
            }

            entity->cell = next;
            entity->facing = dir;
            ++walk.next_step;
            ++steps;

            if (!walk.walking()) {
                events.push_back(
                    MovementEvent{entity->id, entity->cell, entity->facing,
                                  MovementOutcome::Arrived});
                break;
            }

            walk.next_step_at_ms +=
                step_duration_ms(walk.speed_ms_per_cell, walk.path[walk.next_step]);
        }

        // Atingiu o teto de passos sem terminar: reancora o próximo passo no
        // agora, senão a entidade acumularia uma dívida de tempo e andaria em
        // rajadas nos avanços seguintes.
        if (steps == kMaxStepsPerAdvance && walk.walking() && walk.next_step_at_ms <= now_ms) {
            walk.next_step_at_ms = now_ms + step_duration_ms(walk.speed_ms_per_cell,
                                                             walk.path[walk.next_step]);
        }
    }

    return events;
}

} // namespace backlands::game
