#include "game/Combat.hpp"
#include "game/EntityRegistry.hpp"
#include "game/MapGrid.hpp"
#include "game/MonsterAi.hpp"
#include "game/Respawn.hpp"

#include <catch2/catch_test_macros.hpp>

#include <string>
#include <string_view>
#include <vector>

using namespace backlands::game;

namespace {

MapGrid open_grid(std::uint16_t size = 40) {
    std::vector<CellFlags> cells(static_cast<std::size_t>(size) * size,
                                 CellFlags::make(true, false, Terrain::Grass));
    return MapGrid(size, size, std::move(cells));
}

struct Den {
    EntityRegistry registry;
    MapGrid grid = open_grid();
    Rng rng{12345};
    EntityId monster = kNoEntity;

    Entity& spawn(Cell home, std::uint16_t view_range, std::uint16_t chase_range = 12) {
        Entity& m = registry.spawn_monster("Bicho", home);
        m.brain.home = home;
        m.brain.view_range = view_range;
        m.brain.chase_range = chase_range;
        monster = m.id;
        return m;
    }

    EntityId add_player(std::uint32_t session, Cell cell) {
        // Nome derivado da sessão: o registro recusa nome repetido, e um nome
        // fixo faria o segundo jogador falhar ao entrar.
        const auto result =
            registry.spawn_player(session, "Jogador" + std::to_string(session), cell);
        REQUIRE(result.ok());
        return result.id;
    }

    Entity& at(EntityId id) {
        Entity* e = registry.find(id);
        REQUIRE(e != nullptr);
        return *e;
    }

    // Avança o tempo o bastante para o monstro pensar de novo.
    void think(std::uint32_t& now) {
        now += kMonsterThinkIntervalMs;
        advance_monster_ai(registry, grid, now, rng);
    }
};

} // namespace

TEST_CASE("o gerador aleatório é reproduzível e respeita a faixa") {
    // Semente explícita em vez de rand(): sem isso os testes de IA seriam
    // instáveis e um comportamento errado passaria despercebido.
    Rng a{42};
    Rng b{42};
    for (int i = 0; i < 100; ++i) {
        CHECK(a.next() == b.next());
    }

    Rng rng{7};
    for (int i = 0; i < 500; ++i) {
        const int value = rng.range(3, 9);
        CHECK(value >= 3);
        CHECK(value <= 9);
    }
    CHECK(rng.range(5, 5) == 5);
    CHECK(rng.range(9, 2) == 9); // faixa inválida devolve o início
}

TEST_CASE("monstro agressivo adquire o jogador dentro do campo de visão") {
    Den den;
    den.spawn(Cell{20, 20}, /*view_range=*/7);
    const auto player = den.add_player(1, Cell{24, 20}); // 4 células

    std::uint32_t now = 0;
    den.think(now);

    CHECK(den.at(den.monster).combat.target == player);
}

TEST_CASE("monstro agressivo ignora jogador fora do campo de visão") {
    Den den;
    den.spawn(Cell{20, 20}, /*view_range=*/5);
    den.add_player(1, Cell{30, 20}); // 10 células

    std::uint32_t now = 0;
    den.think(now);

    CHECK(den.at(den.monster).combat.target == kNoEntity);
}

TEST_CASE("monstro passivo não inicia combate") {
    Den den;
    den.spawn(Cell{20, 20}, /*view_range=*/0);
    den.add_player(1, Cell{21, 20}); // colado

    std::uint32_t now = 0;
    for (int i = 0; i < 10; ++i) {
        den.think(now);
    }

    // view_range 0 é a diferença entre "não me procure" e "não reaja a nada".
    CHECK(den.at(den.monster).combat.target == kNoEntity);
}

TEST_CASE("monstro passivo revida em quem o atacou") {
    Den den;
    den.spawn(Cell{20, 20}, /*view_range=*/0);
    const auto player = den.add_player(1, Cell{21, 20});

    den.at(den.monster).brain.retaliate_against = player;

    std::uint32_t now = 0;
    den.think(now);

    CHECK(den.at(den.monster).combat.target == player);
}

