#include "game/Geometry.hpp"

#include <algorithm>
#include <cstdlib>

namespace backlands::game {

Cell step(Cell from, Direction dir) noexcept {
    const auto& delta = kDirectionDeltas[static_cast<std::uint8_t>(dir)];
    return Cell{static_cast<std::uint16_t>(from.x + delta.dx),
                static_cast<std::uint16_t>(from.y + delta.dy)};
}

Direction direction_towards(Cell from, Cell to) noexcept {
    const int dx = static_cast<int>(to.x) - static_cast<int>(from.x);
    const int dy = static_cast<int>(to.y) - static_cast<int>(from.y);

    const int sx = (dx > 0) ? 1 : (dx < 0 ? -1 : 0);
    const int sy = (dy > 0) ? 1 : (dy < 0 ? -1 : 0);

    for (std::uint8_t i = 0; i < kDirectionCount; ++i) {
        if (kDirectionDeltas[i].dx == sx && kDirectionDeltas[i].dy == sy) {
            return static_cast<Direction>(i);
        }
    }
    return Direction::South; // from == to
}

int cell_distance(Cell a, Cell b) noexcept {
    const int dx = std::abs(static_cast<int>(a.x) - static_cast<int>(b.x));
    const int dy = std::abs(static_cast<int>(a.y) - static_cast<int>(b.y));
    return std::max(dx, dy);
}

} // namespace backlands::game
