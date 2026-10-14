using System;
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
