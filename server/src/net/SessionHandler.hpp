#pragma once

#include <cstddef>
#include <cstdint>
#include <memory>
#include <span>

namespace backlands::net {

class Session;

// Mora aqui, e não em Session.hpp, porque a interface abaixo só conhece a
// declaração antecipada de Session; Session.hpp inclui este arquivo.
using SessionPtr = std::shared_ptr<Session>;

// O que o transporte precisa de quem está do outro lado: ser avisado de que
// uma sessão abriu, receber cada pacote já enquadrado e saber quando ela
// fechou. Session e Listener dependem só desta interface — não conhecem o
// mundo nem os handlers, e quem decide o que fazer com os bytes fica fora da
// camada de socket.
class SessionHandler {
public:
    virtual ~SessionHandler() = default;

    virtual void on_session_opened(const SessionPtr& session) = 0;

    // Devolve false se a sessão deve ser encerrada.
    [[nodiscard]] virtual bool on_packet(Session& session, std::uint16_t opcode,
                                         std::span<const std::byte> payload) = 0;

    virtual void on_session_closed(Session& session) = 0;
};

} // namespace backlands::net
