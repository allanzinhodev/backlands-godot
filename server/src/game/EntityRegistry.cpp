#include "game/EntityRegistry.hpp"

namespace backlands::game {

EntityRegistry::SpawnResult EntityRegistry::spawn_player(std::uint32_t session_id,
                                                         std::string_view name, Cell cell) {
    if (session_id == 0) {
        return {kNoEntity, SpawnError::AlreadySpawned};
    }
    if (session_to_entity_.contains(session_id)) {
        return {kNoEntity, SpawnError::AlreadySpawned};
    }
    if (!is_valid_character_name(name)) {
        return {kNoEntity, SpawnError::InvalidName};
    }
    if (is_name_in_use(name)) {
        return {kNoEntity, SpawnError::NameInUse};
    }

    const EntityId id = next_id_++;

    Entity entity;
    entity.id = id;
    entity.name = std::string(name);
    entity.cell = cell;
    entity.facing = Direction::South;
    entity.session_id = session_id;

    entities_.emplace(id, std::move(entity));
    session_to_entity_.emplace(session_id, id);

    return {id, SpawnError::None};
}

EntityId EntityRegistry::despawn_session(std::uint32_t session_id) {
    const auto it = session_to_entity_.find(session_id);
    if (it == session_to_entity_.end()) {
        return kNoEntity;
    }

    const EntityId id = it->second;
    session_to_entity_.erase(it);
    entities_.erase(id);
    return id;
}

Entity* EntityRegistry::find(EntityId id) noexcept {
    const auto it = entities_.find(id);
    return it == entities_.end() ? nullptr : &it->second;
}

const Entity* EntityRegistry::find(EntityId id) const noexcept {
    const auto it = entities_.find(id);
    return it == entities_.end() ? nullptr : &it->second;
}

Entity* EntityRegistry::find_by_session(std::uint32_t session_id) noexcept {
    const auto it = session_to_entity_.find(session_id);
    return it == session_to_entity_.end() ? nullptr : find(it->second);
}

bool EntityRegistry::is_name_in_use(std::string_view name) const noexcept {
    for (const auto& [id, entity] : entities_) {
        if (entity.name == name) {
            return true;
        }
    }
    return false;
}

std::vector<const Entity*> EntityRegistry::all() const {
    std::vector<const Entity*> result;
    result.reserve(entities_.size());
    for (const auto& [id, entity] : entities_) {
        result.push_back(&entity);
    }
    return result;
}

std::vector<Entity*> EntityRegistry::all_mutable() {
    std::vector<Entity*> result;
    result.reserve(entities_.size());
    for (auto& [id, entity] : entities_) {
        result.push_back(&entity);
    }
    return result;
}

} // namespace backlands::game
