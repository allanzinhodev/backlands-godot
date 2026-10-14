#include "game/EntityRegistry.hpp"
#include "game/MapGrid.hpp"
#include "game/MapLoader.hpp"
#include "game/MonsterSpawner.hpp"
#include "game/Pathfinder.hpp"
#include "game/Rng.hpp"

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_string.hpp>

#include <algorithm>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <tuple>
#include <vector>

using namespace backlands::game;
using Catch::Matchers::ContainsSubstring;

namespace {

// Escreve um mapa Lua temporário e o remove no fim do teste.
class TempMap {
public:
    explicit TempMap(std::string_view contents) {
        static int counter = 0;
        path_ = std::filesystem::temp_directory_path() /
                ("backlands_map_test_" + std::to_string(++counter) + ".lua");
        std::ofstream file(path_);
        file << contents;
    }

    ~TempMap() {
        std::error_code ec;
        std::filesystem::remove(path_, ec);
    }

    TempMap(const TempMap&) = delete;
    TempMap& operator=(const TempMap&) = delete;

    [[nodiscard]] std::string path() const { return path_.string(); }

private:
    std::filesystem::path path_;
};

constexpr std::string_view kValidMap = R"(
return {
  name = "teste",
  spawn = { x = 1, y = 1 },
  legend = {
    ["."] = { terrain = "grass", walkable = true },
    ["~"] = { terrain = "water", walkable = false },
    ["#"] = { terrain = "rock",  walkable = false, blocks_los = true },
  },
  rows = {
    "###",
    "#.#",
    "###",
  },
}
)";

} // namespace

TEST_CASE("mapa Lua válido é carregado") {
    const TempMap map(kValidMap);
    const auto definition = load_map_from_lua(map.path());

    CHECK(definition.name == "teste");
    CHECK(definition.spawn == Cell{1, 1});
    CHECK(definition.grid.width() == 3);
    CHECK(definition.grid.height() == 3);
    CHECK(definition.grid.is_walkable(Cell{1, 1}));
    CHECK_FALSE(definition.grid.is_walkable(Cell{0, 0}));
}

TEST_CASE("mapa inválido falha no load, não em produção") {
    SECTION("arquivo inexistente") {
        CHECK_THROWS_WITH(load_map_from_lua("nao_existe.lua"),
                          ContainsSubstring("não foi possível carregar"));
    }

    SECTION("linhas de larguras diferentes") {
        // Um grid torto deslocaria as células em relação ao desenho.
        const TempMap map(R"(
            return {
              name = "torto", spawn = { x = 0, y = 0 },
              legend = { ["."] = { terrain = "grass", walkable = true } },
              rows = { "...", ".." },
            }
        )");
        CHECK_THROWS_WITH(load_map_from_lua(map.path()), ContainsSubstring("colunas"));
    }

    SECTION("caractere fora da legenda") {
        const TempMap map(R"(
            return {
              name = "x", spawn = { x = 0, y = 0 },
              legend = { ["."] = { terrain = "grass", walkable = true } },
              rows = { ".?." },
            }
        )");
        CHECK_THROWS_WITH(load_map_from_lua(map.path()), ContainsSubstring("não está na legenda"));
    }

    SECTION("legenda sem walkable explícito") {
        // Default silencioso aqui deixaria o jogador atravessando terreno que
        // o autor do mapa considerou sólido.
        const TempMap map(R"(
            return {
              name = "x", spawn = { x = 0, y = 0 },
              legend = { ["."] = { terrain = "grass" } },
              rows = { "..." },
            }
        )");
        CHECK_THROWS_WITH(load_map_from_lua(map.path()), ContainsSubstring("walkable"));
    }

    SECTION("terreno desconhecido") {
        const TempMap map(R"(
            return {
              name = "x", spawn = { x = 0, y = 0 },
              legend = { ["."] = { terrain = "lava", walkable = true } },
              rows = { "..." },
            }
        )");
        CHECK_THROWS_WITH(load_map_from_lua(map.path()), ContainsSubstring("terreno desconhecido"));
    }

    SECTION("spawn em célula sólida") {
        // Prenderia o jogador no instante em que ele entrasse.
        const TempMap map(R"(
            return {
              name = "x", spawn = { x = 0, y = 0 },
              legend = {
                ["."] = { terrain = "grass", walkable = true },
                ["#"] = { terrain = "rock", walkable = false },
              },
              rows = { "#..", "...", "..." },
            }
        )");
        CHECK_THROWS_WITH(load_map_from_lua(map.path()), ContainsSubstring("não é caminhável"));
    }

    SECTION("sem legenda") {
        const TempMap map("return { name = 'x', spawn = { x = 0, y = 0 }, rows = { '...' } }");
        CHECK_THROWS_AS(load_map_from_lua(map.path()), std::runtime_error);
    }

    SECTION("sem nome") {
        const TempMap map(R"(
            return {
              spawn = { x = 0, y = 0 },
              legend = { ["."] = { terrain = "grass", walkable = true } },
              rows = { "..." },
            }
        )");
        CHECK_THROWS_WITH(load_map_from_lua(map.path()), ContainsSubstring("name"));
    }
}

