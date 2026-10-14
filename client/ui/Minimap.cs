using Backlands.Model;
using Godot;

namespace Backlands.Ui;

/// <summary>
/// O minimapa do canto superior direito: o nome do mapa em cima e, abaixo,
/// o grid do servidor com um ponto por entidade viva.
///
/// Norte para cima e sem girar com a câmera, como no Ragnarok: o mapa é a
/// referência fixa que o jogador consulta justamente quando girou a câmera e
/// perdeu o norte. O desenho em si é do <see cref="MinimapCanvas"/>; esta
/// classe é a moldura e o título.
/// </summary>
public sealed partial class Minimap : PanelContainer
{
    private const int ScreenMargin = 8;
    private const int Padding = 5;

    private Label _title = null!;
    private MinimapCanvas _canvas = null!;

    public Minimap()
    {
        Name = "Minimap";
        Visible = false;
        AddThemeStyleboxOverride("panel", UiStyle.Panel());

        // Preso ao canto superior DIREITO: âncoras na borda direita, e o
        // painel cresce para a esquerda a partir dela, seja qual for a largura
        // que o mapa pedir.
        AnchorLeft = 1.0f;
        AnchorRight = 1.0f;
        AnchorTop = 0.0f;
        AnchorBottom = 0.0f;
        OffsetLeft = -ScreenMargin;
        OffsetRight = -ScreenMargin;
        OffsetTop = ScreenMargin;
        OffsetBottom = ScreenMargin;
        GrowHorizontal = GrowDirection.Begin;
        GrowVertical = GrowDirection.End;
    }

    public override void _Ready()
    {
        var margin = new MarginContainer { Name = "Margin" };
        margin.AddThemeConstantOverride("margin_left", Padding);
        margin.AddThemeConstantOverride("margin_right", Padding);
        margin.AddThemeConstantOverride("margin_top", Padding);
        margin.AddThemeConstantOverride("margin_bottom", Padding);
        AddChild(margin);

        var column = new VBoxContainer { Name = "Column" };
        column.AddThemeConstantOverride("separation", 3);
        margin.AddChild(column);

        _title = new Label
        {
            Name = "Title",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        UiStyle.StyleLabel(_title, UiStyle.FontSmall, UiStyle.Text);
        column.AddChild(_title);

        _canvas = new MinimapCanvas { Name = "Canvas" };
        column.AddChild(_canvas);
    }

    /// O mapa chegou inteiro: título e grid, desenhado uma vez.
    public void SetMap(MapData map)
    {
        _title.Text = map.Name;
        _canvas.SetMap(map);
    }

    /// Os pontos das entidades. <paramref name="localCellPosition"/> é a
    /// célula FRACIONÁRIA sob a posição interpolada do jogador — a que a
    /// câmera segue —, para o ponto dele deslizar em vez de pular de célula.
    public void Refresh(WorldState world, Vector2? localCellPosition) =>
        _canvas.Refresh(world, localCellPosition);

    /// Esquece o mapa e os pontos, como ao cair a conexão.
    public void Clear() => _canvas.Clear();
}
