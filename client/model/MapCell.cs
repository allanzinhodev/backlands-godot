namespace Backlands.Model;

/// Tipo de terreno de uma célula, nos bits altos do byte do grid.
public enum Terrain : byte
{
    Grass = 0,
    Water = 1,
    Rock = 2,
    Bridge = 3,
    Dirt = 4,
}

/// <summary>
/// Decodifica o byte de célula enviado pelo servidor
/// (ver server/src/game/MapGrid.hpp).
///
/// O client usa estas flags para DESENHAR e para dar feedback de cursor.
/// Nunca para decidir passagem: essa decisão é sempre do servidor.
/// </summary>
public readonly struct MapCell
{
    private const byte WalkableBit = 0x01;
    private const byte BlocksLosBit = 0x02;
    private const int TerrainShift = 4;

    public MapCell(byte raw)
    {
        Raw = raw;
    }

    public byte Raw { get; }

    public bool Walkable => (Raw & WalkableBit) != 0;

    public bool BlocksLineOfSight => (Raw & BlocksLosBit) != 0;

    public Terrain Terrain => (Terrain)(Raw >> TerrainShift);
}
