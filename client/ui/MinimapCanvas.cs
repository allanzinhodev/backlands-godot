using System.Collections.Generic;
using Backlands.Model;
using Godot;

namespace Backlands.Ui;

/// <summary>
/// O desenho do minimapa: o grid do servidor, 4 px por célula, e um ponto de
/// 3×3 por entidade viva.
///
/// O grid vira uma <see cref="ImageTexture"/> de 1 pixel por célula, montada
/// UMA vez quando o mapa chega e desenhada ampliada sem suavização — mais
/// barato que 1024 retângulos por quadro, e as cores saem chapadas como no
/// mapa de verdade. Só os pontos são redesenhados a cada refresh.
/// </summary>
public sealed partial class MinimapCanvas : Control
{
    public const int PixelsPerCell = 4;

    private const int DotSize = 3;

    // O mesmo amarelo do nome do jogador: um lugar só para a cor dele.
    private static readonly Color LocalColor = UiStyle.LocalName;

    /// Morto, o próprio jogador continua no mapa, só apagado: é onde o corpo
    /// está, e no Ragnarok o marcador não some — sumir faria parecer que o
    /// jogador saiu do mapa.
    private static readonly Color LocalDeadColor = new(0.55f, 0.52f, 0.35f);
    private static readonly Color LocalBorder = new(0.10f, 0.10f, 0.12f);
    private static readonly Color PlayerColor = Colors.White;
    private static readonly Color MonsterColor = new(0.9f, 0.3f, 0.3f);

    // Cores próximas das do MapRenderer, para o minimapa ler como o mapa. A
    // árvore é bem mais escura que a grama do mapa de verdade: em 4 px por
    // célula o tom das copas não se distingue, e o minimapa existe justamente
    // para mostrar por onde NÃO se passa.
    private static readonly Color Grass = Color.FromHtml("46713a");
    private static readonly Color Tree = Color.FromHtml("22401c");
    private static readonly Color Water = Color.FromHtml("3f8fb0");
    private static readonly Color Rock = Color.FromHtml("929064");
    private static readonly Color Paving = Color.FromHtml("aaa477");

    private readonly List<(Vector2 Cell, Color Color, bool Local)> _dots = new();
    private ImageTexture? _grid;

    public MinimapCanvas()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        // Nearest: cada célula é um quadrado chapado de 4 px, não um borrão.
        TextureFilter = TextureFilterEnum.Nearest;
        // Até o mapa chegar, o espaço de um mapa de 32×32, para a moldura não
        // nascer colapsada.
        CustomMinimumSize = new Vector2(32, 32) * PixelsPerCell;
    }

    public void SetMap(MapData map)
    {
        var image = Image.CreateEmpty(map.Width, map.Height, false, Image.Format.Rgba8);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                image.SetPixel(x, y, ColorFor(map.At(x, y)));
            }
        }

        _grid = ImageTexture.CreateFromImage(image);
        CustomMinimumSize = new Vector2(map.Width, map.Height) * PixelsPerCell;
        QueueRedraw();
    }

    public void Clear()
    {
        _grid = null;
        _dots.Clear();
        QueueRedraw();
    }

    public void Refresh(WorldState world, Vector2? localCellPosition)
    {
        _dots.Clear();
        foreach (var (id, entity) in world.Entities)
        {
            var local = id == world.LocalEntityId;

            // Cadáver dos outros não aparece: o minimapa é de quem está em
            // jogo. O próprio jogador fica, apagado, até renascer.
            if (!entity.Alive && !local)
            {
                continue;
            }

            var cell = local && localCellPosition is { } interpolated
                ? interpolated
                : new Vector2(entity.CellX + 0.5f, entity.CellY + 0.5f);
            var color = local ? (entity.Alive ? LocalColor : LocalDeadColor)
                      : entity.Kind == EntityKind.Monster ? MonsterColor
                      : PlayerColor;
            _dots.Add((cell, color, local));
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_grid == null)
        {
            return;
        }

        DrawTextureRect(_grid, new Rect2(Vector2.Zero, _grid.GetSize() * PixelsPerCell), false);

        // O jogador local por último, para ficar por cima de quem estiver na
        // mesma célula.
        foreach (var dot in _dots)
        {
            if (!dot.Local)
            {
                DrawDot(dot.Cell, dot.Color, border: false);
            }
        }
        foreach (var dot in _dots)
        {
            if (dot.Local)
            {
                DrawDot(dot.Cell, dot.Color, border: true);
            }
        }
    }

    /// Um ponto de 3×3 centrado no pixel da célula; com borda, 5×5 escuro por
    /// baixo. Arredondado ao pixel, para os pontos saírem nítidos.
    private void DrawDot(Vector2 cell, Color color, bool border)
    {
        var center = (cell * PixelsPerCell).Round();
        const int half = DotSize / 2;
        if (border)
        {
            DrawRect(new Rect2(center - new Vector2(half + 1, half + 1),
                               new Vector2(DotSize + 2, DotSize + 2)), LocalBorder);
        }
        DrawRect(new Rect2(center - new Vector2(half, half), new Vector2(DotSize, DotSize)), color);
    }

    private static Color ColorFor(MapCell cell) => cell.Terrain switch
    {
        Terrain.Water => Water,
        Terrain.Rock => Rock,
        Terrain.Bridge or Terrain.Dirt => Paving,
        // Grama que não se anda é árvore.
        _ => cell.Walkable ? Grass : Tree,
    };
}
