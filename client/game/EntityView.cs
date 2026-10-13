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

    private Sprite3D _sprite = null!;
    private Label3D _nameplate = null!;
    private float _speedUnitsPerSecond = 1000.0f / 150.0f;

    public uint EntityId { get; private set; }

    public static EntityView Create(RemoteEntity entity, bool isLocal)
    {
        var view = new EntityView
        {
            Name = $"Entity{entity.Id}",
            EntityId = entity.Id,
            Position = MapRenderer.CellToWorld(entity.CellX, entity.CellY),
        };
        view.BuildVisual(entity, isLocal);
        return view;
    }

    private void BuildVisual(RemoteEntity entity, bool isLocal)
    {
        _sprite = new Sprite3D
        {
            Name = "Sprite",
            Texture = CreatePlaceholderTexture(entity.Id, isLocal),

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

        _nameplate = new Label3D
        {
            Name = "Nameplate",
            Text = entity.Name,
            Position = new Vector3(0, 2.0f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            FontSize = 48,
            PixelSize = 0.006f,
            Modulate = isLocal ? new Color(1.0f, 0.95f, 0.5f) : Colors.White,
            OutlineSize = 16,
            NoDepthTest = true,
        };
        AddChild(_nameplate);
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
    private static ImageTexture CreatePlaceholderTexture(uint entityId, bool isLocal)
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

        // Cabeça (linhas 2..11), corpo (12..25), pernas (26..31).
        FillRect(image, 6, 2, 8, 10, skin);
        FillRect(image, 5, 12, 10, 13, body);
        FillRect(image, 6, 25, 3, 7, outline);
        FillRect(image, 11, 25, 3, 7, outline);

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
