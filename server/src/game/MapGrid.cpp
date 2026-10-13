#include "game/MapGrid.hpp"

#include <sol/sol.hpp>

#include <format>
#include <stdexcept>
#include <unordered_map>

namespace backlands::game {
namespace {

Terrain parse_terrain(const std::string& name, const std::string& where) {
    static const std::unordered_map<std::string, Terrain> kByName{
        {"grass", Terrain::Grass},
        {"water", Terrain::Water},
        {"rock", Terrain::Rock},
        {"bridge", Terrain::Bridge},
        {"dirt", Terrain::Dirt},
    };

    const auto it = kByName.find(name);
    if (it == kByName.end()) {
        throw std::runtime_error(
            std::format("mapa: terreno desconhecido '{}' em {}", name, where));
    }
    return it->second;
}

} // namespace

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

MapDefinition load_map_from_lua(const std::string& path) {
    sol::state lua;
    // Sem io/os: um arquivo de mapa não precisa de disco nem de processos.
    lua.open_libraries(sol::lib::base, sol::lib::math, sol::lib::string, sol::lib::table);

    const sol::load_result chunk = lua.load_file(path);
    if (!chunk.valid()) {
        const sol::error err = chunk;
        throw std::runtime_error(
            std::format("mapa: não foi possível carregar '{}': {}", path, err.what()));
    }

    const sol::protected_function_result result = chunk.get<sol::protected_function>()();
    if (!result.valid()) {
        const sol::error err = result;
        throw std::runtime_error(std::format("mapa: erro ao executar '{}': {}", path, err.what()));
    }

    const sol::optional<sol::table> maybe_root = result;
    if (!maybe_root.has_value()) {
        throw std::runtime_error(std::format("mapa: '{}' precisa retornar uma tabela", path));
    }
    const sol::table root = *maybe_root;

    MapDefinition definition;
    definition.name = root.get_or("name", std::string{});
    if (definition.name.empty()) {
        throw std::runtime_error("mapa: campo 'name' obrigatório");
    }

    // --- Legenda: caractere -> propriedades da célula ----------------------
    const sol::optional<sol::table> maybe_legend = root["legend"];
    if (!maybe_legend.has_value()) {
        throw std::runtime_error("mapa: tabela 'legend' obrigatória");
    }

    std::unordered_map<char, CellFlags> legend;
    for (const auto& [key, value] : *maybe_legend) {
        const auto symbol = key.as<std::string>();
        if (symbol.size() != 1) {
            throw std::runtime_error(
                std::format("mapa: símbolo de legenda '{}' precisa ter 1 caractere", symbol));
        }

        const sol::table entry = value.as<sol::table>();
        const auto terrain_name = entry.get_or("terrain", std::string{});
        if (terrain_name.empty()) {
            throw std::runtime_error(
                std::format("mapa: legenda '{}' sem campo 'terrain'", symbol));
        }

        const Terrain terrain = parse_terrain(terrain_name, std::format("legenda '{}'", symbol));
        // `walkable` precisa ser explícito: um default silencioso aqui deixaria
        // o jogador atravessando terreno que o autor do mapa achou sólido.
        const sol::optional<bool> walkable = entry["walkable"];
        if (!walkable.has_value()) {
            throw std::runtime_error(
                std::format("mapa: legenda '{}' precisa declarar 'walkable'", symbol));
        }
        const bool blocks_los = entry.get_or("blocks_los", false);

        legend.emplace(symbol[0], CellFlags::make(*walkable, blocks_los, terrain));
    }

    // --- Linhas: o mapa como arte ASCII -----------------------------------
    const sol::optional<sol::table> maybe_rows = root["rows"];
    if (!maybe_rows.has_value()) {
        throw std::runtime_error("mapa: tabela 'rows' obrigatória");
    }
    const sol::table rows = *maybe_rows;

    const auto height = rows.size();
    if (height == 0) {
        throw std::runtime_error("mapa: 'rows' está vazia");
    }

    std::vector<CellFlags> cells;
    std::size_t width = 0;

    for (std::size_t y = 1; y <= height; ++y) {
        const sol::optional<std::string> maybe_row = rows[y];
        if (!maybe_row.has_value()) {
            throw std::runtime_error(std::format("mapa: linha {} não é uma string", y));
        }
        const std::string& row = *maybe_row;

        if (y == 1) {
            width = row.size();
            if (width == 0) {
                throw std::runtime_error("mapa: primeira linha está vazia");
            }
            cells.reserve(width * height);
        }
        else if (row.size() != width) {
            // Linhas de tamanhos diferentes significariam um grid torto, com
            // células deslocadas em relação ao que o autor desenhou.
            throw std::runtime_error(std::format(
                "mapa: linha {} tem {} colunas, esperado {}", y, row.size(), width));
        }

        for (const char symbol : row) {
            const auto it = legend.find(symbol);
            if (it == legend.end()) {
                throw std::runtime_error(std::format(
                    "mapa: caractere '{}' na linha {} não está na legenda", symbol, y));
            }
            cells.push_back(it->second);
        }
    }

    if (width > 0xFFFF || height > 0xFFFF) {
        throw std::runtime_error("mapa: dimensões não cabem em uint16");
    }

    definition.grid = MapGrid(static_cast<std::uint16_t>(width),
                              static_cast<std::uint16_t>(height), std::move(cells));

    // --- Spawn -------------------------------------------------------------
    const sol::optional<sol::table> maybe_spawn = root["spawn"];
    if (!maybe_spawn.has_value()) {
        throw std::runtime_error("mapa: tabela 'spawn' obrigatória");
    }
    const sol::optional<int> spawn_x = (*maybe_spawn)["x"];
    const sol::optional<int> spawn_y = (*maybe_spawn)["y"];
    if (!spawn_x.has_value() || !spawn_y.has_value()) {
        throw std::runtime_error("mapa: 'spawn' precisa de x e y");
    }
    if (*spawn_x < 0 || *spawn_y < 0) {
        throw std::runtime_error("mapa: 'spawn' com coordenada negativa");
    }

    definition.spawn = Cell{static_cast<std::uint16_t>(*spawn_x),
                            static_cast<std::uint16_t>(*spawn_y)};

    // Spawn em célula sólida prenderia o jogador: falha agora, não em produção.
    if (!definition.grid.is_walkable(definition.spawn)) {
        throw std::runtime_error(std::format("mapa: spawn ({}, {}) não é caminhável",
                                            definition.spawn.x, definition.spawn.y));
    }

    return definition;
}

} // namespace backlands::game
