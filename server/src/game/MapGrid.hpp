#pragma once

#include "game/Entity.hpp"

#include <cstddef>
#include <cstdint>
#include <string>
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

// Modelo de um monstro, vindo do arquivo de mapa.
struct MonsterTemplate {
    std::string species;
    std::string name;

    // Folha de sprites. Vazio = usa a própria species como chave.
    std::string sprite;
    std::int32_t max_hp = 50;
    std::int32_t attack_min = 7;
    std::int32_t attack_max = 10;
    std::int32_t defense_percent = 0;
    std::int32_t defense_flat = 0;
    std::int32_t hit = 80;
    std::int32_t flee = 60;
    std::uint16_t attack_range = 1;
    std::uint16_t attack_delay_ms = 1872;
    std::uint16_t attack_motion_ms = 672;
    std::uint16_t walk_speed_ms = 400;

    // 0 = passivo: não inicia combate, só revida.
    std::uint16_t view_range = 0;
    std::uint16_t chase_range = 12;
    std::uint32_t respawn_delay_ms = 5000;
};

// Um ponto de nascimento de monstros no mapa.
struct MonsterSpawn {
    std::string species;
    Cell cell;
    std::uint16_t count = 1;

    // Raio em que os monstros se espalham em torno da célula, para não
    // nascerem todos empilhados na mesma posição.
    std::uint16_t spread = 3;
};

// Mapa carregado: grid + metadados.
struct MapDefinition {
    std::string name;
    Cell spawn;
    MapGrid grid;
    std::vector<MonsterTemplate> monsters;
    std::vector<MonsterSpawn> spawns;

    [[nodiscard]] const MonsterTemplate* find_monster(std::string_view species) const noexcept;
};

// Carrega um mapa de um arquivo Lua. Lança std::runtime_error com mensagem
// legível em qualquer inconsistência: subir com mapa torto é pior que não
// subir, porque o erro só apareceria como jogador atravessando parede.
[[nodiscard]] MapDefinition load_map_from_lua(const std::string& path);

} // namespace backlands::game
