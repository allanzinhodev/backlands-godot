#pragma once

#include "game/Geometry.hpp"
#include "game/MapGrid.hpp"

namespace backlands::game {

// Há linha de visão entre duas células? Percorre a reta entre elas e falha se
// alguma célula do meio bloqueia visão. As pontas não contam: estar em cima de
// uma pedra não deve cegar quem está nela.
//
// É o que impede um monstro de enxergar o jogador através de uma parede.
[[nodiscard]] bool has_line_of_sight(const MapGrid& grid, Cell from, Cell to);

} // namespace backlands::game
