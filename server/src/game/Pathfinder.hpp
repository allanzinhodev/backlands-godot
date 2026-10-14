#pragma once

#include "game/Geometry.hpp"
#include "game/MapGrid.hpp"

#include <cstddef>
#include <cstdint>
#include <vector>

namespace backlands::game {

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

} // namespace backlands::game
