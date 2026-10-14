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

    /// Quanto o efeito é empurrado na direção da câmera, em unidades de mundo.
    ///
    /// Efeito e personagem nascem na MESMA origem e os dois são billboard, logo
    /// os dois quadros ficam coplanares. O personagem usa corte em alpha, que
    /// escreve profundidade no pré-passe; o efeito não escreve mas TESTA contra
    /// ela. Empate de profundidade é desempate indefinido: hoje o efeito aparece
    /// na frente, amanhã pode piscar atrás. Agora que ele é levantado até o
    /// tronco, a silhueta disputada é máxima.
    ///
    /// Em projeção ORTOGONAL, andar na direção da câmera não mexe um pixel na
    /// tela — o desempate sai de graça.
    private const float DepthBias = 0.05f;

    private float _lifetime = 1.0f;

    /// Onde o efeito foi pedido, antes do desempate de profundidade.
    private Vector3 _anchor;

    private bool _onGround;

    /// Monta o nó do efeito. Quem chama adiciona à cena; o tempo de vida só
    /// começa a contar quando ele entra na árvore.
    public static VfxPlayer Create(VfxEffect effect, Vector3 position)
    {
        var node = new VfxPlayer
        {
            Name = $"Vfx_{effect.Id}",
            Position = position,
            _anchor = position,
            _onGround = effect.OnGround,
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

    public override void _Process(double delta)
    {
        // Recalculado a cada quadro, e não assado na posição ao nascer: o
        // jogador gira a câmera, e um empurrão assado vale
        // 0,05·cos(quanto girou) — zero ao girar 90°, NEGATIVO depois disso, e
        // aí o personagem passa a abrir um buraco com forma de boneco no meio
        // da explosão. Um círculo em loop vive tempo de sobra para isso.
        if (_onGround)
        {
            return; // o chão já o segura; empurrar o descolaria do piso
        }

        var camera = GetViewport()?.GetCamera3D();
        if (camera != null)
        {
            // GlobalBasis.Z da câmera aponta para o observador: no Godot a
            // câmera olha ao longo do -Z dela.
            Position = _anchor + camera.GlobalBasis.Z * DepthBias;
        }
    }

    public override void _Ready()
    {
        var sprite = GetNode<AnimatedSprite3D>("Sprite");
        sprite.Play(VfxEffect.AnimationName);

        // Congelado, o efeito para no quadro do meio em vez do primeiro: o
        // primeiro de uma explosão é quase vazio, e uma captura dele não diz
        // nada sobre tamanho nem posição. Sem isto, o quadro fotografado
        // dependia de quanto a máquina demorou para compilar shader na
        // partida, e duas execuções nunca comparavam a mesma coisa.
        if (Game.EntityView.FreezeAnimations)
        {
            sprite.SpeedScale = 0.0f;
            sprite.Frame = sprite.SpriteFrames.GetFrameCount(VfxEffect.AnimationName) / 2;
            return;
        }

        // Prazo em vez do sinal de fim: um efeito em loop nunca emitiria o
        // sinal, e o prazo cobre os dois casos com a mesma regra.
        var timer = GetTree().CreateTimer(_lifetime);
        timer.Timeout += QueueFree;
    }
}
