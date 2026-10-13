#pragma once

#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>
#include <vector>

// Tipos básicos do mundo. Sem nenhum include de Asio por design, para que as
// regras de entidade sejam testáveis sem socket.
namespace backlands::game {

using EntityId = std::uint32_t;

// 0 nunca é uma entidade válida: serve como "nenhuma".
inline constexpr EntityId kNoEntity = 0;

// Posição lógica em células. O servidor nunca guarda posição fracionária: a
// verdade é sempre uma célula inteira, como no Ragnarok.
struct Cell {
    std::uint16_t x = 0;
    std::uint16_t y = 0;

    friend bool operator==(const Cell& lhs, const Cell& rhs) noexcept {
        return lhs.x == rhs.x && lhs.y == rhs.y;
    }
};

// 8 direções, na ordem usada no fio (0 = sul, sentido anti-horário).
enum class Direction : std::uint8_t {
    South = 0,
    SouthWest = 1,
    West = 2,
    NorthWest = 3,
    North = 4,
    NorthEast = 5,
    East = 6,
    SouthEast = 7,
};

inline constexpr std::uint8_t kDirectionCount = 8;

[[nodiscard]] constexpr bool is_valid_direction(std::uint8_t raw) noexcept {
    return raw < kDirectionCount;
}

// Velocidade padrão de caminhada, em milissegundos por célula. 150 ms é o
// valor clássico do Ragnarok para um personagem sem bônus de AGI.
inline constexpr std::uint16_t kDefaultWalkSpeedMs = 150;

// Caminhada em andamento. O caminho é a sequência de direções que o servidor
// vai percorrer, uma célula por vez — o mesmo formato enviado ao client, para
// que os dois percorram exatamente as mesmas células.
struct WalkState {
    std::vector<Direction> path;
    std::size_t next_step = 0;

    // Instante (no relógio lógico do mundo) em que o próximo passo vence.
    std::uint32_t next_step_at_ms = 0;

    std::uint16_t speed_ms_per_cell = kDefaultWalkSpeedMs;

    // Destino pedido, guardado para log e para eventual re-rota.
    Cell destination;

    [[nodiscard]] bool walking() const noexcept { return next_step < path.size(); }

    void clear() noexcept {
        path.clear();
        next_step = 0;
    }
};

// Uma entidade do mundo: player, monstro ou NPC.
struct Entity {
    EntityId id = kNoEntity;
    std::string name;
    Cell cell;
    Direction facing = Direction::South;
    WalkState walk;

    // Sessão que controla esta entidade; 0 para entidades sem jogador
    // (monstros e NPCs, que entram nos marcos seguintes).
    std::uint32_t session_id = 0;

    [[nodiscard]] bool is_player() const noexcept { return session_id != 0; }
};

// Regras de nome de personagem. Validar no servidor (e não só na UI) impede
// que um client modificado registre nome vazio, gigante ou com controle.
inline constexpr std::size_t kMinNameLength = 3;
inline constexpr std::size_t kMaxNameLength = 16;

// Aceita apenas [A-Za-z0-9_]: evita homóglifos, espaços em branco à toa e
// problemas de normalização no chat e nos logs.
[[nodiscard]] bool is_valid_character_name(std::string_view name) noexcept;

} // namespace backlands::game
