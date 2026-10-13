#include "game/Combat.hpp"
#include "game/Entity.hpp"
#include "game/EntityRegistry.hpp"
#include "game/MonsterAi.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstdint>

using namespace backlands::game;

namespace {

// Dois combatentes adjacentes, prontos para trocar golpes.
struct Arena {
    EntityRegistry registry;
    Rng rng{999};
    EntityId attacker = kNoEntity;
    EntityId target = kNoEntity;

    explicit Arena(Cell attacker_cell = Cell{5, 5}, Cell target_cell = Cell{6, 5}) {
        const auto a = registry.spawn_player(1, "Atacante", attacker_cell);
        const auto b = registry.spawn_player(2, "Alvo", target_cell);
        REQUIRE(a.ok());
        REQUIRE(b.ok());
        attacker = a.id;
        target = b.id;
    }

    Entity& at(EntityId id) {
        Entity* e = registry.find(id);
        REQUIRE(e != nullptr);
        return *e;
    }

    // Remove a aleatoriedade para os testes que verificam sequência e ritmo:
    // dano fixo e acerto garantido.
    void make_deterministic(std::int32_t damage) {
        auto& a = at(attacker);
        a.stats.attack_min = damage;
        a.stats.attack_max = damage;
        a.stats.hit = 10000; // a taxa satura no teto

        auto& b = at(target);
        b.stats.flee = 0;
        b.stats.defense_percent = 0;
        b.stats.defense_flat = 0;
    }
};

} // namespace

TEST_CASE("distância em células é Chebyshev") {
    // Num grid com diagonais, um passo diagonal cobre uma célula em cada eixo.
    // Medir por Euclides faria o alcance diagonal parecer maior que o movimento.
    CHECK(cell_distance(Cell{5, 5}, Cell{5, 5}) == 0);
    CHECK(cell_distance(Cell{5, 5}, Cell{6, 5}) == 1);
    CHECK(cell_distance(Cell{5, 5}, Cell{6, 6}) == 1);
    CHECK(cell_distance(Cell{5, 5}, Cell{8, 6}) == 3);
    CHECK(cell_distance(Cell{0, 0}, Cell{3, 4}) == 4);
}

TEST_CASE("alcance de ataque usa a distância em células") {
    Arena arena;
    auto& attacker = arena.at(arena.attacker);
    auto& target = arena.at(arena.target);

    attacker.stats.attack_range = 1;
    CHECK(in_attack_range(attacker, target));

    SECTION("diagonal adjacente também está no alcance") {
        target.cell = Cell{6, 6};
        CHECK(in_attack_range(attacker, target));
    }

    SECTION("duas células de distância está fora para alcance 1") {
        target.cell = Cell{7, 5};
        CHECK_FALSE(in_attack_range(attacker, target));
    }

    SECTION("alcance maior cobre mais longe") {
        target.cell = Cell{9, 5};
        attacker.stats.attack_range = 4;
        CHECK(in_attack_range(attacker, target));
        attacker.stats.attack_range = 3;
        CHECK_FALSE(in_attack_range(attacker, target));
    }
}

TEST_CASE("dano aplica defesa percentual antes da subtrativa") {
    Rng rng{1};
    Stats attacker;
    Stats defender;

    attacker.attack_min = 100;
    attacker.attack_max = 100;

    SECTION("sem defesa, o dano é o golpe cheio") {
        CHECK(compute_damage(attacker, defender, rng) == 100);
    }

    SECTION("defesa percentual corta proporcionalmente") {
        defender.defense_percent = 25;
        CHECK(compute_damage(attacker, defender, rng) == 75);
    }

    SECTION("defesa plana subtrai depois da percentual") {
        // A ORDEM importa: 100 * 0,5 = 50, menos 10 dá 40. Subtrair antes daria
        // (100 - 10) * 0,5 = 45, e a defesa percentual quase não importaria em
        // alvos com defesa plana alta.
        defender.defense_percent = 50;
        defender.defense_flat = 10;
        CHECK(compute_damage(attacker, defender, rng) == 40);
    }

    SECTION("defesa altíssima ainda sofre o mínimo de 1") {
        // Sem o mínimo, um alvo muito defensivo ficaria imortal e o combate
        // travaria sem dar nenhum sinal ao jogador.
        defender.defense_percent = 99;
        defender.defense_flat = 1000;
        CHECK(compute_damage(attacker, defender, rng) == 1);
    }

    SECTION("defesa percentual é limitada a 100") {
        defender.defense_percent = 500;
        CHECK(compute_damage(attacker, defender, rng) == 1);
    }
}

