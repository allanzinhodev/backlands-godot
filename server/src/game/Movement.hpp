#pragma once

#include "game/Entity.hpp"
#include "game/EntityRegistry.hpp"
#include "game/MapGrid.hpp"
#include "game/Pathfinder.hpp"

#include <cstdint>
#include <vector>

namespace backlands::game {

// O que aconteceu com uma entidade em movimento neste avanço do relógio.
enum class MovementOutcome : std::uint8_t {
    Arrived,  // chegou ao destino pedido
    Blocked,  // a próxima célula deixou de ser caminhável no meio do caminho
};

struct MovementEvent {
    EntityId id = kNoEntity;
    Cell cell;              // onde a entidade parou
    Direction facing = Direction::South;
    MovementOutcome outcome = MovementOutcome::Arrived;
};

// Teto de passos por entidade em um único avanço. Protege contra um atraso
// grande do loop virar um salto enorme, e contra laço infinito se a velocidade
// for muito baixa em relação ao tick.
inline constexpr int kMaxStepsPerAdvance = 8;

// Inicia uma caminhada. Devolve false se o caminho estiver vazio.
// `now_ms` é o relógio lógico do mundo, não o relógio do sistema: isso mantém
// o movimento determinístico e testável sem depender de tempo real.
bool begin_walk(Entity& entity, WalkPath path, Cell destination, std::uint32_t now_ms);

// Interrompe a caminhada no lugar (usado em morte, teleporte, stun).
void stop_walk(Entity& entity) noexcept;

// Avança todas as entidades cujo próximo passo já venceu. A posição
// autoritativa é sempre célula inteira: uma entidade pode dar 0, 1 ou vários
// passos num mesmo avanço, conforme a velocidade dela e o tempo decorrido.
//
// Cada passo é REVALIDADO contra o grid: o caminho foi calculado no passado e
// o terreno pode ter mudado (obstáculo dinâmico, outra entidade bloqueando).
[[nodiscard]] std::vector<MovementEvent> advance_movement(EntityRegistry& registry,
                                                          const MapGrid& grid,
                                                          std::uint32_t now_ms);

// Custo em milissegundos de um passo nesta direção.
[[nodiscard]] std::uint32_t step_duration_ms(std::uint16_t speed_ms_per_cell,
                                             Direction dir) noexcept;

} // namespace backlands::game
