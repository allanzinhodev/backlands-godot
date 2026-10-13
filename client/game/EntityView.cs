using System.Collections.Generic;
using Backlands.Net;
using Godot;

namespace Backlands.Game;

/// <summary>
/// O nó visual de uma entidade: um sprite 2D billboard dentro do mundo 3D.
///
/// A posição autoritativa é sempre a célula que o servidor mandou. Este nó
/// apenas interpola entre células para o movimento não ficar aos saltos —
/// ele nunca decide para onde a entidade vai.
/// </summary>
public sealed partial class EntityView : Node3D
{
    // Altura do sprite em pixels. O pivô vai nos pés com offset = altura/2,
    // e no Sprite3D o +Y do offset sobe (ao contrário do Sprite2D).
    private const int SpriteHeightPx = 32;
    private const int SpriteWidthPx = 20;

    private readonly Queue<Vector3> _waypoints = new();
    private Color _nameplateColor = Colors.White;

    private Sprite3D _sprite = null!;
    private Label3D _nameplate = null!;
    private Sprite3D _hpBarBack = null!;
    private Sprite3D _hpBarFill = null!;
    private float _speedUnitsPerSecond = 1000.0f / 150.0f;

    public uint EntityId { get; private set; }

    public EntityKind Kind { get; private set; }

    public static EntityView Create(RemoteEntity entity, bool isLocal)
    {
        var view = new EntityView
        {
            Name = $"Entity{entity.Id}",
            EntityId = entity.Id,
            Kind = entity.Kind,
            Position = MapRenderer.CellToWorld(entity.CellX, entity.CellY),
        };
        view.BuildVisual(entity, isLocal);
        view.UpdateHealth(entity);
        return view;
    }