// --- O mapa real do repositório --------------------------------------------
// Estes testes guardam o arquivo que o servidor realmente carrega: editá-lo e
// quebrar o rio ou a ponte precisa falhar aqui, não no playtest.

TEST_CASE("starter_fields carrega e tem spawn em terra firme") {
    const auto definition = load_map_from_lua(BACKLANDS_REPO_MAP);

    CHECK(definition.name == "starter_fields");
    CHECK(definition.grid.width() == 32);
    CHECK(definition.grid.height() == 32);
    CHECK(definition.grid.is_walkable(definition.spawn));

    // A borda inteira é sólida: não se anda para fora do mapa.
    for (std::uint16_t x = 0; x < definition.grid.width(); ++x) {
        CHECK_FALSE(definition.grid.is_walkable(Cell{x, 0}));
        CHECK_FALSE(definition.grid.is_walkable(
            Cell{x, static_cast<std::uint16_t>(definition.grid.height() - 1)}));
    }
    for (std::uint16_t y = 0; y < definition.grid.height(); ++y) {
        CHECK_FALSE(definition.grid.is_walkable(Cell{0, y}));
        CHECK_FALSE(definition.grid.is_walkable(
            Cell{static_cast<std::uint16_t>(definition.grid.width() - 1), y}));
    }
}

