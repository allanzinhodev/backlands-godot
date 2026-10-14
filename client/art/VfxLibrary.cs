using System;
using System.Collections.Generic;
using Godot;

namespace Backlands.Art;

/// <summary>
/// Um efeito visual do pacote WsT: uma tira horizontal de quadros, já fatiada
/// em <see cref="SpriteFrames"/>, com os metadados que decidem como desenhá-lo.
/// </summary>
public sealed class VfxEffect
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required SpriteFrames Frames { get; init; }

    /// Quantas unidades de mundo cada pixel da arte ocupa. O pacote desenha um
    /// tile do jogo com `tile_size` pixels, e no nosso mundo um tile é uma
    /// unidade — então o efeito fica na escala do mapa sem ajuste manual.
    public required float PixelSize { get; init; }

    /// Deslocamento em pixels que leva o pivô declarado no pacote até o ponto
    /// de ancoragem no alvo, já incluindo o levantamento (ver
    /// <see cref="VfxLibrary"/>).
    public required Vector2 Offset { get; init; }

    public required float Fps { get; init; }

    public required bool Loops { get; init; }

    /// Efeito de chão (círculos mágicos): a arte é desenhada vista de cima,
    /// então ela é deitada no plano do mundo em vez de encarar a câmera.
    public required bool OnGround { get; init; }

    public const string AnimationName = "play";

    /// Duração de uma passada completa, em segundos.
    public float Duration => Frames.GetFrameCount(AnimationName) / Math.Max(Fps, 1.0f);
}

/// <summary>
/// Carrega os efeitos de <c>res://vfx/</c>.
///
/// Mesmo caminho das folhas de personagem: lê o PNG cru e fatia em tempo de
/// execução, em vez de depender do pipeline de importação do Godot — que, no
/// padrão, recomprime textura usada em 3D e destrói pixel art.
/// </summary>
public static class VfxLibrary
{
    /// A que altura o efeito é ancorado no alvo, em tiles acima dos pés.
    ///
    /// O pacote ancora o pivô NOS PÉS, e está certo para o jogo dele: 2D
    /// oblíquo, personagem da altura de um tile, e a sobreposição resolvida por
    /// ordem de desenho (camada atrás / na frente). Aqui o personagem tem 3,2
    /// tiles e nós achatamos as camadas numa folha só, então sobrou a altura
    /// como única ferramenta — e no pé o efeito estoura nos tornozelos.
    ///
    /// Meio tile foi medido, não chutado: a 0,5 a explosão cobre o tronco e as
    /// fagulhas ainda tocam o chão; a 1,6 (meio corpo) ela passa da cabeça e
    /// descola do alvo. Cada efeito pode discordar por "lift_tiles" no JSON.
    private const float DefaultLiftTiles = 0.5f;

    private static readonly Dictionary<string, VfxEffect?> Cache = new();

    public static VfxEffect? Load(string id)
    {
        if (Cache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        VfxEffect? effect = null;
        try
        {
            effect = Build(id);
            if (effect == null)
            {
                GD.Print($"[vfx] '{id}' não encontrado em res://vfx/{id}/");
            }
        }
        catch (Exception error)
        {
            GD.PushWarning($"[vfx] '{id}' é inválido: {error.Message}");
        }

        Cache[id] = effect;
        return effect;
    }

    public static void ClearCache() => Cache.Clear();

    private static VfxEffect? Build(string id)
    {
        var folder = $"res://vfx/{id}";
        var metaPath = $"{folder}/{id}.json";
        var sheetPath = $"{folder}/{id}.png";

        if (!FileAccess.FileExists(metaPath) || !FileAccess.FileExists(sheetPath))
        {
            return null;
        }

        // FileAccess + Json.ParseString, e não File.ReadAllText("res://..."):
        // o segundo quebra depois do export, quando os arquivos vivem no .pck.
        using var metaFile = FileAccess.Open(metaPath, FileAccess.ModeFlags.Read);
        var json = new Json();
        if (json.Parse(metaFile.GetAsText()) != Error.Ok)
        {
            throw new InvalidOperationException($"json inválido: {json.GetErrorMessage()}");
        }

        var meta = json.Data.AsGodotDictionary();
        var frameWidth = (int)meta["frame_width"];
        var frameHeight = (int)meta["frame_height"];
        var frameCount = (int)meta["frames"];
        var fps = (float)meta["fps"];
        var loops = (bool)meta["loop"];
        var tileSize = (int)meta["tile_size"];
        var onGround = (bool)meta["on_ground"];

        var pivot = meta["pivot"].AsGodotArray();
        var pivotX = (float)pivot[0];
        var pivotY = (float)pivot[1];

        using var sheetFile = FileAccess.Open(sheetPath, FileAccess.ModeFlags.Read);
        var image = new Image();
        if (image.LoadPngFromBuffer(sheetFile.GetBuffer((long)sheetFile.GetLength())) != Error.Ok)
        {
            throw new InvalidOperationException("PNG inválido");
        }

        var expected = frameWidth * frameCount;
        if (image.GetWidth() < expected || image.GetHeight() < frameHeight)
        {
            throw new InvalidOperationException(
                $"folha {image.GetWidth()}x{image.GetHeight()} pequena demais para "
                + $"{frameCount} quadros de {frameWidth}x{frameHeight}");
        }

        var texture = ImageTexture.CreateFromImage(image);
        var frames = new SpriteFrames();
        frames.RemoveAnimation("default");
        frames.AddAnimation(VfxEffect.AnimationName);
        frames.SetAnimationSpeed(VfxEffect.AnimationName, fps);
        frames.SetAnimationLoopMode(
            VfxEffect.AnimationName,
            loops ? SpriteFrames.LoopMode.Linear : SpriteFrames.LoopMode.None);

        for (var i = 0; i < frameCount; i++)
        {
            frames.AddFrame(VfxEffect.AnimationName, new AtlasTexture
            {
                Atlas = texture,
                Region = new Rect2(i * frameWidth, 0, frameWidth, frameHeight),
                Margin = new Rect2(0, 0, 0, 0),
                FilterClip = false,
            });
        }

        // O círculo mágico NÃO sobe: o chão é o lugar dele. E o quadro dele é
        // deitado, então um deslocamento no Y da textura andaria para o norte
        // em vez de subir — levantar um círculo o moveria de lugar no mapa.
        var lift = onGround ? 0.0f : DefaultLiftTiles;
        if (meta.ContainsKey("lift_tiles"))
        {
            lift = (float)meta["lift_tiles"];
        }

        return new VfxEffect
        {
            Id = id,
            Title = meta.ContainsKey("title") ? (string)meta["title"] : id,
            Frames = frames,
            // Um tile do pacote vira um tile do nosso mundo.
            PixelSize = 1.0f / tileSize,
            // No Sprite3D o +Y do offset SOBE, ao contrário do espaço da imagem.
            //
            // O levantamento entra AQUI, e não na posição do nó: o quadro é
            // billboard, então seu Y local é o de cima da TELA, e subir por ele
            // acompanha o personagem — que também é billboard. Subir pela
            // posição do nó seria subir no Y do MUNDO, que a câmera inclinada
            // encurta em cos(45°): o efeito pararia abaixo do alvo.
            Offset = new Vector2(
                frameWidth / 2.0f - pivotX,
                pivotY - frameHeight / 2.0f + lift * tileSize),
            Fps = fps,
            Loops = loops,
            OnGround = onGround,
        };
    }
}