    private void BuildVisual(RemoteEntity entity, bool isLocal)
    {
        _sprite = new Sprite3D
        {
            Name = "Sprite",
            Texture = CreatePlaceholderTexture(entity.Id, isLocal, entity.Kind),

            // FIXED_Y e não ENABLED: billboard completo inclinaria o sprite
            // para encarar a câmera inclinada, e o personagem ficaria deitado
            // para trás em vez de em pé. FIXED_Y gira só no eixo vertical,
            // que é o comportamento do Ragnarok.
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,

            // Pivô nos pés: offset.y POSITIVO sobe o sprite no 3D.
            Offset = new Vector2(0, SpriteHeightPx / 2.0f),

            // Sem isto o Godot usa linear com mipmaps e a arte fica borrada.
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,

            // alpha_cut DISCARD faz o sprite escrever profundidade e projetar
            // sombra; com o default ele não projeta sombra nenhuma.
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard,

            // 32 px de altura * 0.05 = 1.6 unidades: um pouco mais alto que
            // um tile, como um personagem de Ragnarok em relação ao chão.
            PixelSize = 0.05f,
        };
        AddChild(_sprite);

        // Barra de vida: dois sprites simples, um fundo escuro e um
        // preenchimento que encolhe. Billboard igual ao do personagem, para
        // sempre encarar a câmera.
        _hpBarBack = CreateBar(new Color(0.08f, 0.08f, 0.10f), 1.0f, 0.0f);
        _hpBarFill = CreateBar(new Color(0.85f, 0.25f, 0.25f), 0.98f, 0.001f);

        _nameplate = new Label3D
        {
            Name = "Nameplate",
            Text = entity.Name,
            Position = new Vector3(0, 2.3f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            FontSize = 48,
            PixelSize = 0.006f,
            Modulate = isLocal ? new Color(1.0f, 0.95f, 0.5f) : Colors.White,
            OutlineSize = 16,
            NoDepthTest = true,
        };
        _nameplateColor = _nameplate.Modulate;
        AddChild(_nameplate);
    }

    private Sprite3D CreateBar(Color color, float width, float zOffset)
    {
        var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.SetPixel(0, 0, color);

        var bar = new Sprite3D
        {
            Name = zOffset > 0.0f ? "HpFill" : "HpBack",
            Texture = ImageTexture.CreateFromImage(image),
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            PixelSize = 1.0f,
            Position = new Vector3(0, 1.95f, zOffset),
            Scale = new Vector3(width, 0.2f, 1.0f),
            NoDepthTest = true,
            // Sem sombra: é indicador de interface, não objeto do mundo.
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(bar);
        return bar;
    }

    /// Ajusta a barra de vida. O preenchimento encolhe a partir da esquerda,
    /// por isso o deslocamento acompanha a escala.
    public void UpdateHealth(RemoteEntity entity)
    {
        const float full = 0.98f;
        var fraction = entity.HpFraction;
        var width = full * fraction;

        _hpBarFill.Scale = new Vector3(Mathf.Max(width, 0.0001f), 0.2f, 1.0f);
        _hpBarFill.Position = new Vector3(-(full - width) / 2.0f, 1.95f, 0.001f);

        // Mostrar a barra de todo mundo o tempo todo poluiria a tela: jogador
        // com vida cheia esconde, monstro mostra sempre (é alvo).
        var show = fraction < 1.0f || Kind == EntityKind.Monster;
        _hpBarBack.Visible = show;
        _hpBarFill.Visible = show && fraction > 0.0f;
    }

    /// Entidade morta fica apagada, mas o nó continua: ela pode renascer com
    /// o mesmo id.
    public void SetDead(bool dead)
    {
        _sprite.Modulate = dead ? new Color(1, 1, 1, 0.25f) : Colors.White;
        _nameplate.Modulate = dead ? new Color(0.6f, 0.6f, 0.6f) : _nameplateColor;
        if (dead)
        {
            _hpBarBack.Visible = false;
            _hpBarFill.Visible = false;
        }
    }

    /// Texto que sobe e some: dano, "Miss" ou aviso.
    public void ShowFloatingText(string text, Color color)
    {
        var label = new Label3D
        {
            Text = text,
            Position = new Vector3(0, 1.3f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            FontSize = 64,
            PixelSize = 0.006f,
            Modulate = color,
            OutlineSize = 20,
            NoDepthTest = true,
        };
        AddChild(label);

        // Tween em vez de _Process: o nó se remove sozinho ao terminar, sem o
        // resto da cena precisar saber que ele existe.
        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(label, "position", new Vector3(0, 2.6f, 0), 0.9f);
        tween.TweenProperty(label, "modulate:a", 0.0f, 0.9f).SetDelay(0.3f);
        tween.Chain().TweenCallback(Callable.From(label.QueueFree));
    }

    /// Começa a percorrer o trajeto que o servidor enviou. A posição de partida
    /// vem do pacote, para o client se corrigir de qualquer desvio acumulado.
    public void FollowPath(WalkOrder order)
    {
        _waypoints.Clear();
        _speedUnitsPerSecond = 1000.0f / Mathf.Max(order.SpeedMsPerCell, 1);

        // Se a interpolação divergiu muito da verdade do servidor, corrige de
        // uma vez em vez de "escorregar" até o lugar certo.
        var authoritative = MapRenderer.CellToWorld(order.FromX, order.FromY);
        if (Position.DistanceTo(authoritative) > 1.5f)
        {
            Position = authoritative;
        }

        var x = order.FromX;
        var y = order.FromY;
        foreach (var dir in order.Path)
        {
            var delta = WorldState.DirectionDeltas[(int)dir];
            x += delta.X;
            y += delta.Y;
            _waypoints.Enqueue(MapRenderer.CellToWorld(x, y));
        }
    }

    /// Posição autoritativa imposta pelo servidor: descarta o trajeto pendente.
    public void SnapTo(int cellX, int cellY)
    {
        _waypoints.Clear();
        Position = MapRenderer.CellToWorld(cellX, cellY);
    }

    public override void _Process(double delta)
    {
        if (_waypoints.Count == 0)
        {
            return;
        }

        // Pode consumir mais de um waypoint num frame se o frame foi longo;
        // sem isso o movimento ficaria mais lento que o do servidor.
        var budget = _speedUnitsPerSecond * (float)delta;
        while (budget > 0.0f && _waypoints.Count > 0)
        {
            var target = _waypoints.Peek();
            var toTarget = target - Position;
            var distance = toTarget.Length();

            if (distance <= budget)
            {
                Position = target;
                budget -= distance;
                _waypoints.Dequeue();
            }
            else
            {
                Position += toTarget / distance * budget;
                budget = 0.0f;
            }
        }
    }

    /// Textura placeholder gerada em código: um bonequinho simples. Evita
    /// depender de arte antes de ela existir, e a cor derivada do id deixa
    /// dois clientes distinguíveis na mesma tela durante os testes.
    private static ImageTexture CreatePlaceholderTexture(uint entityId, bool isLocal,
                                                        EntityKind kind)
    {
        var image = Image.CreateEmpty(SpriteWidthPx, SpriteHeightPx, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 0, 0));

        // Matiz a partir do id: estável entre clientes, porque o id vem do
        // servidor. O passo é o conjugado da razão áurea, que espalha ids
        // consecutivos por matizes bem distantes — com um passo pequeno, dois
        // jogadores que entram juntos saem quase da mesma cor.
        var hue = (entityId * 0.618033988f) % 1.0f;
        var body = isLocal ? new Color(0.95f, 0.85f, 0.35f) : Color.FromHsv(hue, 0.55f, 0.85f);
        var skin = new Color(0.95f, 0.78f, 0.62f);
        var outline = new Color(0.08f, 0.07f, 0.10f);

        if (kind == EntityKind.Monster)
        {
            // Monstro: corpo baixo e arredondado, sem pernas. A silhueta
            // precisa diferir do jogador mesmo em miniatura na tela.
            var blob = Color.FromHsv(hue, 0.7f, 0.9f);
            FillRect(image, 3, 14, 14, 16, blob);
            FillRect(image, 5, 11, 10, 4, blob);
            FillRect(image, 6, 19, 2, 3, outline);  // olhos
            FillRect(image, 12, 19, 2, 3, outline);
        }
        else
        {
            // Jogador: cabeça (2..11), corpo (12..24), pernas (25..31).
            FillRect(image, 6, 2, 8, 10, skin);
            FillRect(image, 5, 12, 10, 13, body);
            FillRect(image, 6, 25, 3, 7, outline);
            FillRect(image, 11, 25, 3, 7, outline);
        }

        // Contorno simples: escurece a borda do corpo para o sprite não sumir
        // contra o chão verde. As posições são coletadas antes de escrever,
        // senão um pixel de contorno recém-escrito seria vizinho opaco do
        // próximo e o contorno engrossaria sozinho.
        var outlinePixels = new List<Vector2I>();
        for (var y = 0; y < SpriteHeightPx; y++)
        {
            for (var x = 0; x < SpriteWidthPx; x++)
            {
                if (image.GetPixel(x, y).A <= 0.0f && HasOpaqueNeighbour(image, x, y))
                {
                    outlinePixels.Add(new Vector2I(x, y));
                }
            }
        }
        foreach (var pixel in outlinePixels)
        {
            image.SetPixel(pixel.X, pixel.Y, outline);
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static void FillRect(Image image, int x, int y, int width, int height, Color color)
    {
        for (var dy = 0; dy < height; dy++)
        {
            for (var dx = 0; dx < width; dx++)
            {
                var px = x + dx;
                var py = y + dy;
                if (px >= 0 && py >= 0 && px < image.GetWidth() && py < image.GetHeight())
                {
                    image.SetPixel(px, py, color);
                }
            }
        }
    }

    private static bool HasOpaqueNeighbour(Image image, int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var px = x + dx;
                var py = y + dy;
                if (px < 0 || py < 0 || px >= image.GetWidth() || py >= image.GetHeight())
                {
                    continue;
                }
                if (image.GetPixel(px, py).A > 0.5f)
                {
                    return true;
                }
            }
        }
        return false;
    }
}
