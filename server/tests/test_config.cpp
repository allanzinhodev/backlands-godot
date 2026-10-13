#include "core/Config.hpp"

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_string.hpp>

#include <filesystem>
#include <fstream>
#include <stdexcept>
#include <string>
#include <string_view>

using backlands::core::ServerConfig;
using Catch::Matchers::ContainsSubstring;

namespace {

// Escreve um config.lua temporário e o remove no fim do teste.
class TempConfig {
public:
    explicit TempConfig(std::string_view contents) {
        static int counter = 0;
        path_ = std::filesystem::temp_directory_path() /
                ("backlands_config_test_" + std::to_string(++counter) + ".lua");
        std::ofstream file(path_);
        file << contents;
    }

    ~TempConfig() {
        std::error_code ec;
        std::filesystem::remove(path_, ec);
    }

    TempConfig(const TempConfig&) = delete;
    TempConfig& operator=(const TempConfig&) = delete;

    [[nodiscard]] std::string path() const { return path_.string(); }

private:
    std::filesystem::path path_;
};

} // namespace

TEST_CASE("configuração válida é carregada") {
    const TempConfig config(R"(
        return {
          network = { port = 9999 },
          simulation = { ticks_per_second = 30 },
          world = { map = "maps/starter_fields.lua" },
        }
    )");

    const auto loaded = ServerConfig::load_from_file(config.path());
    CHECK(loaded.port == 9999);
    CHECK(loaded.ticks_per_second == 30);
}

TEST_CASE("arquivo inexistente falha com mensagem clara") {
    CHECK_THROWS_WITH(ServerConfig::load_from_file("nao_existe_em_lugar_algum.lua"),
                      ContainsSubstring("não foi possível carregar"));
}

TEST_CASE("Lua com erro de sintaxe não passa como configuração vazia") {
    const TempConfig config("return { network = ");
    CHECK_THROWS_AS(ServerConfig::load_from_file(config.path()), std::runtime_error);
}

TEST_CASE("erro em tempo de execução no Lua é reportado") {
    const TempConfig config("error('explodiu de propósito')");
    CHECK_THROWS_WITH(ServerConfig::load_from_file(config.path()),
                      ContainsSubstring("erro ao executar"));
}

TEST_CASE("arquivo que não retorna tabela é rejeitado") {
    const TempConfig config("return 42");
    CHECK_THROWS_WITH(ServerConfig::load_from_file(config.path()),
                      ContainsSubstring("precisa retornar uma tabela"));
}

TEST_CASE("seção ausente é rejeitada em vez de usar default silencioso") {
    SECTION("sem network") {
        const TempConfig config(
            "return { simulation = { ticks_per_second = 20 }, world = { map = 'm.lua' } }");
        CHECK_THROWS_WITH(ServerConfig::load_from_file(config.path()),
                          ContainsSubstring("seção obrigatória ausente: network"));
    }

    SECTION("sem simulation") {
        const TempConfig config(
            "return { network = { port = 7777 }, world = { map = 'm.lua' } }");
        CHECK_THROWS_WITH(ServerConfig::load_from_file(config.path()),
                          ContainsSubstring("seção obrigatória ausente: simulation"));
    }
}

TEST_CASE("mapa ausente na config é rejeitado") {
    // Sem mapa o servidor não tem grid, e sem grid não há validação de
    // movimento — falhar no boot é melhor que subir sem autoridade.
    const TempConfig config(R"(
        return {
          network = { port = 7777 },
          simulation = { ticks_per_second = 20 },
        }
    )");
    CHECK_THROWS_WITH(ServerConfig::load_from_file(config.path()),
                      ContainsSubstring("world"));
}

TEST_CASE("campo ausente é rejeitado") {
    const TempConfig config(R"(
        return {
          network = {},
          simulation = { ticks_per_second = 20 },
          world = { map = "m.lua" },
        }
    )");
    CHECK_THROWS_WITH(ServerConfig::load_from_file(config.path()),
                      ContainsSubstring("network.port"));
}

TEST_CASE("valores fora da faixa são rejeitados") {
    SECTION("porta zero") {
        const TempConfig config(R"(
            return {
              network = { port = 0 },
              simulation = { ticks_per_second = 20 },
              world = { map = "m.lua" },
            }
        )");
        CHECK_THROWS_WITH(ServerConfig::load_from_file(config.path()),
                          ContainsSubstring("fora da faixa"));
    }

    SECTION("porta acima de 65535") {
        const TempConfig config(R"(
            return {
              network = { port = 70000 },
              simulation = { ticks_per_second = 20 },
              world = { map = "m.lua" },
            }
        )");
        CHECK_THROWS_WITH(ServerConfig::load_from_file(config.path()),
                          ContainsSubstring("fora da faixa"));
    }

    SECTION("tick rate zero causaria divisão por zero no loop") {
        const TempConfig config(R"(
            return {
              network = { port = 7777 },
              simulation = { ticks_per_second = 0 },
              world = { map = "m.lua" },
            }
        )");
        CHECK_THROWS_WITH(ServerConfig::load_from_file(config.path()),
                          ContainsSubstring("fora da faixa"));
    }
}

TEST_CASE("config.lua do repositório é válido") {
    // Guarda contra editar o config.lua de produção e quebrá-lo sem perceber.
    // O caminho é relativo ao diretório de build gerado pelo CMake.
    const std::filesystem::path repo_config = BACKLANDS_REPO_CONFIG;
    REQUIRE(std::filesystem::exists(repo_config));

    const auto loaded = ServerConfig::load_from_file(repo_config.string());
    CHECK(loaded.port > 0);
    CHECK(loaded.ticks_per_second > 0);
}
