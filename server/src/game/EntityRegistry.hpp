#pragma once

#include "game/Entity.hpp"

#include <cstdint>
#include <string_view>
#include <unordered_map>
#include <vector>

namespace backlands::game {

// Dono das entidades do mundo: aloca ids, indexa por sessão e aplica as regras
// de entrada (nome válido, nome livre, uma entidade por sessão).
//
// Sem Asio: toda a política de spawn/despawn é testável sem abrir socket.
class EntityRegistry {
public:
    // Motivo de recusa de um login. O client recebe isto como código.
    enum class SpawnError : std::uint8_t {
        None = 0,
        InvalidName = 1,
        NameInUse = 2,
        AlreadySpawned = 3,
        ProtocolMismatch = 4,
    };

    struct SpawnResult {
        EntityId id = kNoEntity;
        SpawnError error = SpawnError::None;

        [[nodiscard]] bool ok() const noexcept { return error == SpawnError::None; }
    };

    // Cria a entidade de um jogador. `session_id` precisa ser diferente de 0.
    SpawnResult spawn_player(std::uint32_t session_id, std::string_view name, Cell cell);

    // Cria um monstro. Sem sessão e sem as regras de nome dos jogadores: o
    // nome vem do arquivo de mapa, não de entrada de usuário, e vários
    // monstros da mesma espécie compartilham o mesmo nome de propósito.
    Entity& spawn_monster(std::string_view name, Cell cell);

    // Remove a entidade da sessão. Devolve o id removido, ou kNoEntity se a
    // sessão não tinha entidade (desconexão antes do login, por exemplo).
    EntityId despawn_session(std::uint32_t session_id);

    [[nodiscard]] Entity* find(EntityId id) noexcept;
    [[nodiscard]] const Entity* find(EntityId id) const noexcept;
    [[nodiscard]] Entity* find_by_session(std::uint32_t session_id) noexcept;

    [[nodiscard]] bool is_name_in_use(std::string_view name) const noexcept;

    [[nodiscard]] std::size_t size() const noexcept { return entities_.size(); }

    // Entidades já no mundo, para enviar ao jogador que acabou de entrar.
    [[nodiscard]] std::vector<const Entity*> all() const;

    // Versão mutável, para os sistemas que avançam o estado (movimento hoje,
    // combate e status effects depois).
    [[nodiscard]] std::vector<Entity*> all_mutable();

private:
    std::unordered_map<EntityId, Entity> entities_;
    std::unordered_map<std::uint32_t, EntityId> session_to_entity_;

    // Ids nunca são reutilizados dentro de uma execução: um pacote atrasado
    // referente a uma entidade morta não pode acertar uma entidade nova.
    EntityId next_id_ = 1;
};

} // namespace backlands::game
