#pragma once

#include "game/Entity.hpp"
#include "game/EntityRegistry.hpp"
#include "game/MapDefinition.hpp"
#include "game/MapGrid.hpp"
#include "game/Rng.hpp"
#include "world/Connection.hpp"
#include "world/ConnectionRegistry.hpp"

#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>
#include <utility>

namespace backlands::world {

// Cola entre as entidades do mundo e as sessões de rede: decide quem recebe o
// quê. As políticas em si (spawn, pathfinding, movimento) moram nos módulos de
// `game/`, que não conhecem rede.
//
// Dono do estado é o thread único do io_context: sem locks por design.
//
// Fala com a rede só pela interface world::Connection, nunca com o socket:
// assim o mundo inteiro é testável com uma conexão falsa, sem Asio.
class World {
public:
    // Carrega o mapa. Lança se o arquivo estiver inconsistente — subir com
    // mapa torto só apareceria depois, como jogador atravessando o rio.
    void load_map(const std::string& lua_path);

    // Folha de sprites dos jogadores, vinda da configuração.
    void set_player_sprite(std::string sprite) { player_sprite_ = std::move(sprite); }

    void add_connection(const ConnectionPtr& connection);
    void remove_connection(ConnectionId id);

    [[nodiscard]] std::size_t connection_count() const noexcept { return connections_.size(); }

    [[nodiscard]] game::EntityRegistry& entities() noexcept { return entities_; }
    [[nodiscard]] const game::MapGrid& grid() const noexcept { return map_.grid; }
    [[nodiscard]] std::string_view map_name() const noexcept { return map_.name; }

    // Faz o jogador entrar no mundo: cria a entidade, envia o mapa e o estado
    // inicial a ele, e anuncia aos demais. Em caso de recusa, o motivo já foi
    // enviado ao client e é devolvido aqui para quem chama decidir se derruba.
    game::EntityRegistry::SpawnError try_login(Connection& connection, std::string_view name);

    // Pedido de caminhada do client. Devolve false só em violação de protocolo
    // (sessão sem entidade); destino inválido é recusa normal, não violação.
    bool request_walk(Connection& connection, game::Cell destination);

    // Pedido de ataque. Seleciona o alvo; o golpe em si sai no tick, quando o
    // alcance e o cooldown permitirem. Devolve false só em violação de
    // protocolo (sessão sem entidade no mundo).
    bool request_attack(Connection& connection, game::EntityId target_id);

    // Chamado pelo GameLoop a cada tick, com o passo fixo configurado.
    void tick(std::uint32_t delta_ms);

    [[nodiscard]] std::uint64_t tick_count() const noexcept { return tick_count_; }

    // Relógio lógico do mundo em ms. É ele, e não o relógio do sistema, que
    // governa o movimento — o que mantém a simulação determinística.
    [[nodiscard]] std::uint32_t now_ms() const noexcept { return world_time_ms_; }

private:
    void send_map(Connection& connection);
    void send_existing_entities(Connection& connection, game::EntityId skip_id);
    void publish_movement_events();
    void publish_combat_events();
    void publish_monster_walks();
    void publish_chase_events();
    void publish_respawns();
    void sweep_closed_connections();

    ConnectionRegistry connections_;
    game::EntityRegistry entities_;
    game::MapDefinition map_;
    std::string player_sprite_ = "player";
    std::uint64_t tick_count_ = 0;
    std::uint32_t world_time_ms_ = 0;

    // Semente fixa: o mundo é reproduzível entre execuções, o que torna um
    // playtest comparável com o anterior.
    game::Rng rng_{0xB4C41A1Du};
};

} // namespace backlands::world
