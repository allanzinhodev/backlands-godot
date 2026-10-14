#include "game/LineOfSight.hpp"

#include <cstdlib>

namespace backlands::game {

bool has_line_of_sight(const MapGrid& grid, Cell from, Cell to) {
    // Bresenham sobre células. Só as células INTERMEDIÁRIAS bloqueiam: incluir
    // as pontas faria quem está sobre terreno que bloqueia visão ficar cego.
    int x0 = from.x;
    int y0 = from.y;
    const int x1 = to.x;
    const int y1 = to.y;

    const int dx = std::abs(x1 - x0);
    const int dy = -std::abs(y1 - y0);
    const int sx = x0 < x1 ? 1 : -1;
    const int sy = y0 < y1 ? 1 : -1;
    int error = dx + dy;

    while (true) {
        if (x0 == x1 && y0 == y1) {
            return true;
        }

        const int doubled = 2 * error;
        if (doubled >= dy) {
            error += dy;
            x0 += sx;
        }
        if (doubled <= dx) {
            error += dx;
            y0 += sy;
        }

        if (x0 == x1 && y0 == y1) {
            return true;
        }
        if (grid.blocks_los(Cell{static_cast<std::uint16_t>(x0), static_cast<std::uint16_t>(y0)})) {
            return false;
        }
    }
}

} // namespace backlands::game
