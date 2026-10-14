using System;
using Godot;

namespace Backlands.Ui;

/// <summary>
/// A janela de opções, aberta pela engrenagem da janela básica: mostrar ou
/// esconder a ajuda dos controles, desconectar e fechar.
///
/// Só o que existe de verdade entra aqui: a ajuda é o HUD de texto, e
/// desconectar volta ao login — o que o Ragnarok faz em "Sair para a seleção
/// de personagem". Quem executa é a raiz da cena, pelos eventos; a janela não
/// toca no HUD nem na sessão.
/// </summary>
public sealed partial class OptionsWindow : PanelContainer
{
    private const int Width = 240;
    private const int Padding = 12;

    private CheckButton _help = null!;

    /// O jogador ligou (true) ou desligou a ajuda dos controles.
    public event Action<bool>? HelpToggled;

    public event Action? DisconnectRequested;

    public OptionsWindow()
    {
        Name = "Options";
        Visible = false;
        CustomMinimumSize = new Vector2(Width, 0);
        AddThemeStyleboxOverride("panel", UiStyle.Panel());

        // Centro de verdade: âncoras no meio da tela e crescimento para os
        // dois lados, para o painel crescer EM VOLTA do centro — e não a
        // partir dele para baixo e para a direita, como o painel de login.
        AnchorLeft = 0.5f;
        AnchorRight = 0.5f;
        AnchorTop = 0.5f;
        AnchorBottom = 0.5f;
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Both;
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
        column.AddThemeConstantOverride("separation", 8);
        margin.AddChild(column);

        var title = new Label
        {
            Name = "Title",
            Text = "Opções",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        UiStyle.StyleLabel(title, UiStyle.FontTitle, UiStyle.Text);
        column.AddChild(title);

        _help = new CheckButton
        {
            Name = "Help",
            Text = "Mostrar ajuda dos controles",
            FocusMode = FocusModeEnum.None,
        };
        _help.AddThemeFontSizeOverride("font_size", UiStyle.FontNormal);
        _help.AddThemeColorOverride("font_color", UiStyle.Text);
        _help.Toggled += pressed => HelpToggled?.Invoke(pressed);
        column.AddChild(_help);

        var disconnect = new Button { Name = "Disconnect", Text = "Desconectar" };
        UiStyle.StyleButton(disconnect);
        disconnect.Pressed += () => DisconnectRequested?.Invoke();
        column.AddChild(disconnect);

        var close = new Button { Name = "Close", Text = "Fechar" };
        UiStyle.StyleButton(close);
        close.Pressed += Close;
        column.AddChild(close);
    }

    /// Abre com a caixa da ajuda refletindo o estado REAL do HUD, ou fecha se
    /// já estava aberta — é o que a engrenagem faz.
    public void Toggle(bool helpVisible)
    {
        if (Visible)
        {
            Close();
        }
        else
        {
            Open(helpVisible);
        }
    }

    public void Open(bool helpVisible)
    {
        // Sem sinal: é o estado atual sendo mostrado, não o jogador mudando.
        _help.SetPressedNoSignal(helpVisible);
        Visible = true;
    }

    public void Close() => Visible = false;
}
