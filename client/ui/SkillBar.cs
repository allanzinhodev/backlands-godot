using System;
using System.Collections.Generic;
using Godot;

namespace Backlands.Ui;

/// Uma habilidade da barra de testes: o que ela dispara e como aparece.
public readonly record struct SkillSlot(
    string VfxId,
    string Label,
    int IconRow,
    int IconColumn);

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
    /// A folha de ícones tem 14 colunas por 10 linhas. O tamanho da célula não
    /// é inteiro (1536/14), então a região de cada ícone é calculada por
    /// arredondamento das bordas, em vez de multiplicar um tamanho fixo — o
    /// que acumularia erro e cortaria os ícones da direita.
    private const string IconSheetPath = "res://icons/spell_icons.png";
    private const int IconColumns = 14;
    private const int IconRows = 10;

    private const int SlotSize = 48;
    private const int SlotGap = 4;

    private readonly List<TextureRect> _icons = new();
    private readonly List<Panel> _slots = new();

    private Texture2D? _sheet;

    /// Disparado quando o jogador aciona um slot (tecla ou clique).
    public event Action<int, SkillSlot>? SlotTriggered;

    /// Os 12 slots de teste. Cada um aponta para um efeito em res://vfx/ e
    /// para um ícone da folha.
    public static readonly SkillSlot[] Slots =
    {
        new("impact_fire_explosion", "Explosão de Fogo", 1, 3),
        new("impact_frost_nova", "Nova de Gelo", 1, 11),
        new("impact_lightning_strike", "Raio", 1, 7),
        new("impact_holy_nova", "Nova Sagrada", 6, 6),
        new("impact_healing_burst", "Cura", 2, 10),
        new("impact_shadow_burst", "Explosão Sombria", 3, 1),
        new("impact_meteor", "Meteoro", 1, 2),
        new("impact_earthquake", "Terremoto", 7, 8),
        new("impact_blade_slash", "Corte", 3, 10),
        new("impact_tornado", "Tornado", 2, 4),
        new("circle_inferno_ring", "Anel Infernal", 7, 3),
        new("circle_healing_spring", "Fonte de Cura", 9, 1),
    };

    public override void _Ready()
    {
        // Cobre a tela inteira e deixa o mouse passar: só os slots capturam
        // clique, para a barra não roubar o clique de andar no cenário.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _sheet = LoadIconSheet();

        var row = new HBoxContainer
        {
            Name = "Slots",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        row.AddThemeConstantOverride("separation", SlotGap);
        AddChild(row);

        for (var i = 0; i < Slots.Length; i++)
        {
            row.AddChild(BuildSlot(i, Slots[i]));
        }

        // Ancorada no centro da borda inferior, por âncoras explícitas: um
        // preset de layout sozinho não centraliza um container de largura
        // própria, e a fileira acabaria fora da tela.
        var width = Slots.Length * SlotSize + (Slots.Length - 1) * SlotGap;
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

    private Control BuildSlot(int index, SkillSlot slot)
    {
        var panel = new Panel
        {
            Name = $"Slot{index + 1}",
            CustomMinimumSize = new Vector2(SlotSize, SlotSize),
            TooltipText = $"F{index + 1} — {slot.Label}",
            MouseFilter = MouseFilterEnum.Stop,
        };

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.07f, 0.10f, 0.85f),
            BorderColor = new Color(0.45f, 0.42f, 0.35f),
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
        };
        style.SetBorderWidthAll(1);
        panel.AddThemeStyleboxOverride("panel", style);

        var icon = new TextureRect
        {
            Name = "Icon",
            Texture = IconFor(slot),
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
        key.AddThemeFontSizeOverride("font_size", 11);
        key.AddThemeColorOverride("font_color", new Color(0.95f, 0.92f, 0.75f));
        key.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        key.AddThemeConstantOverride("outline_size", 4);
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

    private Texture2D? IconFor(SkillSlot slot)
    {
        if (_sheet == null)
        {
            return null;
        }

        var width = (float)_sheet.GetWidth() / IconColumns;
        var height = (float)_sheet.GetHeight() / IconRows;

        var column = Math.Clamp(slot.IconColumn - 1, 0, IconColumns - 1);
        var row = Math.Clamp(slot.IconRow - 1, 0, IconRows - 1);

        var left = Mathf.RoundToInt(column * width);
        var top = Mathf.RoundToInt(row * height);
        var right = Mathf.RoundToInt((column + 1) * width);
        var bottom = Mathf.RoundToInt((row + 1) * height);

        return new AtlasTexture
        {
            Atlas = _sheet,
            Region = new Rect2(left, top, right - left, bottom - top),
            Margin = new Rect2(0, 0, 0, 0),
        };
    }

    private static Texture2D? LoadIconSheet()
    {
        if (!FileAccess.FileExists(IconSheetPath))
        {
            GD.Print($"[skills] folha de ícones ausente em {IconSheetPath}");
            return null;
        }

        using var file = FileAccess.Open(IconSheetPath, FileAccess.ModeFlags.Read);
        var image = new Image();
        if (image.LoadPngFromBuffer(file.GetBuffer((long)file.GetLength())) != Error.Ok)
        {
            GD.PushWarning("[skills] folha de ícones não é um PNG válido");
            return null;
        }
        return ImageTexture.CreateFromImage(image);
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
        if (index < 0 || index >= Slots.Length)
        {
            return;
        }

        Flash(index);
        SlotTriggered?.Invoke(index, Slots[index]);
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
