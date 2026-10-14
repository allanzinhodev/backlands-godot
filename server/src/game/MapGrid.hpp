#pragma once

#include "game/Geometry.hpp"

#include <cstddef>
#include <cstdint>
#include <vector>

namespace backlands::game {

// Tipo de terreno de uma célula. Serve ao visual do client; quem decide
// passagem é sempre a flag `walkable`, não o terreno.
enum class Terrain : std::uint8_t {
    Grass = 0,
    Water = 1,
    Rock = 2,
    Bridge = 3,
    Dirt = 4,
};

inline constexpr std::uint8_t kTerrainCount = 5;

// Uma célula empacotada em 1 byte, no formato que vai no fio:
//   bit 0      -> caminhável
//   bit 1      -> bloqueia linha de visão
//   bits 4..7  -> tipo de terreno
//
// Um byte por célula mantém o mapa pequeno o suficiente para ser enviado ao
// client, o que elimina a chance de o visual divergir do grid lógico.
struct CellFlags {
    static constexpr std::uint8_t kWalkable = 0x01;
    static constexpr std::uint8_t kBlocksLos = 0x02;
    static constexpr std::uint8_t kTerrainShift = 4;

    std::uint8_t raw = 0;

    [[nodiscard]] bool walkable() const noexcept { return (raw & kWalkable) != 0; }
    [[nodiscard]] bool blocks_los() const noexcept { return (raw & kBlocksLos) != 0; }
    [[nodiscard]] Terrain terrain() const noexcept {
        return static_cast<Terrain>(raw >> kTerrainShift);
    }

    static CellFlags make(bool walkable, bool blocks_los, Terrain terrain) noexcept {
        CellFlags flags;
        flags.raw = static_cast<std::uint8_t>(
            (walkable ? kWalkable : 0) | (blocks_los ? kBlocksLos : 0) |
            (static_cast<std::uint8_t>(terrain) << kTerrainShift));
        return flags;
    }
};

// Grid lógico de um mapa: a fonte de verdade sobre o que é caminhável.
// Dados puros + consultas O(1), sem Asio e sem Lua, para ser testável a seco.
class MapGrid {
public:
    MapGrid() = default;
    MapGrid(std::uint16_t width, std::uint16_t height, std::vector<CellFlags> cells);

    [[nodiscard]] std::uint16_t width() const noexcept { return width_; }
    [[nodiscard]] std::uint16_t height() const noexcept { return height_; }
    [[nodiscard]] bool empty() const noexcept { return cells_.empty(); }

    [[nodiscard]] bool in_bounds(Cell cell) const noexcept {
        return cell.x < width_ && cell.y < height_;
    }

    // Fora do mapa nunca é caminhável: assim um destino inventado pelo client
    // é recusado pela mesma checagem que recusa parede.
    [[nodiscard]] bool is_walkable(Cell cell) const noexcept {
        return in_bounds(cell) && cells_[index(cell)].walkable();
    }

    [[nodiscard]] bool blocks_los(Cell cell) const noexcept {
        return !in_bounds(cell) || cells_[index(cell)].blocks_los();
    }

    [[nodiscard]] CellFlags at(Cell cell) const noexcept {
        return in_bounds(cell) ? cells_[index(cell)] : CellFlags{};
    }

    // Uma linha crua, para enviar ao client sem recodificar célula a célula.
    [[nodiscard]] std::vector<std::uint8_t> row_bytes(std::uint16_t y) const;

private:
    [[nodiscard]] std::size_t index(Cell cell) const noexcept {
        return static_cast<std::size_t>(cell.y) * width_ + cell.x;
    }

    std::uint16_t width_ = 0;
    std::uint16_t height_ = 0;
    std::vector<CellFlags> cells_;
};

} // namespace backlands::game
