#include "game/MapGrid.hpp"
#include "game/Pathfinder.hpp"

#include <catch2/catch_test_macros.hpp>

#include <string>
#include <string_view>
#include <vector>

using namespace backlands::game;

namespace {

// Monta um grid a partir de arte ASCII, como os arquivos de mapa:
//   '.' caminhável, '#' sólido (bloqueia visão), '~' água (não caminhável).
MapGrid grid_from(std::initializer_list<std::string_view> rows) {
    std::vector<CellFlags> cells;
    std::size_t width = 0;

    for (const auto row : rows) {
        if (width == 0) {
            width = row.size();
        }
        REQUIRE(row.size() == width);
        for (const char c : row) {
            switch (c) {
                case '.':
                    cells.push_back(CellFlags::make(true, false, Terrain::Grass));
                    break;
                case '=':
                    cells.push_back(CellFlags::make(true, false, Terrain::Bridge));
                    break;
                case '~':
                    cells.push_back(CellFlags::make(false, false, Terrain::Water));
                    break;
                case '#':
                    cells.push_back(CellFlags::make(false, true, Terrain::Rock));
                    break;
                default:
                    FAIL("símbolo desconhecido no grid de teste");
            }
        }
    }

    return MapGrid(static_cast<std::uint16_t>(width),
                   static_cast<std::uint16_t>(rows.size()), std::move(cells));
}

// Percorre o caminho e devolve a célula final, validando cada passo.
Cell walk_path(const MapGrid& grid, Cell from, const WalkPath& path) {
    Cell cursor = from;
    for (const Direction dir : path.steps) {
        CHECK(can_step_diagonally(grid, cursor, dir));
        cursor = step(cursor, dir);
        CHECK(grid.is_walkable(cursor));
    }
    return cursor;
}

} // namespace

TEST_CASE("consulta de célula respeita limites do mapa") {
    const auto grid = grid_from({
        "...",
        ".#.",
        "...",
    });

    CHECK(grid.width() == 3);
    CHECK(grid.height() == 3);

    CHECK(grid.is_walkable(Cell{0, 0}));
    CHECK_FALSE(grid.is_walkable(Cell{1, 1}));

    // Fora do mapa nunca é caminhável: é a mesma checagem que recusa parede,
    // então um destino inventado pelo client cai aqui.
    CHECK_FALSE(grid.is_walkable(Cell{3, 0}));
    CHECK_FALSE(grid.is_walkable(Cell{0, 3}));
    CHECK_FALSE(grid.is_walkable(Cell{65535, 65535}));
    CHECK_FALSE(grid.in_bounds(Cell{3, 3}));
}

TEST_CASE("terreno e linha de visão vêm empacotados no mesmo byte") {
    const auto grid = grid_from({
        ".~#",
    });

    CHECK(grid.at(Cell{0, 0}).terrain() == Terrain::Grass);
    CHECK(grid.at(Cell{1, 0}).terrain() == Terrain::Water);
    CHECK(grid.at(Cell{2, 0}).terrain() == Terrain::Rock);

    // Água não bloqueia visão (dá para atacar de uma margem à outra);
    // pedra bloqueia.
    CHECK_FALSE(grid.blocks_los(Cell{1, 0}));
    CHECK(grid.blocks_los(Cell{2, 0}));

    // Fora do mapa bloqueia visão: não se vê através da borda.
    CHECK(grid.blocks_los(Cell{9, 9}));
}

TEST_CASE("linha do grid serializa uma célula por byte") {
    const auto grid = grid_from({
        ".~",
        "#.",
    });

    const auto row0 = grid.row_bytes(0);
    REQUIRE(row0.size() == 2);
    CHECK((row0[0] & CellFlags::kWalkable) != 0);
    CHECK((row0[1] & CellFlags::kWalkable) == 0);

    CHECK(grid.row_bytes(99).empty());
}

TEST_CASE("caminho reto em terreno livre") {
    const auto grid = grid_from({
        ".....",
        ".....",
        ".....",
    });

    const auto path = find_path(grid, Cell{0, 1}, Cell{4, 1});
    REQUIRE_FALSE(path.empty());
    CHECK(path.size() == 4);
    CHECK(walk_path(grid, Cell{0, 1}, path) == Cell{4, 1});
}

