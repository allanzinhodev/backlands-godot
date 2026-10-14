#pragma once

#include "game/Entity.hpp"
#include "game/EntityRegistry.hpp"
#include "game/Geometry.hpp"
#include "game/MapDefinition.hpp"
#include "game/Rng.hpp"

namespace backlands::game {

// Povoa o mapa com os monstros dos pontos de spawn e devolve quantos nasceram.
// O consumo do Rng faz parte do contrato: com a semente fixa do mundo, a mesma
// ordem de sorteios põe os mesmos monstros nas mesmas células a cada execução.
int spawn_map_monsters(EntityRegistry& registry, const MapDefinition& map, Rng& rng);

// Copia atributos, aparência e comportamento do modelo para o monstro recém
// criado, com `home` como a casa para onde ele volta e onde renasce.
void apply_monster_template(Entity& monster, const MonsterTemplate& tmpl, Cell home);

} // namespace backlands::game