TEST_CASE("o rio de starter_fields bloqueia e a ponte é a única passagem") {
    const auto definition = load_map_from_lua(BACKLANDS_REPO_MAP);
    const MapGrid& grid = definition.grid;

    SECTION("a água não é caminhável e não bloqueia visão") {
        CHECK_FALSE(grid.is_walkable(Cell{15, 10}));
        CHECK(grid.at(Cell{15, 10}).terrain() == Terrain::Water);
        CHECK_FALSE(grid.blocks_los(Cell{15, 10}));
    }

    SECTION("a ponte é caminhável") {
        CHECK(grid.is_walkable(Cell{15, 15}));
        CHECK(grid.is_walkable(Cell{15, 16}));
        CHECK(grid.at(Cell{15, 15}).terrain() == Terrain::Bridge);
    }

    SECTION("atravessar longe da ponte obriga um desvio bem mais longo") {
        // De uma margem à outra na altura y=5. Em linha reta seriam 19 passos,
        // mas o rio obriga a descer até a ponte (y=15/16) e voltar.
        const Cell from{6, 5};
        const Cell to{25, 5};
        const auto path = find_path(grid, from, to);
        REQUIRE_FALSE(path.empty());

        // Distância de Chebyshev: o número de passos se não houvesse rio.
        const auto direct = static_cast<std::size_t>(
            std::max(std::abs(static_cast<int>(to.x) - static_cast<int>(from.x)),
                     std::abs(static_cast<int>(to.y) - static_cast<int>(from.y))));
        CHECK(direct == 19);

        // O desvio precisa custar bem mais que a linha reta; se o caminho
        // tivesse o tamanho da reta, ele estaria atravessando a água.
        CHECK(path.size() > direct);

        Cell cursor = from;
        bool used_bridge = false;
        for (const Direction dir : path.steps) {
            cursor = step(cursor, dir);
            REQUIRE(grid.is_walkable(cursor));
            CHECK(grid.at(cursor).terrain() != Terrain::Water);
            if (grid.at(cursor).terrain() == Terrain::Bridge) {
                used_bridge = true;
            }
        }
        CHECK(cursor == to);
        CHECK(used_bridge);
    }

    SECTION("na altura da ponte a travessia funciona") {
        const auto path = find_path(grid, definition.spawn, Cell{25, 16});
        REQUIRE_FALSE(path.empty());

        // Confere passo a passo: nenhuma célula do caminho é água.
        Cell cursor = definition.spawn;
        bool used_bridge = false;
        for (const Direction dir : path.steps) {
            cursor = step(cursor, dir);
            REQUIRE(grid.is_walkable(cursor));
            if (grid.at(cursor).terrain() == Terrain::Bridge) {
                used_bridge = true;
            }
        }
        CHECK(cursor == Cell{25, 16});
        CHECK(used_bridge);
    }

    SECTION("pedras bloqueiam visão") {
        CHECK(grid.at(Cell{21, 6}).terrain() == Terrain::Rock);
        CHECK(grid.blocks_los(Cell{21, 6}));
        CHECK_FALSE(grid.is_walkable(Cell{21, 6}));
    }

    SECTION("destino dentro do rio é recusado") {
        CHECK(find_path(grid, definition.spawn, Cell{15, 20}).empty());
    }

    SECTION("destino fora do mapa é recusado") {
        CHECK(find_path(grid, definition.spawn, Cell{0, 0}).empty());
        CHECK(find_path(grid, definition.spawn, Cell{999, 999}).empty());
    }
}

TEST_CASE("os monstros de starter_fields nascem nas mesmas células a cada execução") {
    const auto definition = load_map_from_lua(BACKLANDS_REPO_MAP);

    int expected = 0;
    for (const MonsterSpawn& spawn : definition.spawns) {
        expected += spawn.count;
    }
    REQUIRE(expected > 0);

    // Povoa um registro novo e devolve (id, x, y) de cada monstro, ordenado
    // por id: a ordem de iteração do registro não é a de criação.
    const auto populate = [&definition, expected](std::uint32_t seed) {
        EntityRegistry registry;
        Rng rng{seed};
        CHECK(spawn_map_monsters(registry, definition, rng) == expected);

        std::vector<std::tuple<EntityId, std::uint16_t, std::uint16_t>> placed;
        for (const Entity* monster : registry.all()) {
            // Em terra firme, com a casa onde nasceu e a ficha da espécie.
            CHECK(definition.grid.is_walkable(monster->cell));
            CHECK(monster->brain.home == monster->cell);
            const MonsterTemplate* tmpl = definition.find_monster(monster->brain.species);
            REQUIRE(tmpl != nullptr);
            CHECK(monster->name == tmpl->name);
            CHECK(monster->stats.hp == tmpl->max_hp);
            CHECK(monster->stats.max_hp == tmpl->max_hp);
            CHECK(monster->walk.speed_ms_per_cell == tmpl->walk_speed_ms);
            CHECK(monster->brain.respawn_delay_ms == tmpl->respawn_delay_ms);
            CHECK(monster->sprite == (tmpl->sprite.empty() ? tmpl->species : tmpl->sprite));
            placed.emplace_back(monster->id, monster->cell.x, monster->cell.y);
        }
        std::sort(placed.begin(), placed.end());
        return placed;
    };

    // A semente fixa do World: com ela o mundo é o mesmo a cada execução, o
    // que torna um playtest comparável com o anterior.
    const auto first = populate(0xB4C41A1Du);
    CHECK(first.size() == static_cast<std::size_t>(expected));
    CHECK(populate(0xB4C41A1Du) == first);
}
