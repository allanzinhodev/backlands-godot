using Backlands.Model;
using Godot;

namespace Backlands.Scene;

/// <summary>
/// A ponte entre a célula do servidor e o espaço 3D da cena.
///
/// Uma célula é um tile de 1 unidade no plano XZ: o x da célula vira o X do
/// mundo e o y vira o Z. Tudo que converte entre os dois passa por aqui, para
/// não existirem duas ideias de "onde fica a célula".
/// </summary>
public static class GridSpace
{
    public const float TileSize = 1.0f;

    public static Vector3 CellToWorld(int x, int y, float height = 0) => new(x + 0.5f, height, y + 0.5f);

    public static (int X, int Y) WorldToCell(Vector3 p) => (Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Z));

    /// A célula FRACIONÁRIA sob um ponto do mundo: o centro da célula (3, 4)
    /// é (3,5, 4,5). Para o minimapa seguir a posição interpolada do
    /// personagem em vez de saltar de célula em célula.
    public static Vector2 WorldToCellPosition(Vector3 p) => new(p.X, p.Z);

    /// Qual das 8 direções lógicas mais aponta de `from` para `to`.
    public static Direction FacingTowards(Vector3 from, Vector3 to)
    {
        var delta = to - from;
        var best = Direction.SouthEast;
        var bestDot = float.NegativeInfinity;

        // Comparar com as 8 direções reais evita reinventar a conversão de
        // ângulo para índice — e evita errar a origem e o sentido do enum.
        for (var d = 0; d < Directions.Count; d++)
        {
            var step = Directions.Delta((Direction)d);
            var candidate = new Vector3(step.X, 0, step.Y).Normalized();
            var dot = candidate.Dot(new Vector3(delta.X, 0, delta.Z).Normalized());
            if (dot > bestDot)
            {
                bestDot = dot;
                best = (Direction)d;
            }
        }

        return best;
    }
}
