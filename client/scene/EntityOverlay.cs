using Backlands.Art;
using Backlands.Model;
using Godot;

namespace Backlands.Scene;

/// <summary>
/// Tudo que flutua sobre uma entidade ou marca o chão sob ela: barras de vida
/// e de conjuro, nome da skill, nome da entidade, anel de alvo e os números
/// de dano.
///
/// Parte do <see cref="EntityView"/>, não um nó: cria tudo como filho DIRETO
/// dele, com os mesmos nomes e na mesma ordem de sempre. Só cuida da
/// aparência; quando mostrar cada coisa é decisão da view.
/// </summary>
internal sealed class EntityOverlay
{
    /// Altura do personagem em unidades de mundo (1 unidade = 1 célula).
    /// Tudo que flutua sobre ele é ancorado a partir daqui, em vez de números
    /// soltos afinados para um tamanho de sprite específico.
    private const float BodyHeight = SpriteLayout.FrameHeight * SpriteLayout.PixelSize;

    /// Alturas do que flutua sobre a entidade, em unidades de mundo na TELA
    /// acima dos pés. Derivadas da altura do corpo para não desencontrarem se o
    /// personagem mudar de escala.
    private const float HpBarHeight = BodyHeight * 1.22f;
    private const float CastBarHeight = BodyHeight * 1.34f;
    private const float NameplateHeight = BodyHeight * 1.45f;
    private const float CastLabelHeight = BodyHeight * 1.60f;
    private const float DamageHeight = BodyHeight * 0.8f;

    /// Espessura das barras, em unidades de mundo na tela.
    private const float BarThickness = 0.2f;

    private const float LabelPixelSize = 0.006f;

    private readonly Node3D _owner;
    private readonly Sprite3D _hpBarBack;
    private readonly Sprite3D _hpBarFill;
    private readonly Sprite3D _castBarBack;
    private readonly Sprite3D _castBarFill;
    private readonly Label3D _castLabel;
    private readonly Sprite3D _targetRing;
    private readonly Label3D _nameplate;
    private readonly Color _nameplateColor = Colors.White;

    public EntityOverlay(Node3D owner, RemoteEntity entity, bool isLocal)
    {
        _owner = owner;

        _hpBarBack = CreateBar(new Color(0.08f, 0.08f, 0.10f), 1.0f, 0, HpBarHeight);
        _hpBarFill = CreateBar(new Color(0.85f, 0.25f, 0.25f), 0.98f, 1, HpBarHeight);

        // Acima da barra de vida, e escondida até alguém conjurar. Preta
        // enchendo de verde: é assim que a barra de conjuro do Ragnarok se
        // parece, e é metade do reconhecimento visual.
        _castBarBack = CreateBar(new Color(0.05f, 0.05f, 0.06f), 1.0f, 0, CastBarHeight);
        _castBarFill = CreateBar(new Color(0.35f, 0.9f, 0.35f), 0.98f, 1, CastBarHeight);
        _castBarBack.Visible = false;
        _castBarFill.Visible = false;

        // O nome da skill acima da cabeça, como no Ragnarok.
        _castLabel = new Label3D
        {
            Name = "CastLabel",
            Offset = LabelOffset(CastLabelHeight, LabelPixelSize),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 40,
            PixelSize = LabelPixelSize,
            Modulate = new Color(0.75f, 1.0f, 0.75f),
            OutlineSize = 16,
            NoDepthTest = true,
            Visible = false,
        };
        owner.AddChild(_castLabel);

        _targetRing = CreateTargetRing();

        _nameplate = new Label3D
        {
            Name = "Nameplate",
            Text = entity.Name,
            Offset = LabelOffset(NameplateHeight, LabelPixelSize),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 48,
            PixelSize = LabelPixelSize,
            Modulate = isLocal ? new Color(1.0f, 0.95f, 0.5f) : Colors.White,
            OutlineSize = 16,
            NoDepthTest = true,
        };
        _nameplateColor = _nameplate.Modulate;
        owner.AddChild(_nameplate);
    }

    /// Converte uma altura em unidades de mundo no Offset de um Label3D.
    ///
    /// Mesma razão das barras: o Offset vive no plano do billboard, ou seja na
    /// TELA, enquanto a posição do nó vive no mundo e encolhe por
    /// cos(inclinação) — pendurado por posição, o nome desce até o quadril
    /// quando a câmera olha de cima.
    private static Vector2 LabelOffset(float height, float pixelSize) =>
        new(0, height / pixelSize);

