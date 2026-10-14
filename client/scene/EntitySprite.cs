using Backlands.Art;
using Backlands.Model;
using Godot;

namespace Backlands.Scene;

/// <summary>
/// O corpo desenhado de uma entidade: o sprite da folha, ou o bonequinho
/// procedural quando a arte falta.
///
/// Parte do <see cref="EntityView"/>, não um nó: os dois sprites são filhos
/// DIRETOS dele, e esta classe só guarda o que já foi aplicado para mexer no
/// sprite apenas quando a pose muda. Quem decide estado e direção é a view.
/// </summary>
internal sealed class EntitySprite
{
    /// Quanto tempo cada quadro da caminhada fica na tela, em milissegundos.
    ///
    /// Fixo, e não mais casado com a velocidade de andar. O ritmo casado dava
    /// 75 ms por quadro no jogador (150 ms/célula), 140 no lobo e 200 no slime
    /// — cada um com uma cadência, e todas corridas demais para duas poses.
    ///
    /// O preço de fixar é o pé patinar: com 2 quadros de 300 ms o ciclo leva
    /// 600 ms, tempo em que o jogador atravessa 4 células, então a passada não
    /// acompanha mais a distância percorrida.
    private const float WalkFrameMs = 300.0f;

    private readonly Node3D _owner;
    private readonly SpriteSheet? _sheet;
    private readonly AnimatedSprite3D _sprite;
    private readonly Sprite3D _fallbackSprite;

    // O que já está no sprite, para ApplyPose só mexer nele quando mudar.
    private SpriteState _appliedState;
    private SpritePose _appliedPose;
    private bool _appliedFlip;
    private bool _applied;

    /// Cria "Sprite" e "SpriteFallback", nesta ordem, como filhos de
    /// <paramref name="owner"/>.
    public EntitySprite(Node3D owner, RemoteEntity entity, bool isLocal)
    {
        _owner = owner;
        _sheet = SpriteSheet.Load(entity.Sprite);

        _sprite = new AnimatedSprite3D
        {
            Name = "Sprite",
            // ENABLED e não FIXED_Y: o billboard completo substitui a base do
            // nó pela da câmera, então o quadro é desenhado inteiro, de frente.
            // FIXED_Y manteria o quadro vertical no MUNDO, e a câmera a -45° o
            // achataria em cos(45°) — foi o bug do personagem amassado.
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            // Pivô nos pés: offset.y POSITIVO sobe o sprite.
            //
            // Sob billboard o Y do offset é o de cima da TELA, não o do mundo:
            // os pés caem na origem do nó em projeção. É por isso que levantar
            // um efeito pelo offset dele acompanha o personagem em qualquer
            // ângulo de câmera, e levantar pela posição do nó não acompanharia.
            Offset = new Vector2(0, SpriteLayout.FrameHeight / 2.0f),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
            PixelSize = SpriteLayout.PixelSize,
            // O quadro é billboard para a CÂMERA, não para a luz, então a
            // sombra que ele projetaria é geometricamente sem sentido e treme
            // conforme a câmera gira.
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = _sheet != null,
        };
        if (_sheet != null)
        {
            _sprite.SpriteFrames = _sheet.Frames;
        }
        owner.AddChild(_sprite);

        // Sem arte na pasta, cai no bonequinho gerado em código: o jogo nunca
        // deixa de rodar — e o playtest automatizado, única verificação ponta a
        // ponta do repositório, continua verde.
        _fallbackSprite = new Sprite3D
        {
            Name = "SpriteFallback",
            Texture = PlaceholderTexture.Create(entity.Id, isLocal, entity.Kind),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Offset = new Vector2(0, 16),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
            // Mesma altura de mundo do sprite real, para o placeholder nao
            // mudar de tamanho quando a arte entrar. A textura tem 32 px.
            PixelSize = SpriteLayout.BodyHeightUnits / 32.0f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = _sheet == null,
        };
        owner.AddChild(_fallbackSprite);
    }

