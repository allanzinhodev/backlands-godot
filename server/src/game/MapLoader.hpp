#pragma once

#include "game/MapDefinition.hpp"

#include <string>

namespace backlands::game {

// Carrega um mapa de um arquivo Lua. Lança std::runtime_error com mensagem
// legível em qualquer inconsistência: subir com mapa torto é pior que não
// subir, porque o erro só apareceria como jogador atravessando parede.
[[nodiscard]] MapDefinition load_map_from_lua(const std::string& path);

} // namespace backlands::game
