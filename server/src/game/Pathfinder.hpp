#pragma once

#include "game/Entity.hpp"
#include "game/MapGrid.hpp"

#include <cstdint>
#include <vector>

namespace backlands::game {

// Deslocamento de cada direção em células. Índice = valor de Direction.
struct DirectionDelta {
    std::int8_t dx;
    std::int8_t dy;
};

// y cresce para o sul (mesma orientação do eixo Z no mundo 3D).
inline constexpr DirectionDelta kDirectionDeltas[kDirectionCount] = {
    {0, 1},   // South
    {-1, 1},  // SouthWest
    {-1, 0},  // West
    {-1, -1}, // NorthWest
    {0, -1},  // North
    {1, -1},  // NorthEast
    {1, 0},   // East
    {1, 1},   // SouthEast
};

[[nodiscard]] constexpr bool is_diagonal(Direction dir) noexcept {
    return (static_cast<std::uint8_t>(dir) % 2) == 1;
}

// Caminho como sequência de direções, igual ao walkpath_data do eAthena:
// guardar direções em vez de células deixa o pacote de movimento pequeno e dá
// ao client exatamente a mesma sequência que o servidor vai percorrer.
struct WalkPath {
    std::vector<Direction> steps;

    [[nodiscard]] bool empty() const noexcept { return steps.empty(); }
    [[nodiscard]] std::size_t size() const noexcept { return steps.size(); }
};

// Teto de células por requisição de caminhada, como o MAX_WALKPATH do eAthena.
// É também proteção: sem limite, um destino distante num mapa grande deixaria
// o client ditar quanto trabalho o servidor faz por pacote.
inline constexpr std::size_t kMaxWalkPath = 32;

// Custos inteiros, como no rAthena: evita ponto flutuante no caminho crítico.
inline constexpr int kMoveCost = 10;
inline constexpr int kMoveDiagonalCost = 14; // ~10 * raiz(2)

// A* sobre o grid. Devolve caminho vazio se não houver rota, se o destino não
// for caminhável ou se a rota exceder kMaxWalkPath.
//
// A célula de origem NÃO é validada de propósito: se um jogador terminar em
// cima de terreno sólido (mapa recarregado, efeito que o empurrou), exigir
// origem caminhável o deixaria preso para sempre.
[[nodiscard]] WalkPath find_path(const MapGrid& grid, Cell from, Cell to);

// Caminhar na diagonal exige que as DUAS células ortogonais adjacentes estejam
// livres. Sem isso o personagem corta o canto de uma parede, passando pela
// quina — visualmente errado e explorável para atravessar barreiras.
[[nodiscard]] bool can_step_diagonally(const MapGrid& grid, Cell from, Direction dir);

// Aplica um passo. Não valida: quem chama decide com can_step/is_walkable.
[[nodiscard]] Cell step(Cell from, Direction dir) noexcept;

// Direção dominante de `from` para `to`, para virar o personagem.
[[nodiscard]] Direction direction_towards(Cell from, Cell to) noexcept;

// Há linha de visão entre duas células? Percorre a reta entre elas e falha se
// alguma célula do meio bloqueia visão. As pontas não contam: estar em cima de
// uma pedra não deve cegar quem está nela.
//
// É o que impede um monstro de enxergar o jogador através de uma parede.
[[nodiscard]] bool has_line_of_sight(const MapGrid& grid, Cell from, Cell to);

} // namespace backlands::game