    /// Escurece o bonequinho no cadáver; o sprite da folha já tem a pose Dead.
    public void SetDead(bool dead)
    {
        // O sprite procedural não tem pose de morte, então ele usa a cor. Note
        // que com AlphaCut.Discard o corte é uniforme em 0,5: qualquer alpha
        // abaixo disso some por inteiro, então escurecer é a única leitura
        // possível — reduzir o alpha deixaria o corpo INVISÍVEL, não fantasma.
        _fallbackSprite.Modulate = dead ? new Color(0.45f, 0.45f, 0.5f) : Colors.White;
    }

    /// <summary>
    /// Leva o sprite para o par (estado, direção) desejado.
    ///
    /// Chamada todo quadro e comparando o DESEJADO com o APLICADO, em vez de
    /// depender de cada ponto de mudança lembrar de chamá-la. A versão
    /// anterior só aplicava quando o ESTADO mudava, então começar uma nova
    /// caminhada enquanto já se andava mantinha a pose e o espelhamento
    /// antigos — virar para o oeste no meio do caminho não refletia o sprite.
    /// </summary>
    public void ApplyPose(SpriteState state, Direction facing, double oneShotUntil, double now)
    {
        if (_sheet == null)
        {
            return;
        }

        // Sem câmera não há como escolher a pose, e Create() chama isto antes de
        // o nó entrar na árvore — aí GetViewport() é nulo. Sair sem aplicar é
        // seguro porque _applied segue falso e o primeiro _Process resolve.
        // (Create() e _Process são os da EntityView, dona deste sprite.)
        var camera = _owner.GetViewport()?.GetCamera3D();
        if (camera == null)
        {
            return;
        }

        // A pose é comparada pelo RESULTADO, não pelos ingredientes: a câmera
        // gira em jogo, e com o personagem parado nem o estado nem a direção
        // mudariam — a pose ficaria congelada na guinada antiga e o sprite
        // passaria a olhar para o lado errado.
        var (pose, flip) = SpriteLayout.PoseFor(facing, camera.GlobalBasis);

        if (_applied && _appliedState == state && _appliedPose == pose && _appliedFlip == flip)
        {
            return;
        }
        _applied = true;
        _appliedState = state;
        _appliedPose = pose;
        _appliedFlip = flip;

        var animation = SpriteLayout.AnimationName(state, pose);

        _sprite.FlipH = flip;

        // SpeedScale por entidade, nunca SpriteFrames.SetAnimationSpeed: o
        // recurso é compartilhado entre todas as entidades do mesmo tipo, e
        // alterá-lo mudaria todo mundo na tela de uma vez.
        _sprite.SpeedScale = AnimationFreeze.Enabled
            ? 0.0f
            : SpeedScaleFor(state, animation, oneShotUntil, now);

        if (_sprite.Animation != animation)
        {
            _sprite.Animation = animation;
            // Play() não reinicia uma animação já terminada a menos que o
            // quadro esteja no fim; forçar o quadro faz cada golpe aparecer em
            // vez de golpes seguidos se fundirem em um só.
            _sprite.SetFrameAndProgress(0, 0.0f);
        }

        _sprite.Play(animation);
    }

    private float SpeedScaleFor(SpriteState state, string animation, double oneShotUntil, double now)
    {
        var frames = _sheet!.Frames.GetFrameCount(animation);
        if (frames <= 1)
        {
            return 1.0f;
        }

        return state switch
        {
            // Ritmo fixo: a velocidade base da folha é 1, então a escala é o
            // próprio valor em quadros por segundo.
            SpriteState.Walk => 1000.0f / WalkFrameMs,
            // Os quadros do golpe cabem na duração que o servidor informou.
            SpriteState.Attack when oneShotUntil > now =>
                (float)(frames / (oneShotUntil - now)),
            _ => 1.0f,
        };
    }
}
