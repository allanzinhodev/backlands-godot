#include "game/MapDefinition.hpp"

namespace backlands::game {

const MonsterTemplate* MapDefinition::find_monster(std::string_view species) const noexcept {
    for (const MonsterTemplate& monster : monsters) {
        if (monster.species == species) {
            return &monster;
        }
    }
    return nullptr;
}

} // namespace backlands::game
