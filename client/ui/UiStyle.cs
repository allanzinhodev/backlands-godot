using Godot;

namespace Backlands.Ui;

/// <summary>
/// O visual comum de toda a UI em jogo: fundo escuro azulado semitransparente,
/// borda fina clara de 1 px, cantos de 3 px e fonte pequena — a janela do
/// Ragnarok clássico.
///
/// Mora num lugar só para a barra de skills, a janela básica, o minimapa e a
/// janela de opções parecerem uma peça só: quem mudar o tom aqui muda o
/// conjunto, em vez de caçar as mesmas cores repetidas em quatro arquivos.
/// </summary>
public static class UiStyle
{
    public static readonly Color PanelBackground = new(0.07f, 0.07f, 0.10f, 0.85f);

    public static readonly Color PanelBorder = new(0.45f, 0.42f, 0.35f);

    /// Fundo das barras: mais escuro e mais opaco que o painel, para o
    /// preenchimento ter contraste mesmo com o mundo atrás.
    public static readonly Color BarBackground = new(0.04f, 0.04f, 0.06f, 0.95f);

    public static readonly Color HpFill = new(0.35f, 0.80f, 0.35f);

    /// Abaixo de <see cref="HpLowFraction"/> a barra de HP fica vermelha,
    /// como o Ragnarok avisa.
    public static readonly Color HpLowFill = new(0.85f, 0.25f, 0.25f);

    public const float HpLowFraction = 0.25f;

    public static readonly Color SpFill = new(0.35f, 0.55f, 0.95f);

    /// Texto comum: o mesmo creme das teclas da barra de skills.
    public static readonly Color Text = new(0.95f, 0.92f, 0.75f);

    /// O nome do próprio jogador, igual ao nome que flutua sobre ele.
    public static readonly Color LocalName = new(1.0f, 0.95f, 0.5f);

    public static readonly Color Outline = new(0, 0, 0);

    /// Ícones desenhados em código, como a engrenagem das opções.
    public static readonly Color Icon = new(0.88f, 0.85f, 0.72f);

    public const int FontSmall = 11;

    public const int FontNormal = 12;

    public const int FontTitle = 13;

    /// O painel: fundo escuro, borda de 1 px, cantos de 3. Uma instância nova
    /// por chamada, para quem precisar de uma variação (botão em hover)
    /// alterar a sua sem mexer nas outras.
    public static StyleBoxFlat Panel()
    {
        var style = new StyleBoxFlat
        {
            BgColor = PanelBackground,
            BorderColor = PanelBorder,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
        };
        style.SetBorderWidthAll(1);
        return style;
    }

    /// Fundo ou preenchimento de uma barra (HP, SP): cor chapada, sem borda,
    /// cantos de 2 para não brigar com os cantos de 3 do painel em volta.
    public static StyleBoxFlat Bar(Color fill)
    {
        return new StyleBoxFlat
        {
            BgColor = fill,
            CornerRadiusTopLeft = 2,
            CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2,
            CornerRadiusBottomRight = 2,
        };
    }

    /// Fonte pequena do tema padrão e, se pedido, contorno escuro — para o
    /// texto continuar legível quando fica sobre o mundo e não sobre o painel.
    public static void StyleLabel(Label label, int fontSize, Color color, int outline = 0)
    {
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        if (outline > 0)
        {
            label.AddThemeColorOverride("font_outline_color", Outline);
            label.AddThemeConstantOverride("outline_size", outline);
        }
    }

    /// Um botão com a cara do painel: o mesmo fundo e a mesma borda, um pouco
    /// mais claro sob o mouse e mais claro ainda pressionado. Sem anel de foco
    /// nem foco por teclado: a UI não pode ficar com o teclado do jogo.
    public static void StyleButton(Button button)
    {
        var hover = Panel();
        hover.BgColor = new Color(0.13f, 0.13f, 0.18f, 0.9f);
        var pressed = Panel();
        pressed.BgColor = new Color(0.20f, 0.19f, 0.16f, 0.95f);

        button.AddThemeStyleboxOverride("normal", Panel());
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", pressed);
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.AddThemeFontSizeOverride("font_size", FontNormal);
        button.AddThemeColorOverride("font_color", Text);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
        button.FocusMode = Control.FocusModeEnum.None;
    }
}
