#pragma once

#include "game/Entity.hpp"
#include "game/EntityRegistry.hpp"
#include "game/MapGrid.hpp"
#include "game/MonsterAi.hpp"
#include "net/Session.hpp"

#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

namespace backlands::game {

// Cola entre as entidades do mundo e as sessões de rede: decide quem recebe o
// quê. As políticas em si (spawn, pathfinding, movimento) moram nos módulos de
// `core`, que não conhecem rede.
//
// Dono do estado é o thread único do io_context: sem locks por design.
class World {
public:
    // Carrega o mapa. Lança se o arquivo estiver inconsistente — subir com
    // mapa torto só apareceria depois, como jogador atravessando o rio.
    void load_map(const std::string& lua_path);

    // Folha de sprites dos jogadores, vinda da configuração.
    void set_player_sprite(std::string sprite) { player_sprite_ = std::move(sprite); }

    void add_session(const net::SessionPtr& session);
    void remove_session(net::SessionId id);

    [[nodiscard]] net::Session* find_session(net::SessionId id) noexcept;
    [[nodiscard]] std::size_t session_count() const noexcept { return sessions_.size(); }

    [[nodiscard]] EntityRegistry& entities() noexcept { return entities_; }
    [[nodiscard]] const MapGrid& grid() const noexcept { return map_.grid; }
    [[nodiscard]] std::string_view map_name() const noexcept { return map_.name; }

    // Faz o jogador entrar no mundo: cria a entidade, envia o mapa e o estado
    // inicial a ele, e anuncia aos demais. Em caso de recusa, o motivo já foi
    // enviado ao client e é devolvido aqui para quem chama decidir se derruba.
    EntityRegistry::SpawnError try_login(net::Session& session, std::string_view name);

    // Pedido de caminhada do client. Devolve false só em violação de protocolo
    // (sessão sem entidade); destino inválido é recusa normal, não violação.
    bool request_walk(net::Session& session, Cell destination);

    // Pedido de ataque. Seleciona o alvo; o golpe em si sai no tick, quando o
    // alcance e o cooldown permitirem. Devolve false só em violação de
    // protocolo (sessão sem entidade no mundo).
    bool request_attack(net::Session& session, EntityId target_id);

    // Envia o frame a todas as sessões, opcionalmente pulando uma.
    void broadcast(const std::vector<std::byte>& frame, net::SessionId except = 0);

    // Chamado pelo GameLoop a cada tick, com o passo fixo configurado.
    void tick(std::uint32_t delta_ms);

    [[nodiscard]] std::uint64_t tick_count() const noexcept { return tick_count_; }

    // Relógio lógico do mundo em ms. É ele, e não o relógio do sistema, que
    // governa o movimento — o que mantém a simulação determinística.
    [[nodiscard]] std::uint32_t now_ms() const noexcept { return world_time_ms_; }

    // Quanto tempo um jogador fica caído antes de voltar ao ponto inicial.
    static constexpr std::uint32_t kPlayerRespawnDelayMs = 5000;

private:
    void send_map(net::Session& session);
    void send_existing_entities(net::Session& session, EntityId skip_id);
    void sweep_closed_sessions();
    void spawn_map_monsters();
    void advance_chasing();
    void advance_respawns();
    void advance_monsters();
    void publish_combat_events();

    std::unordered_map<net::SessionId, net::SessionPtr> sessions_;
    EntityRegistry entities_;
    MapDefinition map_;
    std::string player_sprite_ = "player";
    std::uint64_t tick_count_ = 0;
    std::uint32_t world_time_ms_ = 0;

    // Semente fixa: o mundo é reproduzível entre execuções, o que torna um
    // playtest comparável com o anterior.
    Rng rng_{0xB4C41A1Du};
};

} // namespace backlands::game