TEST_CASE("caminho usa diagonal quando ela é livre") {
    const auto grid = grid_from({
        ".....",
        ".....",
        ".....",
    });

    // De (0,0) a (2,2): três passos diagonais custam menos que seis ortogonais.
    const auto path = find_path(grid, Cell{0, 0}, Cell{2, 2});
    REQUIRE_FALSE(path.empty());
    CHECK(path.size() == 2);
    CHECK(walk_path(grid, Cell{0, 0}, path) == Cell{2, 2});
}

TEST_CASE("destino igual à origem não gera caminho") {
    const auto grid = grid_from({"..."});
    CHECK(find_path(grid, Cell{1, 0}, Cell{1, 0}).empty());
}

TEST_CASE("destino não caminhável é recusado") {
    const auto grid = grid_from({
        ".~.",
        ".~.",
        ".~.",
    });

    SECTION("dentro da água") {
        CHECK(find_path(grid, Cell{0, 0}, Cell{1, 1}).empty());
    }
    SECTION("fora do mapa") {
        CHECK(find_path(grid, Cell{0, 0}, Cell{99, 99}).empty());
    }
}

TEST_CASE("margem oposta sem ponte é inalcançável") {
    // O rio corta o mapa inteiro: não existe rota.
    const auto grid = grid_from({
        ".~.",
        ".~.",
        ".~.",
    });

    CHECK(find_path(grid, Cell{0, 1}, Cell{2, 1}).empty());
}

TEST_CASE("o caminho desvia pela ponte em vez de atravessar o rio") {
    // Rio vertical em x=2 cortando o mapa INTEIRO, com ponte em (2,2).
    // Se sobrar uma linha livre no topo ou na base, a rota passa por lá e o
    // teste não prova nada sobre a ponte.
    const auto grid = grid_from({
        "..~..",
        "..~..",
        "..=..",
        "..~..",
        "..~..",
    });

    const auto path = find_path(grid, Cell{0, 0}, Cell{4, 0});
    REQUIRE_FALSE(path.empty());

    const Cell destination = walk_path(grid, Cell{0, 0}, path);
    CHECK(destination == Cell{4, 0});

    // A rota tem de passar pela ponte: é a única célula que liga as margens.
    Cell cursor{0, 0};
    bool crossed_bridge = false;
    for (const Direction dir : path.steps) {
        cursor = step(cursor, dir);
        if (cursor == Cell{2, 2}) {
            crossed_bridge = true;
        }
        // E nunca pode pisar na água.
        CHECK(grid.at(cursor).terrain() != Terrain::Water);
    }
    CHECK(crossed_bridge);
}

TEST_CASE("diagonal não corta o canto de uma parede") {
    // Para ir de (0,0) a (1,1) na diagonal seria preciso passar pela quina
    // entre (1,0) e (0,1); ambas sólidas.
    const auto grid = grid_from({
        ".#.",
        "#..",
        "...",
    });

    CHECK_FALSE(can_step_diagonally(grid, Cell{0, 0}, Direction::SouthEast));

    // Com apenas UMA das ortogonais livre ainda é proibido: senão o
    // personagem passaria raspando a quina da parede.
    const auto half_open = grid_from({
        ".#.",
        "...",
        "...",
    });
    CHECK_FALSE(can_step_diagonally(half_open, Cell{0, 0}, Direction::SouthEast));

    const auto open = grid_from({
        "...",
        "...",
        "...",
    });
    CHECK(can_step_diagonally(open, Cell{0, 0}, Direction::SouthEast));
}

TEST_CASE("passo ortogonal nunca é barrado pela regra de diagonal") {
    const auto grid = grid_from({
        ".#.",
        "#..",
        "...",
    });

    for (const auto dir : {Direction::South, Direction::North, Direction::East, Direction::West}) {
        CHECK(can_step_diagonally(grid, Cell{1, 1}, dir));
    }
}