TEST_CASE("o dano varia dentro da faixa do golpe") {
    Rng rng{2024};
    Stats attacker;
    const Stats defender;
    attacker.attack_min = 10;
    attacker.attack_max = 20;

    const std::int32_t first = compute_damage(attacker, defender, rng);
    bool saw_different = false;

    for (int i = 0; i < 200; ++i) {
        const auto damage = compute_damage(attacker, defender, rng);
        CHECK(damage >= 10);
        CHECK(damage <= 20);
        if (damage != first) {
            saw_different = true;
        }
    }

    // Dano fixo deixaria o combate monótono; a faixa precisa variar de fato.
    CHECK(saw_different);
}

TEST_CASE("taxa de acerto é 80 + hit - flee, com piso e teto") {
    Stats attacker;
    Stats defender;

    attacker.hit = 80;
    defender.flee = 80;
    CHECK(hit_rate(attacker, defender) == 80);

    attacker.hit = 100;
    defender.flee = 60;
    CHECK(hit_rate(attacker, defender) == 100);

    SECTION("nem o melhor atacante passa do teto") {
        attacker.hit = 10000;
        defender.flee = 0;
        CHECK(hit_rate(attacker, defender) == kMaxHitRate);
    }

    SECTION("nem o pior atacante fica abaixo do piso") {
        // Sem o piso, uma esquiva alta tornaria o alvo intocável e o jogador
        // ficaria batendo para sempre sem entender por quê.
        attacker.hit = 0;
        defender.flee = 10000;
        CHECK(hit_rate(attacker, defender) == kMinHitRate);
    }
}

TEST_CASE("errar o golpe não causa dano") {
    Rng rng{7};
    Stats attacker;
    Stats defender;
    attacker.hit = 0;
    defender.flee = 10000; // taxa no piso de 5%

    int misses = 0;
    for (int i = 0; i < 200; ++i) {
        const auto result = roll_attack(attacker, defender, rng);
        if (result.missed) {
            ++misses;
            CHECK(result.damage == 0);
        }
        else {
            CHECK(result.damage > 0);
        }
    }

    CHECK(misses > 100); // com 5% de acerto, a maioria tem de errar
}

TEST_CASE("validação de ataque recusa cada caso por um motivo próprio") {
    Arena arena;
    auto& attacker = arena.at(arena.attacker);
    auto& target = arena.at(arena.target);

    CHECK(validate_attack(attacker, &target, 0) == AttackRejection::None);

    SECTION("alvo inexistente") {
        CHECK(validate_attack(attacker, nullptr, 0) == AttackRejection::NoSuchTarget);
    }

    SECTION("atacar a si mesmo") {
        CHECK(validate_attack(attacker, &attacker, 0) == AttackRejection::SelfTarget);
    }

    SECTION("alvo morto") {
        target.stats.hp = 0;
        CHECK(validate_attack(attacker, &target, 0) == AttackRejection::TargetDead);
    }

    SECTION("atacante morto") {
        attacker.stats.hp = 0;
        CHECK(validate_attack(attacker, &target, 0) == AttackRejection::AttackerDead);
    }

    SECTION("fora de alcance") {
        target.cell = Cell{20, 20};
        CHECK(validate_attack(attacker, &target, 0) == AttackRejection::OutOfRange);
    }

    SECTION("ainda em cooldown") {
        attacker.combat.next_attack_at_ms = 500;
        CHECK(validate_attack(attacker, &target, 499) == AttackRejection::OnCooldown);
        CHECK(validate_attack(attacker, &target, 500) == AttackRejection::None);
    }
}

TEST_CASE("dano aplicado reduz HP e mata em zero") {
    Arena arena;
    auto& target = arena.at(arena.target);
    target.stats.max_hp = 100;
    target.stats.hp = 100;

    auto result = apply_damage(target, 30);
    CHECK(result.damage == 30);
    CHECK_FALSE(result.killed);
    CHECK(target.stats.hp == 70);

    result = apply_damage(target, 70);
    CHECK(result.killed);
    CHECK(target.stats.hp == 0);

    SECTION("HP nunca fica negativo") {
        auto& other = arena.at(arena.attacker);
        other.stats.hp = 10;
        apply_damage(other, 9999);
        CHECK(other.stats.hp == 0);
    }
}

TEST_CASE("morte limpa caminhada e alvo") {
    Arena arena;
    auto& target = arena.at(arena.target);
    target.walk.path = {Direction::East, Direction::East};
    target.combat.target = arena.attacker;
    target.stats.hp = 5;

    apply_damage(target, 5);

    // Sem isto o cadáver continuaria andando até o destino antigo.
    CHECK_FALSE(target.walk.walking());
    CHECK(target.combat.target == kNoEntity);
}

