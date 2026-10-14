#pragma once

#include "game/Geometry.hpp"
#include "game/MapGrid.hpp"

#include <cstdint>
#include <string>
#include <string_view>
#include <vector>

namespace backlands::game {

// Modelo de um monstro, vindo do arquivo de mapa.
struct MonsterTemplate {
    std::string species;
    std::string name;

    // Folha de sprites. Vazio = usa a própria species como chave.
    std::string sprite;
    std::int32_t max_hp = 50;
    std::int32_t attack_min = 7;
    std::int32_t attack_max = 10;
    std::int32_t defense_percent = 0;
    std::int32_t defense_flat = 0;
    std::int32_t hit = 80;
    std::int32_t flee = 60;
    std::uint16_t attack_range = 1;
    std::uint16_t attack_delay_ms = 1872;
    std::uint16_t attack_motion_ms = 672;
    std::uint16_t walk_speed_ms = 400;

    // 0 = passivo: não inicia combate, só revida.
    std::uint16_t view_range = 0;
    std::uint16_t chase_range = 12;
    std::uint32_t respawn_delay_ms = 5000;
};

// Um ponto de nascimento de monstros no mapa.
struct MonsterSpawn {
    std::string species;
    Cell cell;
    std::uint16_t count = 1;

    // Raio em que os monstros se espalham em torno da célula, para não
    // nascerem todos empilhados na mesma posição.
    std::uint16_t spread = 3;
};

// Mapa carregado: grid + metadados.
struct MapDefinition {
    std::string name;
    Cell spawn;
    MapGrid grid;
    std::vector<MonsterTemplate> monsters;
    std::vector<MonsterSpawn> spawns;

    [[nodiscard]] const MonsterTemplate* find_monster(std::string_view species) const noexcept;
};

} // namespace backlands::game
