using Godot;

namespace Backlands.Art;

/// <summary>
/// Toca um efeito visual em um ponto do mundo e se remove sozinho ao terminar.
///
/// Dois posicionamentos, porque o pacote tem dois tipos de arte:
///  - impactos e auras encaram a câmera, como os personagens;
///  - círculos mágicos são desenhados vistos de cima e ficam DEITADOS no plano
///    do chão. Billboardar um círculo o deixaria em pé feito parede.
/// </summary>
public sealed partial class VfxPlayer : Node3D
{
    /// Quanto tempo um efeito em loop fica na tela antes de sumir. Enquanto
    /// não há skills de verdade, é o que impede um círculo de ficar para sempre.
    private const float LoopingSeconds = 3.0f;

    private float _lifetime = 1.0f;

    /// Monta o nó do efeito. Quem chama adiciona à cena; o tempo de vida só
    /// começa a contar quando ele entra na árvore.
    public static VfxPlayer Create(VfxEffect effect, Vector3 position)
    {
        var node = new VfxPlayer
        {
            Name = $"Vfx_{effect.Id}",
            Position = position,
            _lifetime = effect.Loops ? LoopingSeconds : effect.Duration,
        };

        var sprite = new AnimatedSprite3D
        {
            Name = "Sprite",
            SpriteFrames = effect.Frames,
            Animation = VfxEffect.AnimationName,
            PixelSize = effect.PixelSize,
            Offset = effect.Offset,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            // Transparência de verdade, sem corte duro: efeito mágico é feito
            // de brilho e bordas suaves, que um corte em 0,5 recortaria.
            AlphaCut = SpriteBase3D.AlphaCutMode.Disabled,
            // Brilha em vez de receber a luz da cena: é luz própria.
            Shaded = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Billboard = effect.OnGround
                ? BaseMaterial3D.BillboardModeEnum.Disabled
                : BaseMaterial3D.BillboardModeEnum.Enabled,
        };

        if (effect.OnGround)
        {
            // Deita o quadro no plano do chão, com o topo da arte apontando
            // para o norte do mapa.
            sprite.RotationDegrees = new Vector3(-90, 0, 0);
            // Um fio acima do solo, para não brigar com o chão pelo mesmo pixel.
            sprite.Position = new Vector3(0, 0.02f, 0);
        }

        node.AddChild(sprite);
        return node;
    }

    public override void _Ready()
    {
        var sprite = GetNode<AnimatedSprite3D>("Sprite");
        sprite.Play(VfxEffect.AnimationName);

        // Prazo em vez do sinal de fim: um efeito em loop nunca emitiria o
        // sinal, e o prazo cobre os dois casos com a mesma regra.
        var timer = GetTree().CreateTimer(_lifetime);
        timer.Timeout += QueueFree;
    }
}
