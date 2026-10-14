using System.Collections.Generic;
using Backlands.Art;
using Backlands.Net;
using Godot;

namespace Backlands.Game;

/// <summary>
/// O nó visual de uma entidade: um sprite 2D billboard dentro do mundo 3D.
///
/// A posição e a direção autoritativas são sempre as que o servidor mandou.
/// Este nó só interpola entre células e escolhe a pose — nunca decide para
/// onde a entidade vai nem para onde ela encara.
/// </summary>
public sealed partial class EntityView : Node3D
{
    /// Altura do personagem em unidades de mundo (1 unidade = 1 célula).
    /// Tudo que flutua sobre ele é ancorado a partir daqui, em vez de números
    /// soltos afinados para um tamanho de sprite específico.
    private const float BodyHeight = SpriteLayout.FrameHeight * SpriteLayout.PixelSize;

    /// Tempo que o estado `walk` segura antes de voltar para `idle`. Sem esta
    /// folga há tremor garantido: a perseguição só recalcula a rota depois de
    /// parar, e cada emenda de trecho daria um quadro de `idle`.
    private const double WalkHysteresisSeconds = 0.2;

    /// Alturas do que flutua sobre a entidade, em unidades de mundo na TELA
    /// acima dos pés. Derivadas da altura do corpo para não desencontrarem se o
    /// personagem mudar de escala.
    private const float HpBarHeight = BodyHeight * 1.22f;
    private const float CastBarHeight = BodyHeight * 1.34f;
    private const float NameplateHeight = BodyHeight * 1.45f;
    private const float CastLabelHeight = BodyHeight * 1.60f;
    private const float DamageHeight = BodyHeight * 0.8f;

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

    private readonly Queue<(Vector3 Position, Direction Facing)> _waypoints = new();

    private AnimatedSprite3D _sprite = null!;
    private Sprite3D _fallbackSprite = null!;
    private Label3D _nameplate = null!;
    private Sprite3D _hpBarBack = null!;
    private Sprite3D _hpBarFill = null!;
    private Sprite3D _castBarBack = null!;
    private Sprite3D _castBarFill = null!;
    private Label3D _castLabel = null!;
    private Sprite3D _targetRing = null!;
    private Color _nameplateColor = Colors.White;

    private SpriteSheet? _sheet;
    private SpriteState _state = SpriteState.Idle;
    private Direction _renderFacing = Direction.SouthEast;
    private float _speedUnitsPerSecond = 1000.0f / 150.0f;
    private int _speedMsPerCell = 150;

    // Prazos em vez do sinal AnimationFinished: o sinal chega um quadro
    // atrasado e corre com uma caminhada que comece no meio do golpe.
    private double _oneShotUntil;
    private double _walkHoldUntil;
    private double _now;
    private bool _dead;

    // Conjuro em andamento: começo e fim, para a barra saber a fração.
    private double _castStartedAt;
    private double _castUntil;

    // O que já está no sprite, para ApplyPose só mexer nele quando mudar.
    private SpriteState _appliedState;
    private SpritePose _appliedPose;
    private bool _appliedFlip;
    private bool _applied;

    public uint EntityId { get; private set; }

    public EntityKind Kind { get; private set; }

    /// Trava a animação para a captura de tela seguir comparável entre
    /// execuções do playtest.
    public static bool FreezeAnimations { get; set; }

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

