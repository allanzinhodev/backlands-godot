#include "game/MonsterAi.hpp"

#include "game/Geometry.hpp"
#include "game/LineOfSight.hpp"
#include "game/Movement.hpp"
#include "game/Pathfinder.hpp"

#include <algorithm>
#include <utility>

namespace backlands::game {
namespace {

// Abaixo desta distância a linha de visão não é exigida: coladinho, o monstro
// percebe o jogador mesmo com uma quina no caminho. É o que o
// battle_check_range do eAthena faz.
constexpr int kLineOfSightFreeDistance = 2;

bool can_see(const MapGrid& grid, Cell from, Cell to) {
    if (cell_distance(from, to) < kLineOfSightFreeDistance) {
        return true;
    }
    return has_line_of_sight(grid, from, to);
}

bool any_player_near(const EntityRegistry& registry, Cell cell, int range) {
    for (const Entity* entity : registry.all()) {
        if (entity->is_player() && entity->alive() && cell_distance(cell, entity->cell) <= range) {
            return true;
        }
    }
    return false;
}

} // namespace

const Entity* find_nearest_player(const EntityRegistry& registry, const MapGrid& grid, Cell from,
                                  int max_distance) {
    const Entity* best = nullptr;
    int best_distance = max_distance + 1;

    for (const Entity* entity : registry.all()) {
        if (!entity->is_player() || !entity->alive()) {
            continue;
        }
        const int distance = cell_distance(from, entity->cell);
        if (distance >= best_distance) {
            continue;
        }
        // Linha de visão impede o monstro de enxergar através de uma parede.
        if (!can_see(grid, from, entity->cell)) {
            continue;
        }
        best_distance = distance;
        best = entity;
    }
    return best;
}

std::vector<WanderIntent> advance_monster_ai(EntityRegistry& registry, const MapGrid& grid,
                                             std::uint32_t now_ms, Rng& rng) {
    std::vector<WanderIntent> wanders;

    for (Entity* monster : registry.all_mutable()) {
        if (!monster->is_monster() || !monster->alive()) {
            continue;
        }
        if (now_ms < monster->brain.next_think_at_ms) {
            continue;
        }

        monster->brain.next_think_at_ms = now_ms + kMonsterThinkIntervalMs;

        // --- Já tem alvo: decide se mantém -------------------------------
        // Esta checagem roda ANTES do portão de atividade: largar um alvo que
        // morreu ou sumiu é barato, e adiá-la deixaria o monstro preso a um
        // alvo inválido enquanto não houvesse jogador por perto.
        if (monster->combat.target != kNoEntity) {
            const Entity* target = registry.find(monster->combat.target);

            // Desiste se o alvo morreu, sumiu, fugiu longe demais ou saiu de
            // vista. A distância é medida do MONSTRO ao alvo, não de casa:
            // medir de casa criaria um vai-e-vem, porque o monstro largaria o
            // alvo, voltaria, e o readquiriria no mesmo lugar.
            const bool lost = target == nullptr || !target->alive() ||
                              cell_distance(monster->cell, target->cell) >
                                  monster->brain.chase_range ||
                              !can_see(grid, monster->cell, target->cell);

            if (lost) {
                monster->combat.clear_target();
                monster->brain.retaliate_against = kNoEntity;
                // Fica ocioso onde está. Sem coleira de volta para casa: ela
                // exigiria um estado "voltando, ignore agressão", senão o
                // monstro ping-pongaria entre a casa e o jogador.
                monster->brain.next_wander_at_ms = now_ms + kIdleWanderIntervalMs;
            }
            continue;
        }

        // Portão de atividade: sem ninguém por perto, o monstro não procura
        // alvo nem passeia. O custo da IA passa a crescer com o que está em
        // jogo, não com o tamanho do mapa. (O eAthena ainda faz os distantes
        // passearem a 1 Hz; omitimos de propósito — com poucos jogadores
        // ninguém observa a diferença.)
        if (!any_player_near(registry, monster->cell, kMonsterActiveRange)) {
            continue;
        }

        // --- Sem alvo: agressivo procura, passivo só revida --------------
        if (monster->brain.view_range > 0) {
            const Entity* prey =
                find_nearest_player(registry, grid, monster->cell, monster->brain.view_range);
            if (prey != nullptr) {
                monster->combat.target = prey->id;
                continue;
            }
        }

        if (monster->brain.retaliate_against != kNoEntity) {
            const Entity* aggressor = registry.find(monster->brain.retaliate_against);
            if (aggressor != nullptr && aggressor->alive() &&
                cell_distance(monster->cell, aggressor->cell) <= monster->brain.chase_range) {
                monster->combat.target = aggressor->id;
                continue;
            }
            monster->brain.retaliate_against = kNoEntity;
        }

        // --- Ocioso: passeia em intervalos, não por sorteio a cada tick ---
        if (monster->walk.walking() || now_ms < monster->brain.next_wander_at_ms) {
            continue;
        }

        bool wandered = false;
        for (int attempt = 0; attempt < 3 && !wandered; ++attempt) {
            const int dx = rng.range(-kIdleWanderRadius, kIdleWanderRadius);
            const int dy = rng.range(-kIdleWanderRadius, kIdleWanderRadius);
            if (dx == 0 && dy == 0) {
                continue;
            }

            const int x = static_cast<int>(monster->cell.x) + dx;
            const int y = static_cast<int>(monster->cell.y) + dy;
            if (x < 0 || y < 0) {
                continue;
            }

            const Cell destination{static_cast<std::uint16_t>(x), static_cast<std::uint16_t>(y)};
            if (!grid.is_walkable(destination)) {
                continue;
            }

            wanders.push_back(WanderIntent{monster->id, destination});
            wandered = true;
        }

        // Reagenda sempre, mesmo sem ter achado destino: deixar sem agendar
        // faria um monstro cercado de parede tentar a cada pensamento.
        monster->brain.next_wander_at_ms =
            now_ms + kIdleWanderIntervalMs +
            static_cast<std::uint32_t>(rng.range(0, static_cast<int>(kIdleWanderJitterMs)));
    }

    return wanders;
}

std::vector<EntityId> start_wander_walks(EntityRegistry& registry, const MapGrid& grid,
                                         const std::vector<WanderIntent>& intents,
                                         std::uint32_t now_ms) {
    std::vector<EntityId> started;

    for (const WanderIntent& intent : intents) {
        Entity* monster = registry.find(intent.id);
        if (monster == nullptr || monster->walk.walking()) {
            continue;
        }

        WalkPath path = find_path(grid, monster->cell, intent.destination);
        if (path.empty()) {
            continue;
        }
        if (begin_walk(*monster, std::move(path), intent.destination, now_ms)) {
            started.push_back(monster->id);
        }
    }

    return started;
}

} // namespace backlands::game
