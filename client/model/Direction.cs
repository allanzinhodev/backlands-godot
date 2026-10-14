namespace Backlands.Model;

/// As 8 direções, na ordem usada no fio (0 = sul, sentido anti-horário).
public enum Direction : byte
{
    South = 0,
    SouthWest = 1,
    West = 2,
    NorthWest = 3,
    North = 4,
    NorthEast = 5,
    East = 6,
    SouthEast = 7,
}

/// Geometria das direções, num lugar só: o fio, o espelho do mundo, a cena e a
/// arte precisam da MESMA tabela, e duas cópias acabariam discordando.
public static class Directions
{
    /// Quantas direções existem; os laços que percorrem todas contam até aqui.
    public const int Count = 8;

    /// Deslocamento de cada direção, na mesma ordem do servidor
    /// (server/src/game/Geometry.hpp). y cresce para o sul.
    private static readonly (int X, int Y)[] Deltas =
    {
        (0, 1),   // South
        (-1, 1),  // SouthWest
        (-1, 0),  // West
        (-1, -1), // NorthWest
        (0, -1),  // North
        (1, -1),  // NorthEast
        (1, 0),   // East
        (1, 1),   // SouthEast
    };

    /// Quanto um passo nesta direção move, em células.
    public static (int X, int Y) Delta(Direction direction) => Deltas[(int)direction];
}