TEST_CASE("caminho contorna obstáculo em U") {
    const auto grid = grid_from({
        ".......",
        ".#####.",
        ".#...#.",
        ".#.#.#.",
        ".....#.",
        "#######",
    });

    const auto path = find_path(grid, Cell{0, 0}, Cell{2, 2});
    REQUIRE_FALSE(path.empty());
    CHECK(walk_path(grid, Cell{0, 0}, path) == Cell{2, 2});
}

TEST_CASE("caminho acima do teto de células é recusado") {
    // Corredor reto com mais de kMaxWalkPath células.
    std::string corridor(kMaxWalkPath + 10, '.');
    const auto grid = grid_from({std::string_view(corridor)});

    const Cell from{0, 0};
    const Cell far{static_cast<std::uint16_t>(corridor.size() - 1), 0};

    // Sem teto, um destino distante deixaria o client ditar quanto trabalho o
    // servidor faz por pacote.
    CHECK(find_path(grid, from, far).empty());

    // Exatamente no limite ainda passa.
    const Cell edge{static_cast<std::uint16_t>(kMaxWalkPath), 0};
    const auto path = find_path(grid, from, edge);
    REQUIRE_FALSE(path.empty());
    CHECK(path.size() == kMaxWalkPath);
    CHECK(walk_path(grid, from, path) == edge);
}

TEST_CASE("origem sólida ainda produz caminho de saída") {
    // Um jogador pode terminar sobre terreno sólido (mapa recarregado, efeito
    // que o empurrou). Exigir origem caminhável o deixaria preso para sempre.
    // A origem é água, mas há célula livre adjacente por onde sair.
    const auto grid = grid_from({
        "~~~",
        "...",
        "...",
    });

    const auto path = find_path(grid, Cell{0, 0}, Cell{2, 2});
    REQUIRE_FALSE(path.empty());
    CHECK(walk_path(grid, Cell{0, 0}, path) == Cell{2, 2});
}

TEST_CASE("direções e seus deslocamentos são consistentes") {
    CHECK(step(Cell{5, 5}, Direction::North) == Cell{5, 4});
    CHECK(step(Cell{5, 5}, Direction::South) == Cell{5, 6});
    CHECK(step(Cell{5, 5}, Direction::East) == Cell{6, 5});
    CHECK(step(Cell{5, 5}, Direction::West) == Cell{4, 5});
    CHECK(step(Cell{5, 5}, Direction::NorthEast) == Cell{6, 4});
    CHECK(step(Cell{5, 5}, Direction::SouthWest) == Cell{4, 6});

    CHECK(is_diagonal(Direction::NorthEast));
    CHECK(is_diagonal(Direction::SouthWest));
    CHECK_FALSE(is_diagonal(Direction::North));
    CHECK_FALSE(is_diagonal(Direction::East));
}

TEST_CASE("direção calculada aponta para o destino") {
    CHECK(direction_towards(Cell{5, 5}, Cell{5, 0}) == Direction::North);
    CHECK(direction_towards(Cell{5, 5}, Cell{9, 5}) == Direction::East);
    CHECK(direction_towards(Cell{5, 5}, Cell{0, 9}) == Direction::SouthWest);
    CHECK(direction_towards(Cell{5, 5}, Cell{5, 5}) == Direction::South);
}

TEST_CASE("todo passo do caminho é um passo de uma célula") {
    const auto grid = grid_from({
        "........",
        "...##...",
        "........",
        "..##....",
        "........",
    });

    const auto path = find_path(grid, Cell{0, 0}, Cell{7, 4});
    REQUIRE_FALSE(path.empty());

    Cell cursor{0, 0};
    for (const Direction dir : path.steps) {
        const Cell next = step(cursor, dir);
        const int dx = std::abs(static_cast<int>(next.x) - static_cast<int>(cursor.x));
        const int dy = std::abs(static_cast<int>(next.y) - static_cast<int>(cursor.y));
        // Teletransporte no meio do caminho seria um bug grave de reconstrução.
        CHECK(dx <= 1);
        CHECK(dy <= 1);
        CHECK((dx + dy) > 0);
        cursor = next;
    }
    CHECK(cursor == Cell{7, 4});
}