    /// <summary>
    /// Uma barra flutuante acima da entidade.
    ///
    /// Nada de deslocamento no MUNDO. Tanto a altura quanto a camada ficavam em
    /// mundo e as duas quebravam ao girar a câmera: um Z de 0,001 separando
    /// preenchimento e fundo projeta +0,5 na guinada padrão mas -0,5 a 135°, e
    /// a partir daí o preenchimento ordenava ATRÁS do fundo preto — a barra
    /// ficava toda preta em metade do giro. A altura em Y do mundo encolhe por
    /// cos(inclinação), então olhando de cima o nome caía sobre o quadril.
    ///
    /// A camada agora sai de RenderPriority, que o Godot resolve entre
    /// transparentes sem depender da geometria, e a posição sai do Offset do
    /// sprite — que, sob billboard, vive no plano da TELA.
    /// </summary>
    private Sprite3D CreateBar(Color color, float width, int layer, float height)
    {
        var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.SetPixel(0, 0, color);

        var bar = new Sprite3D
        {
            Name = layer > 0 ? "BarFill" : "BarBack",
            Texture = ImageTexture.CreateFromImage(image),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            PixelSize = 1.0f,
            RenderPriority = layer,
            NoDepthTest = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _owner.AddChild(bar);
        PlaceBar(bar, width, height, 0.0f);
        return bar;
    }

    /// <summary>
    /// Coloca uma barra em espaço de TELA: largura, altura acima dos pés e
    /// centro horizontal.
    ///
    /// O Offset do Sprite3D é multiplicado pelo tamanho do quadro e pela escala
    /// do nó — e a escala aqui É o tamanho da barra. Dividir por ela cancela
    /// isso e deixa os números em unidades de mundo, que é como o resto do
    /// arquivo mede.
    /// </summary>
    private static void PlaceBar(Sprite3D bar, float width, float height, float centerX)
    {
        var safeWidth = Mathf.Max(width, 0.0001f);
        bar.Scale = new Vector3(safeWidth, BarThickness, 1.0f);
        bar.Offset = new Vector2(centerX / safeWidth, height / BarThickness);
    }

    /// <summary>
    /// Anel no chão que marca a entidade selecionada.
    ///
    /// Deitado no plano do piso, e não billboard: um anel billboard viraria uma
    /// argola em pé na frente do personagem. Girar a câmera não o afeta, porque
    /// ele vive no chão como o resto do mapa.
    /// </summary>
    private Sprite3D CreateTargetRing()
    {
        const int size = 64;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 0, 0));

        var center = (size - 1) / 2.0f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var distance = new Vector2(x - center, y - center).Length() / center;
                // Anel fino na borda: cheio demais esconderia o chão e brigaria
                // com os efeitos de área, que também são desenhados no piso.
                if (distance is > 0.80f and < 0.98f)
                {
                    image.SetPixel(x, y, new Color(1.0f, 0.85f, 0.25f, 0.9f));
                }
            }
        }

        var ring = new Sprite3D
        {
            Name = "TargetRing",
            Texture = ImageTexture.CreateFromImage(image),
            Billboard = BaseMaterial3D.BillboardModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Shaded = false,
            PixelSize = 1.4f / size,
            RotationDegrees = new Vector3(-90, 0, 0),
            // Um fio acima do piso, para não brigar com ele pelo mesmo pixel.
            Position = new Vector3(0, 0.02f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        _owner.AddChild(ring);
        return ring;
    }

    public void SetRingVisible(bool visible) => _targetRing.Visible = visible;

    // --- Vida ----------------------------------------------------------------

    public void UpdateHealth(float fraction, bool dead, EntityKind kind)
    {
        const float full = 0.98f;
        var width = full * fraction;

        // Ancorado pela ESQUERDA: a barra drena para a direita, como se espera.
        // Em espaço de tela, senão o lado de onde ela drena dependeria do
        // ângulo da câmera e inverteria ao girar.
        PlaceBar(_hpBarFill, width, HpBarHeight, -(full - width) / 2.0f);

        var show = !dead && (fraction < 1.0f || kind == EntityKind.Monster);
        _hpBarBack.Visible = show;
        _hpBarFill.Visible = show && fraction > 0.0f;
    }

    public void HideHealth()
    {
        _hpBarBack.Visible = false;
        _hpBarFill.Visible = false;
    }

    /// Nome acinzentado no cadáver; vivo, volta à cor de nascença.
    public void SetNameplateDead(bool dead)
    {
        _nameplate.Modulate = dead ? new Color(0.6f, 0.6f, 0.6f) : _nameplateColor;
    }

    // --- Conjuro -------------------------------------------------------------

    public void ShowCastLabel(string skillName)
    {
        _castLabel.Text = skillName;
        _castLabel.Visible = true;
    }

    public bool CastBarVisible => _castBarBack.Visible;

    public void HideCast()
    {
        _castBarBack.Visible = false;
        _castBarFill.Visible = false;
        _castLabel.Visible = false;
    }

    /// Enche a barra de conjuro até a fração dada.
    public void ShowCastProgress(float fraction)
    {
        const float full = 0.98f;
        var width = full * fraction;

        _castBarBack.Visible = true;
        _castBarFill.Visible = fraction > 0.0f;
        _castBarFill.Scale = new Vector3(Mathf.Max(width, 0.0001f), 0.2f, 1.0f);
        _castBarFill.Position = new Vector3(-(full - width) / 2.0f, CastBarHeight, 0.001f);
    }

    // --- Dano ----------------------------------------------------------------

    public void ShowFloatingText(string text, Color color)
    {
        var label = new Label3D
        {
            Text = text,
            Offset = LabelOffset(DamageHeight, LabelPixelSize),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 64,
            PixelSize = LabelPixelSize,
            Modulate = color,
            OutlineSize = 20,
            NoDepthTest = true,
        };
        _owner.AddChild(label);

        // Sobe pelo OFFSET, não pela posição: subir no Y do mundo encolheria
        // por cos(inclinação) e, com a câmera olhando de cima, o número mal
        // sairia de dentro do personagem.
        var tween = _owner.CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(label, "offset",
                            LabelOffset(CastLabelHeight, LabelPixelSize), 0.9f);
        tween.TweenProperty(label, "modulate:a", 0.0f, 0.9f).SetDelay(0.3f);
        tween.Chain().TweenCallback(Callable.From(label.QueueFree));
    }
}
