using System;
using Backlands.Art;
using Backlands.Model;
using Godot;

namespace Backlands.Ui;

/// <summary>
/// A "janela básica" do Ragnarok: retrato, nome e as barras de HP e SP do
/// próprio jogador, com a engrenagem das opções no canto.
///
/// Só mostra o que o servidor mandou: nome, folha, HP e SP vêm da entidade
/// local, e o que ainda não existe (nível, zeny) não aparece. Quem decide
/// QUANDO atualizar é a raiz da cena, nos eventos do mundo que tocam a
/// entidade local — aqui só se desenha.
///
/// Os filhos nascem no <see cref="_Ready"/>, como no painel de login: antes
/// de entrar na árvore não há onde escrever.
/// </summary>
public sealed partial class BasicInfoWindow : PanelContainer
{
    private const int ScreenMargin = 8;
    private const int Width = 236;
    private const int Padding = 6;
    private const int BarHeight = 14;
    private const int GearSize = 20;

    /// O retrato na tela, derivado do recorte e da ampliação de SpriteLayout:
    /// se o recorte mudar lá, o TextureRect acompanha e cada pixel da arte
    /// continua inteiro, em vez de ficar com o tamanho antigo e desalinhar.
    private static readonly Vector2 PortraitPixels =
        SpriteLayout.PortraitCrop.Size * SpriteLayout.PortraitScale;

    private TextureRect _portrait = null!;
    private Label _name = null!;
    private ProgressBar _hpBar = null!;
    private Label _hpText = null!;
    private ProgressBar _spBar = null!;
    private Label _spText = null!;

    // As duas caras da barra de HP, criadas uma vez: trocar o stylebox no
    // refresh é mais barato que recriá-lo a cada golpe.
    private readonly StyleBoxFlat _hpFill = UiStyle.Bar(UiStyle.HpFill);
    private readonly StyleBoxFlat _hpLowFill = UiStyle.Bar(UiStyle.HpLowFill);

    /// O jogador clicou na engrenagem.
    public event Action? OptionsRequested;

    public BasicInfoWindow()
    {
        Name = "BasicInfo";
        Visible = false;
        Position = new Vector2(ScreenMargin, ScreenMargin);
        CustomMinimumSize = new Vector2(Width, 0);
        AddThemeStyleboxOverride("panel", UiStyle.Panel());
    }

    public override void _Ready()
    {
        var margin = new MarginContainer { Name = "Margin" };
        margin.AddThemeConstantOverride("margin_left", Padding);
        margin.AddThemeConstantOverride("margin_right", Padding);
        margin.AddThemeConstantOverride("margin_top", Padding);
        margin.AddThemeConstantOverride("margin_bottom", Padding);
        AddChild(margin);

        var row = new HBoxContainer { Name = "Row" };
        row.AddThemeConstantOverride("separation", 8);
        margin.AddChild(row);

        row.AddChild(BuildPortraitFrame());

        var column = new VBoxContainer
        {
            Name = "Column",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", 3);
        row.AddChild(column);

        _name = new Label
        {
            Name = "Name",
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        UiStyle.StyleLabel(_name, UiStyle.FontTitle, UiStyle.LocalName, outline: 2);
        column.AddChild(_name);

        (_hpBar, _hpText) = BuildBar("Hp", _hpFill);
        column.AddChild(_hpBar);
        (_spBar, _spText) = BuildBar("Sp", UiStyle.Bar(UiStyle.SpFill));
        column.AddChild(_spBar);

        // A engrenagem no canto inferior direito, como os ícones da janela
        // básica do Ragnarok. Numa linha própria, alinhada à direita, para não
        // cair em cima da barra de SP.
        var footer = new HBoxContainer
        {
            Name = "Footer",
            Alignment = BoxContainer.AlignmentMode.End,
        };
        column.AddChild(footer);
        footer.AddChild(BuildGearButton());
    }

    /// A moldura do retrato: o mesmo painel, com o quadro ampliado sem
    /// suavização dentro. Sem folha, fica a moldura escura vazia.
    private Control BuildPortraitFrame()
    {
        var frame = new PanelContainer
        {
            Name = "PortraitFrame",
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
        };
        frame.AddThemeStyleboxOverride("panel", UiStyle.Panel());

        _portrait = new TextureRect
        {
            Name = "Portrait",
            CustomMinimumSize = PortraitPixels,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        frame.AddChild(_portrait);
        return frame;
    }

    /// Uma barra com o texto centralizado por cima.
    private static (ProgressBar Bar, Label Text) BuildBar(string name, StyleBoxFlat fill)
    {
        var bar = new ProgressBar
        {
            Name = $"{name}Bar",
            CustomMinimumSize = new Vector2(0, BarHeight),
            ShowPercentage = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        bar.AddThemeStyleboxOverride("background", UiStyle.Bar(UiStyle.BarBackground));
        bar.AddThemeStyleboxOverride("fill", fill);

        var text = new Label
        {
            Name = $"{name}Text",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        UiStyle.StyleLabel(text, UiStyle.FontSmall, Colors.White, outline: 3);
        text.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        bar.AddChild(text);

        return (bar, text);
    }

    private Button BuildGearButton()
    {
        var button = new Button
        {
            Name = "Options",
            CustomMinimumSize = new Vector2(GearSize, GearSize),
            TooltipText = "Opções",
            Icon = GearTexture.Create(GearSize * 2, UiStyle.Icon),
            ExpandIcon = true,
            IconAlignment = HorizontalAlignment.Center,
        };
        UiStyle.StyleButton(button);
        button.Pressed += RequestOptions;
        return button;
    }

    /// Ao entrar no mundo: retrato e nome, que não mudam depois, e as barras.
    public void Show(RemoteEntity local)
    {
        _portrait.Texture = SpriteSheet.Load(local.Sprite)?.Portrait;
        _name.Text = local.Name;
        Refresh(local);
        Visible = true;
    }

    /// As barras, quando a vida muda, ao morrer e ao renascer.
    public void Refresh(RemoteEntity local)
    {
        Fill(_hpBar, _hpText, "HP", local.Hp, local.MaxHp);
        Fill(_spBar, _spText, "SP", local.Sp, local.MaxSp);

        // Abaixo de um quarto a barra fica vermelha, como o Ragnarok avisa.
        _hpBar.AddThemeStyleboxOverride(
            "fill", local.HpFraction < UiStyle.HpLowFraction ? _hpLowFill : _hpFill);
    }

    private static void Fill(ProgressBar bar, Label text, string label, int value, int max)
    {
        // Máximo zero nunca deveria vir do servidor, mas uma barra de 0/0
        // não pode travar a UI: fica vazia.
        bar.MaxValue = Math.Max(max, 1);
        bar.Value = Math.Clamp(value, 0, Math.Max(max, 1));
        text.Text = $"{label} {value} / {max}";
    }

    /// O mesmo que clicar na engrenagem. O piloto automático dos testes passa
    /// por aqui para provar a ligação real entre o botão e a janela.
    public void RequestOptions() => OptionsRequested?.Invoke();
}
