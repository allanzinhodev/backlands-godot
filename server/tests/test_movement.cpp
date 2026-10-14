#include "game/Chase.hpp"
#include "game/EntityRegistry.hpp"
#include "game/MapGrid.hpp"
#include "game/Movement.hpp"
#include "game/Pathfinder.hpp"

#include <catch2/catch_test_macros.hpp>

#include <string_view>
#include <vector>

using namespace backlands::game;

namespace {

MapGrid grid_from(std::initializer_list<std::string_view> rows) {
    std::vector<CellFlags> cells;
    std::size_t width = 0;
    for (const auto row : rows) {
        if (width == 0) {
            width = row.size();
        }
        REQUIRE(row.size() == width);
        for (const char c : row) {
            if (c == '.') {
                cells.push_back(CellFlags::make(true, false, Terrain::Grass));
            }
            else {
                cells.push_back(CellFlags::make(false, false, Terrain::Water));
            }
        }
    }
    return MapGrid(static_cast<std::uint16_t>(width), static_cast<std::uint16_t>(rows.size()),
                   std::move(cells));
}

// Registro com um jogador na célula pedida.
struct Fixture {
    EntityRegistry registry;
    EntityId id = kNoEntity;

    explicit Fixture(Cell start) {
        const auto result = registry.spawn_player(1, "Teste", start);
        REQUIRE(result.ok());
        id = result.id;
    }

    Entity& entity() {
        Entity* e = registry.find(id);
        REQUIRE(e != nullptr);
        return *e;
    }
};

} // namespace

TEST_CASE("passo diagonal leva mais tempo que ortogonal") {
    // A razão 14/10 é a do eAthena: a diagonal percorre ~1.4 célula.
    CHECK(step_duration_ms(150, Direction::East) == 150);
    CHECK(step_duration_ms(150, Direction::North) == 150);
    CHECK(step_duration_ms(150, Direction::NorthEast) == 210);
    CHECK(step_duration_ms(100, Direction::SouthWest) == 140);
}

TEST_CASE("caminhada não começa com caminho vazio") {
    Fixture fixture(Cell{1, 1});
    CHECK_FALSE(begin_walk(fixture.entity(), WalkPath{}, Cell{1, 1}, 0));
    CHECK_FALSE(fixture.entity().walk.walking());
}

TEST_CASE("entidade só anda quando o passo vence") {
    const auto grid = grid_from({
        ".....",
        ".....",
        ".....",
    });

    Fixture fixture(Cell{0, 1});
    auto path = find_path(grid, Cell{0, 1}, Cell{3, 1});
    REQUIRE(path.size() == 3);
    REQUIRE(begin_walk(fixture.entity(), std::move(path), Cell{3, 1}, 0));

    // Antes de 150 ms não houve passo: a posição é autoritativa, não interpolada.
    auto events = advance_movement(fixture.registry, grid, 100);
    CHECK(events.empty());
    CHECK(fixture.entity().cell == Cell{0, 1});

    // Em 150 ms, exatamente um passo.
    events = advance_movement(fixture.registry, grid, 150);
    CHECK(events.empty());
    CHECK(fixture.entity().cell == Cell{1, 1});
    CHECK(fixture.entity().facing == Direction::East);

    events = advance_movement(fixture.registry, grid, 300);
    CHECK(fixture.entity().cell == Cell{2, 1});

    // O último passo gera o evento de chegada.
    events = advance_movement(fixture.registry, grid, 450);
    REQUIRE(events.size() == 1);
    CHECK(events[0].id == fixture.id);
    CHECK(events[0].outcome == MovementOutcome::Arrived);
    CHECK(events[0].cell == Cell{3, 1});
    CHECK(fixture.entity().cell == Cell{3, 1});
    CHECK_FALSE(fixture.entity().walk.walking());
}

TEST_CASE("um avanço pode cobrir vários passos se o tempo passou") {
    const auto grid = grid_from({
        "..........",
    });

    Fixture fixture(Cell{0, 0});
    auto path = find_path(grid, Cell{0, 0}, Cell{5, 0});
    REQUIRE(path.size() == 5);
    REQUIRE(begin_walk(fixture.entity(), std::move(path), Cell{5, 0}, 0));

    // Um tick atrasado não pode fazer a entidade "perder" passos: ela anda o
    // que o tempo decorrido permite.
    const auto events = advance_movement(fixture.registry, grid, 600);
    CHECK(fixture.entity().cell == Cell{4, 0});
    CHECK(events.empty());
}

TEST_CASE("avanço respeita o teto de passos por vez") {
    // Corredor longo e um salto de tempo enorme: sem teto, um atraso do loop
    // viraria um teleporte.
    const auto grid = grid_from({
        "..................................",
    });

    Fixture fixture(Cell{0, 0});
    auto path = find_path(grid, Cell{0, 0}, Cell{30, 0});
    REQUIRE_FALSE(path.empty());
    REQUIRE(begin_walk(fixture.entity(), std::move(path), Cell{30, 0}, 0));

    advance_movement(fixture.registry, grid, 1'000'000);
    CHECK(fixture.entity().cell.x == kMaxStepsPerAdvance);
    CHECK(fixture.entity().walk.walking());
}

