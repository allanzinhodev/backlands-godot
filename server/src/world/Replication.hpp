#pragma once

#include "game/Combat.hpp"
#include "game/Entity.hpp"
#include "game/EntityRegistry.hpp"
#include "game/MapGrid.hpp"
#include "protocol/LoginFailure.hpp"

#include <cstddef>
#include <cstdint>
#include <string_view>
#include <vector>

// Um construtor de frame por pacote S_*: é aqui, e só aqui, que o estado do
// jogo vira bytes no fio. Juntar todos num lugar deixa o formato de cada
// pacote conferível contra docs/protocol.md sem caçar escritas espalhadas
// pelo World. Os comentários sobre cada formato ficam no .cpp, junto das
// escritas.
namespace backlands::world {

[[nodiscard]] std::vector<std::byte> make_enter_world_frame(const game::Entity& entity,
                                                            std::string_view map_name);

[[nodiscard]] std::vector<std::byte> make_spawn_frame(const game::Entity& entity);

[[nodiscard]] std::vector<std::byte> make_despawn_frame(game::EntityId id);

[[nodiscard]] std::vector<std::byte> make_map_info_frame(const game::MapGrid& grid,
                                                         std::string_view map_name);

[[nodiscard]] std::vector<std::byte> make_map_row_frame(const game::MapGrid& grid,
                                                        std::uint16_t y);

[[nodiscard]] std::vector<std::byte> make_walk_frame(const game::Entity& entity);
[[nodiscard]] std::vector<std::byte> make_stop_frame(const game::Entity& entity);
[[nodiscard]] std::vector<std::byte> make_attack_frame(const game::CombatEvent& event);
[[nodiscard]] std::vector<std::byte> make_died_frame(const game::CombatEvent& event);
[[nodiscard]] std::vector<std::byte> make_respawn_frame(const game::Entity& entity);

[[nodiscard]] std::vector<std::byte> make_login_failed_frame(protocol::LoginFailure reason);
[[nodiscard]] std::vector<std::byte> make_attack_rejected_frame(game::AttackRejection rejection);

[[nodiscard]] std::vector<std::byte> make_pong_frame(std::uint32_t nonce);

// Motivo de recusa do registro de entidades -> motivo no fio. Os valores
// numéricos coincidem (conferido em tempo de compilação no .cpp).
[[nodiscard]] protocol::LoginFailure to_login_failure(
    game::EntityRegistry::SpawnError error) noexcept;

} // namespace backlands::world