TEST_CASE("monstro desiste quando o alvo o arrasta longe de casa") {
    Den den;
    den.spawn(Cell{20, 20}, /*view_range=*/7, /*chase_range=*/6);
    const auto player = den.add_player(1, Cell{22, 20});

    std::uint32_t now = 0;
    den.think(now);
    REQUIRE(den.at(den.monster).combat.target == player);

    // O jogador foge para fora do raio de perseguição. Sem este limite, um
    // jogador puxaria o mapa inteiro atrás de si.
    den.at(player).cell = Cell{35, 20};
    den.think(now);

    CHECK(den.at(den.monster).combat.target == kNoEntity);
}

TEST_CASE("ao desistir, o monstro fica ocioso onde está, sem voltar para casa") {
    Den den;
    den.spawn(Cell{20, 20}, /*view_range=*/7, /*chase_range=*/6);
    const auto player = den.add_player(1, Cell{22, 20});

    std::uint32_t now = 0;
    den.think(now);
    REQUIRE(den.at(den.monster).combat.target == player);

    // Monstro longe de casa e alvo fugindo para fora do raio.
    den.at(den.monster).cell = Cell{26, 20};
    den.at(player).cell = Cell{35, 20};

    now += kMonsterThinkIntervalMs;
    const auto wanders = advance_monster_ai(den.registry, den.grid, now, den.rng);

    CHECK(den.at(den.monster).combat.target == kNoEntity);

    // Nada de coleira de volta ao ponto de spawn: ela exigiria um estado
    // "voltando, ignore agressão", senão o monstro ping-pongaria entre a casa
    // e o jogador. Ele simplesmente para onde está.
    CHECK(wanders.empty());
    CHECK(den.at(den.monster).cell == Cell{26, 20});
}

TEST_CASE("alvo que morre é largado") {
    Den den;
    den.spawn(Cell{20, 20}, /*view_range=*/7);
    const auto player = den.add_player(1, Cell{22, 20});

    std::uint32_t now = 0;
    den.think(now);
    REQUIRE(den.at(den.monster).combat.target == player);

    den.at(player).stats.hp = 0;
    den.think(now);

    CHECK(den.at(den.monster).combat.target == kNoEntity);
}

TEST_CASE("o rancor não sobrevive à morte do monstro") {
    Den den;
    auto& monster = den.spawn(Cell{20, 20}, /*view_range=*/0);
    const auto player = den.add_player(1, Cell{21, 20});

    monster.brain.retaliate_against = player;
    std::uint32_t now = 0;
    den.think(now);
    REQUIRE(den.at(den.monster).combat.target == player);

    // Morre e "renasce" como o World faz: vida cheia, alvo e rancor zerados.
    // Sem zerar o rancor, um passivo volta atacando quem o matou antes, sem
    // ter sido provocado de novo. O renascer aqui é o próprio
    // advance_respawns que o World chama no tick, e não uma cópia dele.
    auto& m = den.at(den.monster);
    m.stats.hp = 0;
    schedule_respawn(m, now);
    now = m.combat.respawn_at_ms;
    REQUIRE(advance_respawns(den.registry, Cell{1, 1}, now).size() == 1);

    den.think(now);
    CHECK(den.at(den.monster).combat.target == kNoEntity);
}

TEST_CASE("monstro morto não pensa") {
    Den den;
    auto& monster = den.spawn(Cell{20, 20}, /*view_range=*/9);
    monster.stats.hp = 0;
    den.add_player(1, Cell{21, 20});

    std::uint32_t now = 0;
    den.think(now);

    CHECK(den.at(den.monster).combat.target == kNoEntity);
}

TEST_CASE("o monstro só pensa no seu intervalo") {
    Den den;
    den.spawn(Cell{20, 20}, /*view_range=*/9);
    const auto player = den.add_player(1, Cell{22, 20});

    // Logo após pensar, não pensa de novo — pensar a cada tick seria
    // desperdício e deixaria a perseguição nervosa.
    std::uint32_t now = 0;
    advance_monster_ai(den.registry, den.grid, now, den.rng);
    REQUIRE(den.at(den.monster).combat.target == player);

    den.at(den.monster).combat.clear_target();
    advance_monster_ai(den.registry, den.grid, now + 1, den.rng);
    CHECK(den.at(den.monster).combat.target == kNoEntity);

    advance_monster_ai(den.registry, den.grid, now + kMonsterThinkIntervalMs, den.rng);
    CHECK(den.at(den.monster).combat.target == player);
}

