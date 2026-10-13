#include "game/Entity.hpp"
#include "game/EntityRegistry.hpp"

#include <catch2/catch_test_macros.hpp>

#include <string>
#include <string_view>

using namespace backlands::game;
using SpawnError = EntityRegistry::SpawnError;

TEST_CASE("nome de personagem segue as regras do servidor") {
    SECTION("aceita nomes comuns") {
        CHECK(is_valid_character_name("Allan"));
        CHECK(is_valid_character_name("abc"));
        CHECK(is_valid_character_name("Guerreiro_01"));
        CHECK(is_valid_character_name("ABCDEFGHIJKLMNOP")); // exatos 16
    }

    SECTION("rejeita tamanho fora da faixa") {
        CHECK_FALSE(is_valid_character_name(""));
        CHECK_FALSE(is_valid_character_name("ab"));
        CHECK_FALSE(is_valid_character_name("ABCDEFGHIJKLMNOPQ")); // 17
        CHECK_FALSE(is_valid_character_name(std::string(500, 'x')));
    }

    SECTION("rejeita caracteres que criariam problema em log e chat") {
        CHECK_FALSE(is_valid_character_name("com espaco"));
        CHECK_FALSE(is_valid_character_name("quebra\nlinha"));
        CHECK_FALSE(is_valid_character_name("acentuação"));
        CHECK_FALSE(is_valid_character_name("<script>"));
        CHECK_FALSE(is_valid_character_name("tab\there"));

        // O tamanho explícito importa: um literal com \0 no meio seria cortado
        // por strlen, mas read_string monta a string pelo tamanho que veio no
        // fio, então o NUL chega inteiro e precisa ser recusado.
        CHECK_FALSE(is_valid_character_name(std::string_view("nulo\0dentro", 11)));
    }
}

TEST_CASE("spawn de jogador cria entidade na célula pedida") {
    EntityRegistry registry;

    const auto result = registry.spawn_player(1, "Allan", Cell{10, 20});
    REQUIRE(result.ok());
    REQUIRE(result.id != kNoEntity);
    CHECK(registry.size() == 1);

    const Entity* entity = registry.find(result.id);
    REQUIRE(entity != nullptr);
    CHECK(entity->name == "Allan");
    CHECK(entity->cell == Cell{10, 20});
    CHECK(entity->facing == Direction::South);
    CHECK(entity->session_id == 1);
    CHECK(entity->is_player());
}

TEST_CASE("ids de entidade não se repetem depois de um despawn") {
    EntityRegistry registry;

    const auto first = registry.spawn_player(1, "Primeiro", Cell{1, 1});
    REQUIRE(first.ok());
    REQUIRE(registry.despawn_session(1) == first.id);

    const auto second = registry.spawn_player(2, "Segundo", Cell{2, 2});
    REQUIRE(second.ok());

    // Reusar ids faria um pacote atrasado da entidade morta acertar a nova.
    CHECK(second.id != first.id);
}

TEST_CASE("nome inválido é recusado com motivo próprio") {
    EntityRegistry registry;

    const auto result = registry.spawn_player(1, "ab", Cell{0, 0});
    CHECK_FALSE(result.ok());
    CHECK(result.error == SpawnError::InvalidName);
    CHECK(result.id == kNoEntity);
    CHECK(registry.size() == 0);
}

TEST_CASE("nome em uso é recusado") {
    EntityRegistry registry;
    REQUIRE(registry.spawn_player(1, "Allan", Cell{0, 0}).ok());

    const auto duplicate = registry.spawn_player(2, "Allan", Cell{5, 5});
    CHECK_FALSE(duplicate.ok());
    CHECK(duplicate.error == SpawnError::NameInUse);
    CHECK(registry.size() == 1);
}

TEST_CASE("nome liberado depois do despawn pode ser reusado") {
    EntityRegistry registry;
    REQUIRE(registry.spawn_player(1, "Allan", Cell{0, 0}).ok());
    REQUIRE(registry.despawn_session(1) != kNoEntity);

    CHECK_FALSE(registry.is_name_in_use("Allan"));
    CHECK(registry.spawn_player(2, "Allan", Cell{0, 0}).ok());
}

TEST_CASE("a mesma sessão não entra duas vezes") {
    EntityRegistry registry;
    REQUIRE(registry.spawn_player(1, "Allan", Cell{0, 0}).ok());

    // Dois logins na mesma conexão é erro de protocolo: nenhum client correto
    // faz isso, e aceitar criaria entidade órfã (a primeira ficaria sem dono).
    const auto again = registry.spawn_player(1, "Outro", Cell{0, 0});
    CHECK_FALSE(again.ok());
    CHECK(again.error == SpawnError::AlreadySpawned);
    CHECK(registry.size() == 1);
}

TEST_CASE("sessão 0 nunca cria entidade") {
    EntityRegistry registry;

    // 0 é a marca de "sem sessão" (monstros e NPCs), não uma conexão real.
    const auto result = registry.spawn_player(0, "Allan", Cell{0, 0});
    CHECK_FALSE(result.ok());
    CHECK(registry.size() == 0);
}

TEST_CASE("despawn de sessão sem entidade é inofensivo") {
    EntityRegistry registry;

    // Acontece de verdade: cliente conecta e cai antes de mandar o login.
    CHECK(registry.despawn_session(42) == kNoEntity);
    CHECK(registry.size() == 0);
}

TEST_CASE("busca por sessão encontra a entidade do jogador") {
    EntityRegistry registry;
    const auto result = registry.spawn_player(7, "Allan", Cell{3, 4});
    REQUIRE(result.ok());

    const Entity* found = registry.find_by_session(7);
    REQUIRE(found != nullptr);
    CHECK(found->id == result.id);

    CHECK(registry.find_by_session(8) == nullptr);
    CHECK(registry.find(9999) == nullptr);
}

TEST_CASE("listagem devolve todas as entidades para enviar a quem entra") {
    EntityRegistry registry;
    REQUIRE(registry.spawn_player(1, "Um1", Cell{1, 1}).ok());
    REQUIRE(registry.spawn_player(2, "Dois", Cell{2, 2}).ok());
    REQUIRE(registry.spawn_player(3, "Tres", Cell{3, 3}).ok());

    const auto all = registry.all();
    CHECK(all.size() == 3);

    REQUIRE(registry.despawn_session(2) != kNoEntity);
    CHECK(registry.all().size() == 2);
}

TEST_CASE("direção no fio é validada") {
    CHECK(is_valid_direction(0));
    CHECK(is_valid_direction(7));
    CHECK_FALSE(is_valid_direction(8));
    CHECK_FALSE(is_valid_direction(255));
}
