using System;
using System.Collections.Generic;
using Godot;

namespace Backlands.Ui;

/// <summary>
/// Barra de habilidades no estilo Ragnarok: 12 slots fixos, acionados por
/// F1–F12 ou por clique.
///
/// É um banco de testes, não o sistema de skills: aciona o efeito LOCALMENTE,
/// sem passar pelo servidor. Quando as skills existirem de verdade, a tecla
/// vira um pedido ao servidor e o efeito só toca quando ele confirmar — como
/// já acontece com o ataque.
/// </summary>
public sealed partial class SkillBar : Control
{
    private const int SlotSize = 48;
    private const int SlotGap = 4;

    private readonly List<TextureRect> _icons = new();
    private readonly List<Panel> _slots = new();

    private IconSheet? _sheet;

    /// Disparado quando o jogador aciona um slot (tecla ou clique).
    public event Action<int, SkillSlot>? SlotTriggered;

    /// Nasce escondida: só aparece quando o personagem entra no mundo.
    public SkillBar()
    {
        Name = "SkillBar";
        Visible = false;
    }

    public override void _Ready()
    {
        // Cobre a tela inteira e deixa o mouse passar: só os slots capturam
        // clique, para a barra não roubar o clique de andar no cenário.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _sheet = IconSheet.Load();

        var row = new HBoxContainer
        {
            Name = "Slots",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        row.AddThemeConstantOverride("separation", SlotGap);
        AddChild(row);

        for (var i = 0; i < TestSkills.Slots.Length; i++)
        {
            row.AddChild(BuildSlot(i, TestSkills.Slots[i]));
        }

        // Ancorada no centro da borda inferior, por âncoras explícitas: um
        // preset de layout sozinho não centraliza um container de largura
        // própria, e a fileira acabaria fora da tela.
        var width = TestSkills.Slots.Length * SlotSize + (TestSkills.Slots.Length - 1) * SlotGap;
        row.AnchorLeft = 0.5f;
        row.AnchorRight = 0.5f;
        row.AnchorTop = 1.0f;
        row.AnchorBottom = 1.0f;
        row.OffsetLeft = -width / 2.0f;
        row.OffsetRight = width / 2.0f;
        row.OffsetTop = -(SlotSize + BottomMargin);
        row.OffsetBottom = -BottomMargin;
    }

    private const int BottomMargin = 16;

    /// A faixa que a barra ocupa a partir da borda de baixo da tela. O que
    /// precisa ficar acima dela (o HUD de texto) mede daqui, em vez de
    /// repetir os números e desencontrar quando a barra mudar.
    public const int BottomLine = SlotSize + BottomMargin;

    private Control BuildSlot(int index, SkillSlot slot)
    {
        var panel = new Panel
        {
            Name = $"Slot{index + 1}",
            CustomMinimumSize = new Vector2(SlotSize, SlotSize),
            TooltipText = $"F{index + 1} — {slot.Label}",
            MouseFilter = MouseFilterEnum.Stop,
        };

        // O mesmo painel das janelas (UiStyle), para a barra e elas parecerem
        // uma peça só.
        panel.AddThemeStyleboxOverride("panel", UiStyle.Panel());

        var icon = new TextureRect
        {
            Name = "Icon",
            Texture = _sheet?.Icon(slot.IconRow, slot.IconColumn),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        panel.AddChild(icon);
        _icons.Add(icon);

        // Número da tecla no canto, como na hotbar do Ragnarok.
        var key = new Label
        {
            Name = "Key",
            Text = $"F{index + 1}",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        UiStyle.StyleLabel(key, UiStyle.FontSmall, UiStyle.Text, outline: 4);
        key.Position = new Vector2(3, SlotSize - 17);
        panel.AddChild(key);

        panel.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                Trigger(index);
            }
        };

        _slots.Add(panel);
        return panel;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        // Teclas cruas em vez de ações do InputMap: é uma barra de teste, e
        // assim ela não exige mexer na configuração do projeto.
        var index = key.Keycode switch
        {
            Key.F1 => 0, Key.F2 => 1, Key.F3 => 2, Key.F4 => 3,
            Key.F5 => 4, Key.F6 => 5, Key.F7 => 6, Key.F8 => 7,
            Key.F9 => 8, Key.F10 => 9, Key.F11 => 10, Key.F12 => 11,
            _ => -1,
        };

        if (index >= 0)
        {
            Trigger(index);
            GetViewport().SetInputAsHandled();
        }
    }

    private void Trigger(int index)
    {
        if (index < 0 || index >= TestSkills.Slots.Length)
        {
            return;
        }

        Flash(index);
        SlotTriggered?.Invoke(index, TestSkills.Slots[index]);
    }

    /// Pisca o slot acionado, para o jogador ver que a tecla pegou mesmo que o
    /// efeito demore a aparecer.
    private void Flash(int index)
    {
        var icon = _icons[index];
        icon.Modulate = new Color(2.0f, 2.0f, 2.0f);

        var tween = CreateTween();
        tween.TweenProperty(icon, "modulate", Colors.White, 0.25f);
    }
}
