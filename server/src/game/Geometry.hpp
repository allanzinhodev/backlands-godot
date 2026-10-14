#pragma once

#include <cstdint>

// Geometria do grid: célula, as 8 direções e as contas entre elas. Fica num
// arquivo próprio porque entidade, mapa, pathfinding, combate e IA dependem
// disto, e nenhum deles deveria ter de puxar os outros só para ter uma Cell.
namespace backlands::game {

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

// Deslocamento de cada direção em células. Índice = valor de Direction.
struct DirectionDelta {
    std::int8_t dx;
    std::int8_t dy;
};

// y cresce para o sul (mesma orientação do eixo Z no mundo 3D).
inline constexpr DirectionDelta kDirectionDeltas[kDirectionCount] = {
    {0, 1},   // South
    {-1, 1},  // SouthWest
    {-1, 0},  // West
    {-1, -1}, // NorthWest
    {0, -1},  // North
    {1, -1},  // NorthEast
    {1, 0},   // East
    {1, 1},   // SouthEast
};

[[nodiscard]] constexpr bool is_diagonal(Direction dir) noexcept {
    return (static_cast<std::uint8_t>(dir) % 2) == 1;
}

// Aplica um passo. Não valida: quem chama decide com can_step/is_walkable.
[[nodiscard]] Cell step(Cell from, Direction dir) noexcept;

// Direção dominante de `from` para `to`, para virar o personagem.
[[nodiscard]] Direction direction_towards(Cell from, Cell to) noexcept;

// Distância de Chebyshev em células: num grid com diagonais, mover-se na
// diagonal cobre uma célula em cada eixo, então o alcance tem de ser medido
// assim — usar distância euclidiana faria o alcance diagonal parecer maior do
// que o movimento permite.
[[nodiscard]] int cell_distance(Cell a, Cell b) noexcept;

} // namespace backlands::game
