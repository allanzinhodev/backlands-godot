#pragma once

#include "game/Entity.hpp"
#include "game/EntityRegistry.hpp"
#include "net/Session.hpp"

#include <cstddef>
#include <cstdint>
#include <string_view>
#include <unordered_map>
#include <vector>

namespace backlands::game {

// Cola entre as entidades do mundo e as sessões de rede: decide quem recebe o
// quê. A política de spawn em si mora em EntityRegistry, que não conhece rede.
//
// Dono do estado é o thread único do io_context: sem locks por design.
class World {
public:
    // Célula onde um jogador nasce. Fixa até o M4 trazer o grid do mapa.
    static constexpr Cell kSpawnCell{16, 16};
    static constexpr std::string_view kMapName = "starter_fields";

    void add_session(const net::SessionPtr& session);
    void remove_session(net::SessionId id);

    [[nodiscard]] net::Session* find_session(net::SessionId id) noexcept;
    [[nodiscard]] std::size_t session_count() const noexcept { return sessions_.size(); }

    [[nodiscard]] EntityRegistry& entities() noexcept { return entities_; }

    // Faz o jogador entrar no mundo: cria a entidade, manda o estado inicial a
    // ele e anuncia aos demais. Em caso de recusa, o motivo já foi enviado ao
    // client e é devolvido aqui para quem chama decidir se derruba a sessão.
    EntityRegistry::SpawnError try_login(net::Session& session, std::string_view name);

    // Envia o frame a todas as sessões, opcionalmente pulando uma (em geral a
    // que originou a ação).
    void broadcast(const std::vector<std::byte>& frame, net::SessionId except = 0);

    // Chamado pelo GameLoop a cada tick. `delta_ms` é o passo fixo configurado.
    void tick(std::uint32_t delta_ms);

    [[nodiscard]] std::uint64_t tick_count() const noexcept { return tick_count_; }

private:
    void send_existing_entities(net::Session& session, EntityId skip_id);

    std::unordered_map<net::SessionId, net::SessionPtr> sessions_;
    EntityRegistry entities_;
    std::uint64_t tick_count_ = 0;
};

} // namespace backlands::game