        // Estado inicial vindo da vida: quem entra no meio da partida não pode
        // ver cadáver em pé.
        view._renderFacing = entity.Facing;
        view.SetDead(!entity.Alive);
        view.ApplyPose();
        return view;
    }

    private void BuildVisual(RemoteEntity entity, bool isLocal)
    {
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
        AddChild(_sprite);

        // Sem arte na pasta, cai no bonequinho gerado em código: o jogo nunca
        // deixa de rodar — e o playtest automatizado, única verificação ponta a
        // ponta do repositório, continua verde.
        _fallbackSprite = new Sprite3D
        {
            Name = "SpriteFallback",
            Texture = CreatePlaceholderTexture(entity.Id, isLocal, entity.Kind),
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
        AddChild(_fallbackSprite);

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
        AddChild(_castLabel);

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
        AddChild(_nameplate);
    }

    /// Espessura das barras, em unidades de mundo na tela.
    private const float BarThickness = 0.2f;

    private const float LabelPixelSize = 0.006f;

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
        AddChild(bar);
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
        AddChild(ring);
        return ring;
    }

    /// Liga ou desliga o anel de alvo.
    public void SetTargeted(bool targeted) => _targetRing.Visible = targeted && !_dead;

    // --- Vida e morte ------------------------------------------------------

    public void UpdateHealth(RemoteEntity entity)
    {
        const float full = 0.98f;
        var fraction = entity.HpFraction;
        var width = full * fraction;

        // Ancorado pela ESQUERDA: a barra drena para a direita, como se espera.
        // Em espaço de tela, senão o lado de onde ela drena dependeria do
        // ângulo da câmera e inverteria ao girar.
        PlaceBar(_hpBarFill, width, HpBarHeight, -(full - width) / 2.0f);

        var show = !_dead && (fraction < 1.0f || Kind == EntityKind.Monster);
        _hpBarBack.Visible = show;
        _hpBarFill.Visible = show && fraction > 0.0f;
    }

    /// Entidade morta mantém o nó, porque pode renascer com o mesmo id.
    public void SetDead(bool dead)
    {
        _dead = dead;
        _nameplate.Modulate = dead ? new Color(0.6f, 0.6f, 0.6f) : _nameplateColor;

        if (dead)
        {
            _waypoints.Clear();
            _hpBarBack.Visible = false;
            _hpBarFill.Visible = false;
            // Morrer leva junto conjuro e seleção: cadáver não conjura, e um
            // anel sobre o corpo faria o jogador insistir num alvo que já era.
            EndCast();
            _targetRing.Visible = false;
            EnterState(SpriteState.Dead);
        }
        else
        {
            _oneShotUntil = 0;
            EnterState(SpriteState.Idle);
        }

        // O sprite procedural não tem pose de morte, então ele usa a cor. Note
        // que com AlphaCut.Discard o corte é uniforme em 0,5: qualquer alpha
        // abaixo disso some por inteiro, então escurecer é a única leitura
        // possível — reduzir o alpha deixaria o corpo INVISÍVEL, não fantasma.
        _fallbackSprite.Modulate = dead ? new Color(0.45f, 0.45f, 0.5f) : Colors.White;
    }

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
        AddChild(label);

        // Sobe pelo OFFSET, não pela posição: subir no Y do mundo encolheria
        // por cos(inclinação) e, com a câmera olhando de cima, o número mal
        // sairia de dentro do personagem.
        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(label, "offset",
                            LabelOffset(CastLabelHeight, LabelPixelSize), 0.9f);
        tween.TweenProperty(label, "modulate:a", 0.0f, 0.9f).SetDelay(0.3f);
        tween.Chain().TweenCallback(Callable.From(label.QueueFree));
    }

    // --- Movimento ---------------------------------------------------------

    /// Percorre o trajeto que o servidor enviou. A direção de cada passo vem
    /// do próprio caminho, para o personagem encarar o trecho que está andando
    /// — e não o fim do trajeto, como aconteceria usando a direção
    /// autoritativa gravada no início da caminhada.
    public void FollowPath(WalkOrder order)
    {
        if (_dead)
        {
            return;
        }

        _waypoints.Clear();
        _speedMsPerCell = Mathf.Max(order.SpeedMsPerCell, 1);
        _speedUnitsPerSecond = 1000.0f / _speedMsPerCell;

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
            _waypoints.Enqueue((MapRenderer.CellToWorld(x, y), dir));
        }

        if (_waypoints.Count > 0)
        {
            _renderFacing = _waypoints.Peek().Facing;
            EnterState(SpriteState.Walk);
        }
    }

    /// Posição autoritativa imposta pelo servidor: descarta o trajeto pendente.
    public void SnapTo(int cellX, int cellY, Direction facing)
    {
        _waypoints.Clear();
        Position = MapRenderer.CellToWorld(cellX, cellY);
        _renderFacing = facing;
        ApplyPose();
    }

    // --- Animação ----------------------------------------------------------

    /// Toca o golpe uma vez, no tempo real que o servidor informou.
    public void PlayAttack(Direction facing, int motionMs)
    {
        if (_dead)
        {
            return;
        }

        _renderFacing = facing;
        _oneShotUntil = _now + System.Math.Max(motionMs, 1) / 1000.0;
        EnterState(SpriteState.Attack);
        ApplyPose();
    }

    /// Esquiva: o servidor manda dano 0 quando o golpe erra.
    public void PlayEvade(int motionMs)
    {
        // Conjurando, a esquiva é engolida: ela duraria menos que um piscar e o
        // sprite ia e voltava da pose de conjuro no meio da barra.
        if (_dead || _state == SpriteState.Attack || Casting)
        {
            return;
        }
        _oneShotUntil = _now + System.Math.Max(motionMs, 1) / 1000.0;
        EnterState(SpriteState.Evade);
    }

    /// <summary>
    /// Começa um conjuro: segura a pose de Cast e mostra a barra até o prazo.
    ///
    /// Quem decide se o conjuro completa, falha ou é cancelado é quem chamou —
    /// aqui é só a aparência. Como tudo que anima, o controle é por PRAZO e não
    /// pelo fim da animação: a pose de Cast tem um quadro só e nunca terminaria.
    /// </summary>
    public void BeginCast(Direction facing, double seconds, string skillName)
    {
        if (_dead)
        {
            return;
        }

        _castLabel.Text = skillName;
        _castLabel.Visible = true;
        _renderFacing = facing;
        _castStartedAt = _now;
        _castUntil = _now + System.Math.Max(seconds, 0.001);
        // O golpe em andamento não pode segurar a pose por cima do conjuro.
        _oneShotUntil = 0;
        EnterState(SpriteState.Cast);
        ApplyPose();
    }

    /// Encerra o conjuro, tendo ele completado ou não.
    public void EndCast()
    {
        _castUntil = 0;
        _castBarBack.Visible = false;
        _castBarFill.Visible = false;
        _castLabel.Visible = false;
    }

    public bool Casting => _castUntil > _now;

    private void UpdateCastBar()
    {
        if (!Casting)
        {
            if (_castBarBack.Visible)
            {
                EndCast();
            }
            return;
        }

        const float full = 0.98f;
        var total = _castUntil - _castStartedAt;
        var fraction = (float)Mathf.Clamp((_now - _castStartedAt) / total, 0.0, 1.0);
        var width = full * fraction;

        _castBarBack.Visible = true;
        _castBarFill.Visible = fraction > 0.0f;
        _castBarFill.Scale = new Vector3(Mathf.Max(width, 0.0001f), 0.2f, 1.0f);
        _castBarFill.Position = new Vector3(-(full - width) / 2.0f, CastBarHeight, 0.001f);
    }

    private void EnterState(SpriteState state)
    {
        _state = state;
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
    private void ApplyPose()
    {
        if (_sheet == null)
        {
            return;
        }

        // Sem câmera não há como escolher a pose, e Create() chama isto antes de
        // o nó entrar na árvore — aí GetViewport() é nulo. Sair sem aplicar é
        // seguro porque _applied segue falso e o primeiro _Process resolve.
        var camera = GetViewport()?.GetCamera3D();
        if (camera == null)
        {
            return;
        }

        // A pose é comparada pelo RESULTADO, não pelos ingredientes: a câmera
        // gira em jogo, e com o personagem parado nem o estado nem a direção
        // mudariam — a pose ficaria congelada na guinada antiga e o sprite
        // passaria a olhar para o lado errado.
        var (pose, flip) = SpriteLayout.PoseFor(_renderFacing, camera.GlobalBasis);

        if (_applied && _appliedState == _state && _appliedPose == pose && _appliedFlip == flip)
        {
            return;
        }
        _applied = true;
        _appliedState = _state;
        _appliedPose = pose;
        _appliedFlip = flip;

        var animation = SpriteLayout.AnimationName(_state, pose);

        _sprite.FlipH = flip;

        // SpeedScale por entidade, nunca SpriteFrames.SetAnimationSpeed: o
        // recurso é compartilhado entre todas as entidades do mesmo tipo, e
        // alterá-lo mudaria todo mundo na tela de uma vez.
        _sprite.SpeedScale = FreezeAnimations ? 0.0f : SpeedScaleFor(_state, animation);

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

    private float SpeedScaleFor(SpriteState state, string animation)
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
            SpriteState.Attack when _oneShotUntil > _now =>
                (float)(frames / (_oneShotUntil - _now)),
            _ => 1.0f,
        };
    }

    public override void _Process(double delta)
    {
        _now += delta;

        AdvanceMovement(delta);
        AdvanceState();
        UpdateCastBar();
        ApplyPose();
    }

    private void AdvanceMovement(double delta)
    {
        if (_waypoints.Count == 0)
        {
            return;
        }

        var budget = _speedUnitsPerSecond * (float)delta;
        while (budget > 0.0f && _waypoints.Count > 0)
        {
            var (target, facing) = _waypoints.Peek();
            var toTarget = target - Position;
            var distance = toTarget.Length();

            if (_state == SpriteState.Walk)
            {
                // Só anota o desejado; quem leva ao sprite é o ApplyPose do fim
                // do quadro, que compara com o que já está aplicado.
                _renderFacing = facing;
            }

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

    /// Prioridade: dead > attack/evade > cast > walk > idle.
    private void AdvanceState()
    {
        if (_dead)
        {
            return;
        }

        if (_state is SpriteState.Attack or SpriteState.Evade)
        {
            if (_now < _oneShotUntil)
            {
                return;
            }
            _oneShotUntil = 0;
        }

        // Acima de walk: no Ragnarok não se anda conjurando, e quem decide
        // cancelar por movimento é quem comanda o conjuro, não esta máquina.
        if (Casting)
        {
            EnterState(SpriteState.Cast);
            return;
        }

        if (_waypoints.Count > 0)
        {
            _walkHoldUntil = _now + WalkHysteresisSeconds;
            EnterState(SpriteState.Walk);
            return;
        }

        if (_now < _walkHoldUntil)
        {
            return; // segura `walk` pela folga, para a emenda de trecho não piscar
        }

        EnterState(SpriteState.Idle);
    }

    // --- Placeholder procedural -------------------------------------------

    private static ImageTexture CreatePlaceholderTexture(uint entityId, bool isLocal,
                                                        EntityKind kind)
    {
        const int width = 20;
        const int height = 32;

        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 0, 0));

        // Matiz a partir do id, com passo da razão áurea: ids consecutivos
        // saem em cores bem distantes.
        var hue = (entityId * 0.618033988f) % 1.0f;
        var body = isLocal ? new Color(0.95f, 0.85f, 0.35f) : Color.FromHsv(hue, 0.55f, 0.85f);
        var skin = new Color(0.95f, 0.78f, 0.62f);
        var outline = new Color(0.08f, 0.07f, 0.10f);

        if (kind == EntityKind.Monster)
        {
            var blob = Color.FromHsv(hue, 0.7f, 0.9f);
            FillRect(image, 3, 14, 14, 16, blob);
            FillRect(image, 5, 11, 10, 4, blob);
            FillRect(image, 6, 19, 2, 3, outline);
            FillRect(image, 12, 19, 2, 3, outline);
        }
        else
        {
            FillRect(image, 6, 2, 8, 10, skin);
            FillRect(image, 5, 12, 10, 13, body);
            FillRect(image, 6, 25, 3, 7, outline);
            FillRect(image, 11, 25, 3, 7, outline);
        }

        var outlinePixels = new List<Vector2I>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
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
