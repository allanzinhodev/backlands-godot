#include "game/MapGrid.hpp"

#include <format>
#include <stdexcept>
#include <utility>

namespace backlands::game {

MapGrid::MapGrid(std::uint16_t width, std::uint16_t height, std::vector<CellFlags> cells)
    : width_(width), height_(height), cells_(std::move(cells)) {
    const auto expected = static_cast<std::size_t>(width_) * height_;
    if (cells_.size() != expected) {
        throw std::runtime_error(std::format(
            "mapa: {} células para um grid {}x{} (esperado {})", cells_.size(), width_, height_,
            expected));
    }
}

std::vector<std::uint8_t> MapGrid::row_bytes(std::uint16_t y) const {
    if (y >= height_) {
        return {};
    }
    std::vector<std::uint8_t> row;
    row.reserve(width_);
    for (std::uint16_t x = 0; x < width_; ++x) {
        row.push_back(cells_[index(Cell{x, y})].raw);
    }
    return row;
}

} // namespace backlands::game