TEST_CASE("ataque contínuo repete no ritmo do cooldown") {
    Arena arena;
    arena.make_deterministic(10);

    auto& attacker = arena.at(arena.attacker);
    auto& target = arena.at(arena.target);
    attacker.stats.attack_delay_ms = 1000;
    target.stats.max_hp = 100;
    target.stats.hp = 100;
    attacker.combat.target = arena.target;

    // Primeiro golpe sai imediatamente.
    auto events = advance_combat(arena.registry, 0, arena.rng);
    REQUIRE(events.size() == 1);
    CHECK(events[0].type == CombatEvent::Type::Hit);
    CHECK(events[0].damage == 10);
    CHECK(arena.at(arena.target).stats.hp == 90);

    // Antes do cooldown vencer, nada acontece — é isto que impede um client
    // modificado de atacar a cada pacote.
    CHECK(advance_combat(arena.registry, 999, arena.rng).empty());
    CHECK(arena.at(arena.target).stats.hp == 90);

    events = advance_combat(arena.registry, 1000, arena.rng);
    REQUIRE(events.size() == 1);
    CHECK(arena.at(arena.target).stats.hp == 80);
}

TEST_CASE("ataque contínuo para quando o alvo sai do alcance") {
    Arena arena;
    arena.make_deterministic(5);

    auto& attacker = arena.at(arena.attacker);
    attacker.combat.target = arena.target;
    attacker.stats.attack_delay_ms = 0;

    REQUIRE_FALSE(advance_combat(arena.registry, 0, arena.rng).empty());

    // O alvo foge: o golpe não sai, mas o alvo CONTINUA selecionado, porque no
    // modelo do Ragnarok o atacante persegue.
    arena.at(arena.target).cell = Cell{20, 20};
    CHECK(advance_combat(arena.registry, 100, arena.rng).empty());
    CHECK(arena.at(arena.attacker).combat.target == arena.target);
}

TEST_CASE("matar gera evento de morte e encerra o ataque") {
    Arena arena;
    arena.make_deterministic(1000);

    auto& attacker = arena.at(arena.attacker);
    auto& target = arena.at(arena.target);
    attacker.stats.attack_delay_ms = 0;
    attacker.combat.target = arena.target;
    target.stats.hp = 10;

    const auto events = advance_combat(arena.registry, 0, arena.rng);
    REQUIRE(events.size() == 2);
    CHECK(events[0].type == CombatEvent::Type::Hit);
    CHECK(events[1].type == CombatEvent::Type::Died);
    CHECK(events[1].target == arena.target);

    CHECK(arena.at(arena.target).stats.hp == 0);
    // Ninguém fica golpeando cadáver.
    CHECK(arena.at(arena.attacker).combat.target == kNoEntity);
}

TEST_CASE("alvo que some do mundo encerra o ataque") {
    Arena arena;
    arena.at(arena.attacker).combat.target = arena.target;

    // Jogador alvo desconecta no meio da luta.
    REQUIRE(arena.registry.despawn_session(2) != kNoEntity);

    CHECK(advance_combat(arena.registry, 0, arena.rng).empty());
    CHECK(arena.at(arena.attacker).combat.target == kNoEntity);
}

TEST_CASE("atacante morto não bate") {
    Arena arena;
    arena.at(arena.attacker).combat.target = arena.target;
    arena.at(arena.attacker).stats.hp = 0;

    CHECK(advance_combat(arena.registry, 0, arena.rng).empty());
}

TEST_CASE("golpear interrompe a caminhada e vira para o alvo") {
    Arena arena;
    arena.make_deterministic(5);

    auto& attacker = arena.at(arena.attacker);
    attacker.combat.target = arena.target;
    attacker.walk.path = {Direction::West, Direction::West};
    attacker.facing = Direction::West;

    REQUIRE_FALSE(advance_combat(arena.registry, 0, arena.rng).empty());

    // Atacar enquanto desliza para o próximo tile deixaria a posição do golpe
    // ambígua entre as duas células.
    CHECK_FALSE(arena.at(arena.attacker).walk.walking());
    CHECK(arena.at(arena.attacker).facing == Direction::East);
}

TEST_CASE("o monstro não repensa durante a animação do golpe") {
    EntityRegistry registry;
    Rng rng{5};

    Entity& monster = registry.spawn_monster("Bicho", Cell{5, 5});
    monster.stats.attack_min = 5;
    monster.stats.attack_max = 5;
    monster.stats.hit = 10000;
    monster.stats.attack_motion_ms = 700;
    monster.stats.attack_delay_ms = 2000;

    const auto player = registry.spawn_player(1, "Vitima", Cell{6, 5});
    REQUIRE(player.ok());
    registry.find(player.id)->stats.flee = 0;
    monster.combat.target = player.id;

    REQUIRE_FALSE(advance_combat(registry, 1000, rng).empty());

    // Sem esta trava o monstro tremeria e recalcularia caminho no meio do golpe.
    CHECK(registry.find(monster.id)->brain.next_think_at_ms >= 1700);
}