TEST_CASE("obstáculo que aparece no meio do caminho interrompe a caminhada") {
    // O caminho é calculado em um grid e percorrido em outro: é exatamente o
    // que acontece quando o terreno muda depois do cálculo.
    const auto open = grid_from({
        ".....",
    });
    const auto blocked = grid_from({
        "..#..",
    });

    Fixture fixture(Cell{0, 0});
    auto path = find_path(open, Cell{0, 0}, Cell{4, 0});
    REQUIRE(path.size() == 4);
    REQUIRE(begin_walk(fixture.entity(), std::move(path), Cell{4, 0}, 0));

    // Primeiro passo ainda é válido.
    advance_movement(fixture.registry, blocked, 150);
    CHECK(fixture.entity().cell == Cell{1, 0});

    // O segundo entraria na célula bloqueada: a caminhada para no lugar.
    const auto events = advance_movement(fixture.registry, blocked, 300);
    REQUIRE(events.size() == 1);
    CHECK(events[0].outcome == MovementOutcome::Blocked);
    CHECK(events[0].cell == Cell{1, 0});
    CHECK(fixture.entity().cell == Cell{1, 0});
    CHECK_FALSE(fixture.entity().walk.walking());
}

TEST_CASE("entidade parada não gera evento nem se move") {
    const auto grid = grid_from({"....."});
    Fixture fixture(Cell{2, 0});

    const auto events = advance_movement(fixture.registry, grid, 10'000);
    CHECK(events.empty());
    CHECK(fixture.entity().cell == Cell{2, 0});
}

TEST_CASE("stop_walk interrompe sem mover") {
    const auto grid = grid_from({"....."});
    Fixture fixture(Cell{0, 0});

    auto path = find_path(grid, Cell{0, 0}, Cell{4, 0});
    REQUIRE(begin_walk(fixture.entity(), std::move(path), Cell{4, 0}, 0));
    REQUIRE(fixture.entity().walk.walking());

    stop_walk(fixture.entity());
    CHECK_FALSE(fixture.entity().walk.walking());

    const auto events = advance_movement(fixture.registry, grid, 10'000);
    CHECK(events.empty());
    CHECK(fixture.entity().cell == Cell{0, 0});
}

TEST_CASE("várias entidades avançam de forma independente") {
    const auto grid = grid_from({
        "..........",
        "..........",
    });

    EntityRegistry registry;
    const auto a = registry.spawn_player(1, "Rapido", Cell{0, 0});
    const auto b = registry.spawn_player(2, "Lento", Cell{0, 1});
    REQUIRE(a.ok());
    REQUIRE(b.ok());

    registry.find(a.id)->walk.speed_ms_per_cell = 100;
    registry.find(b.id)->walk.speed_ms_per_cell = 300;

    auto path_a = find_path(grid, Cell{0, 0}, Cell{4, 0});
    auto path_b = find_path(grid, Cell{0, 1}, Cell{4, 1});
    REQUIRE(begin_walk(*registry.find(a.id), std::move(path_a), Cell{4, 0}, 0));
    REQUIRE(begin_walk(*registry.find(b.id), std::move(path_b), Cell{4, 1}, 0));

    advance_movement(registry, grid, 300);

    // Mesma janela de tempo, distâncias diferentes conforme a velocidade.
    CHECK(registry.find(a.id)->cell == Cell{3, 0});
    CHECK(registry.find(b.id)->cell == Cell{1, 1});
}

TEST_CASE("caminhada diagonal gasta o tempo maior por passo") {
    const auto grid = grid_from({
        "....",
        "....",
        "....",
        "....",
    });

    Fixture fixture(Cell{0, 0});
    auto path = find_path(grid, Cell{0, 0}, Cell{3, 3});
    REQUIRE(path.size() == 3);
    for (const Direction dir : path.steps) {
        REQUIRE(is_diagonal(dir));
    }
    REQUIRE(begin_walk(fixture.entity(), std::move(path), Cell{3, 3}, 0));

    // 150 ms não bastam para um passo diagonal (custa 210 ms).
    advance_movement(fixture.registry, grid, 150);
    CHECK(fixture.entity().cell == Cell{0, 0});

    advance_movement(fixture.registry, grid, 210);
    CHECK(fixture.entity().cell == Cell{1, 1});
}

TEST_CASE("perseguição sai andando atrás do alvo e para ao entrar no alcance") {
    const auto grid = grid_from({
        "..........",
        "..........",
        "..........",
    });

    Fixture fixture(Cell{0, 1});
    const EntityId prey = fixture.registry.spawn_monster("Presa", Cell{6, 1}).id;
    fixture.entity().combat.target = prey;

    auto events = advance_chasing(fixture.registry, grid, 0);
    REQUIRE(events.size() == 1);
    CHECK(events[0].id == fixture.id);
    CHECK(events[0].type == ChaseEvent::Type::StartedWalk);
    REQUIRE(fixture.entity().walk.walking());

    // Andando e ainda longe: segue a rota em que se comprometeu, sem evento.
    const std::vector<Direction> committed = fixture.entity().walk.path;
    CHECK(advance_chasing(fixture.registry, grid, 50).empty());
    CHECK(fixture.entity().walk.path == committed);

    // Entrou no alcance no meio do caminho: corta a rota e vira para o alvo.
    fixture.entity().cell = Cell{5, 1};
    events = advance_chasing(fixture.registry, grid, 100);
    REQUIRE(events.size() == 1);
    CHECK(events[0].id == fixture.id);
    CHECK(events[0].type == ChaseEvent::Type::ReachedTarget);
    CHECK_FALSE(fixture.entity().walk.walking());
    CHECK(fixture.entity().facing == Direction::East);

    // Parado e no alcance: daqui em diante é o combate que age.
    CHECK(advance_chasing(fixture.registry, grid, 150).empty());
}

TEST_CASE("perseguição sem rota até o alvo não sai andando") {
    const auto grid = grid_from({
        "..~..",
        "..~..",
        "..~..",
    });

    Fixture fixture(Cell{0, 1});
    fixture.entity().combat.target = fixture.registry.spawn_monster("Presa", Cell{4, 1}).id;

    CHECK(advance_chasing(fixture.registry, grid, 0).empty());
    CHECK_FALSE(fixture.entity().walk.walking());
}
