#include "core/Config.hpp"

#include <sol/sol.hpp>

#include <format>
#include <stdexcept>

namespace backlands::core {
namespace {

// Lê um inteiro de uma subtabela, exigindo que caiba na faixa esperada.
template <typename T>
T require_integer(const sol::table& section, const char* section_name, const char* key,
                  std::int64_t min_value, std::int64_t max_value) {
    const sol::optional<std::int64_t> value = section[key];
    if (!value.has_value()) {
        throw std::runtime_error(
            std::format("config: campo obrigatório ausente: {}.{}", section_name, key));
    }
    if (*value < min_value || *value > max_value) {
        throw std::runtime_error(std::format("config: {}.{} = {} fora da faixa [{}, {}]",
                                             section_name, key, *value, min_value, max_value));
    }
    return static_cast<T>(*value);
}

sol::table require_table(const sol::table& root, const char* key) {
    const sol::optional<sol::table> section = root[key];
    if (!section.has_value()) {
        throw std::runtime_error(std::format("config: seção obrigatória ausente: {}", key));
    }
    return *section;
}

} // namespace

ServerConfig ServerConfig::load_from_file(const std::string& path) {
    sol::state lua;
    // Sem bibliotecas de io/os: um arquivo de configuração não precisa de
    // acesso a disco nem a processos.
    lua.open_libraries(sol::lib::base, sol::lib::math, sol::lib::string, sol::lib::table);

    const sol::load_result chunk = lua.load_file(path);
    if (!chunk.valid()) {
        const sol::error err = chunk;
        throw std::runtime_error(std::format("config: não foi possível carregar '{}': {}", path,
                                             err.what()));
    }

    const sol::protected_function_result result = chunk.get<sol::protected_function>()();
    if (!result.valid()) {
        const sol::error err = result;
        throw std::runtime_error(
            std::format("config: erro ao executar '{}': {}", path, err.what()));
    }

    const sol::optional<sol::table> root = result;
    if (!root.has_value()) {
        throw std::runtime_error(std::format("config: '{}' precisa retornar uma tabela", path));
    }

    ServerConfig config;

    const sol::table network = require_table(*root, "network");
    config.port = require_integer<std::uint16_t>(network, "network", "port", 1, 65535);

    const sol::table simulation = require_table(*root, "simulation");
    config.ticks_per_second = require_integer<std::uint32_t>(simulation, "simulation",
                                                             "ticks_per_second", 1, 240);

    const sol::table world = require_table(*root, "world");
    const sol::optional<std::string> map_path = world["map"];
    if (!map_path.has_value() || map_path->empty()) {
        throw std::runtime_error("config: campo obrigatório ausente: world.map");
    }
    config.map_path = *map_path;

    return config;
}

} // namespace backlands::core