TEST_CASE("o passeio ocioso é curto, em terreno caminhável e espaçado no tempo") {
    Den den;
    den.spawn(Cell{20, 20}, /*view_range=*/0);
    // Precisa de alguém por perto: sem jogador no raio de atividade, o monstro
    // nem pensa — é o portão que mantém o custo da IA proporcional ao que
    // está em jogo.
    den.add_player(1, Cell{28, 20});

    std::uint32_t now = 0;
    int wander_count = 0;
    std::uint32_t last_wander_at = 0;

    for (int i = 0; i < 600; ++i) {
        now += kMonsterThinkIntervalMs;
        for (const auto& intent : advance_monster_ai(den.registry, den.grid, now, den.rng)) {
            const Cell from = den.at(intent.id).cell;
            CHECK(den.grid.is_walkable(intent.destination));
            CHECK(cell_distance(from, intent.destination) <= kIdleWanderRadius);

            if (wander_count > 0) {
                // Espaçado por intervalo, e não sorteado a cada pensamento:
                // com pensamento a cada 100 ms, uma chance por pensamento
                // faria o monstro andar sem parar.
                CHECK(now - last_wander_at >= kIdleWanderIntervalMs);
            }
            last_wander_at = now;
            ++wander_count;

            // O monstro "chega" para poder passear de novo.
            den.at(intent.id).cell = intent.destination;
        }
    }

    CHECK(wander_count > 0);
}

TEST_CASE("monstro em movimento não recebe novo passeio") {
    Den den;
    auto& monster = den.spawn(Cell{20, 20}, /*view_range=*/0);
    monster.walk.path = {Direction::East, Direction::East};

    std::uint32_t now = 0;
    for (int i = 0; i < 50; ++i) {
        now += kMonsterThinkIntervalMs;
        CHECK(advance_monster_ai(den.registry, den.grid, now, den.rng).empty());
    }
}

TEST_CASE("o passeio decidido vira caminhada, menos para quem já anda ou não tem rota") {
    Den den;
    const EntityId idle = den.spawn(Cell{20, 20}, /*view_range=*/0).id;
    const EntityId stranded = den.registry.spawn_monster("Ilhado", Cell{30, 30}).id;
    Entity& busy = den.registry.spawn_monster("Ocupado", Cell{10, 10});
    busy.walk.path = {Direction::South, Direction::South};
    const std::vector<Direction> busy_path = busy.walk.path;

    const std::vector<WanderIntent> intents = {
        {idle, Cell{23, 20}},
        {stranded, Cell{500, 500}}, // fora do mapa: sem rota, o passeio é descartado
        {busy.id, Cell{13, 10}},
        {9999, Cell{5, 5}},         // sumiu do mundo entre decidir e executar
    };
    const std::vector<EntityId> started =
        start_wander_walks(den.registry, den.grid, intents, 0);

    REQUIRE(started.size() == 1);
    CHECK(started[0] == idle);
    CHECK(den.at(idle).walk.walking());
    CHECK(den.at(idle).walk.destination == Cell{23, 20});
    CHECK_FALSE(den.at(stranded).walk.walking());

    // Quem já anda é deixado em paz: a rota em curso não é trocada.
    CHECK(den.at(busy.id).walk.path == busy_path);
}

TEST_CASE("busca do jogador mais próximo ignora mortos e monstros") {
    Den den;
    den.spawn(Cell{20, 20}, /*view_range=*/0);

    const auto far = den.add_player(1, Cell{26, 20});
    const auto near_dead = den.add_player(2, Cell{21, 20});
    den.at(near_dead).stats.hp = 0;

    // Outro monstro colado não pode ser confundido com presa.
    den.registry.spawn_monster("Outro", Cell{20, 21});

    const Entity* found = find_nearest_player(den.registry, den.grid, Cell{20, 20}, 10);
    REQUIRE(found != nullptr);
    CHECK(found->id == far);

    CHECK(find_nearest_player(den.registry, den.grid, Cell{20, 20}, 3) == nullptr);
}
