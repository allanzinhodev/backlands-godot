namespace Backlands.Model;

/// Trajeto recebido do servidor: por onde a entidade vai passar.
public sealed class WalkOrder
{
    public WalkOrder(uint entityId, int fromX, int fromY, int toX, int toY, int speedMsPerCell,
                     Direction[] path)
    {
        EntityId = entityId;
        FromX = fromX;
        FromY = fromY;
        ToX = toX;
        ToY = toY;
        SpeedMsPerCell = speedMsPerCell;
        Path = path;
    }

    public uint EntityId { get; }

    public int FromX { get; }

    public int FromY { get; }

    /// Célula final do trajeto, derivada do caminho recebido.
    public int ToX { get; }

    public int ToY { get; }

    public int SpeedMsPerCell { get; }

    public Direction[] Path { get; }
}
