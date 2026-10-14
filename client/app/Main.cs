using Backlands.Dev;
using Backlands.Game;
using Backlands.Model;
using Backlands.Scene;
using Backlands.Ui;
using Godot;

namespace Backlands.App;

/// <summary>
/// Raiz da cena. Monta a câmera isométrica, o mapa, as entidades e a tela de
/// conexão, e, pelo InputController, traduz clique do mouse em pedido de
/// caminhada.
///
/// A cena é construída em código de propósito: tudo que importa aqui é
/// comportamento, e um .tscn gigante escrito à mão seria mais frágil que
/// código que o compilador verifica.
///
/// Aqui só se COMPÕE: cada peça mora no seu namespace (modelo, rede, jogo,
/// cena, arte, UI, ferramentas de teste) e esta classe as cria, liga os
/// eventos e dita a ordem do quadro. A ordem é parte do comportamento — nos
/// filhos, nos handlers e no _Process —, e está comentada onde importa.
/// </summary>
public sealed partial class Main : Node3D
{
    private GameSession _session = null!;
    private MapRenderer _mapRenderer = null!;
    private CameraRig _cameraRig = null!;
    private WorldView _worldView = null!;

    private LoginPanel _loginPanel = null!;
    private IStatusLine _status = null!;
    private Hud _hud = null!;
    private SkillBar _skillBar = null!;
    private BasicInfoWindow _basicInfo = null!;
    private Minimap _minimap = null!;
    private OptionsWindow _options = null!;

    private TargetSelection _targets = null!;
    private CastController _cast = null!;
    private InputController _input = null!;
    private ScreenshotCapture _screenshot = null!;
    private AutoPilot _autoPilot = null!;

    /// <summary>
    /// Monta tudo e aplica a linha de comando.
    ///
    /// Filhos de Main, NESTA ordem: WorldEnvironment, Sun, Camera, Ui
    /// (LoginPanel, SkillBar, Hud, BasicInfo, Minimap, Options), MapRenderer,
    /// Entities, GameSession (que cria o NetworkClient). SpritePreview e os
    /// Vfx_* entram depois, quando pedidos.
    /// </summary>
    public override void _Ready()
    {
        var options = LaunchOptions.Parse(OS.GetCmdlineUserArgs());

        // Antes de qualquer mapa, entidade ou efeito existir: todos leem o
        // interruptor quando nascem, e nenhum relê depois.
        if (options.FreezeAnimations)
        {
            AnimationFreeze.Enabled = true;
        }

        BuildEnvironment();
        BuildUi();

        _mapRenderer = new MapRenderer { Name = "MapRenderer" };
        AddChild(_mapRenderer);

        // A sessão nasce antes do nó "Entities" porque ele lê o espelho do
        // mundo que ela possui; na árvore, entra depois, como sempre.
        _session = new GameSession { Name = "GameSession" };
        _worldView = new WorldView(_session.World) { Name = "Entities" };
        AddChild(_worldView);
        AddChild(_session);

        var world = _session.World;
        _targets = new TargetSelection(world, _worldView);
        _cast = new CastController(world, _worldView, _targets, _status, this);
        _screenshot = new ScreenshotCapture(options.ScreenshotPath, options.ScreenshotDelayFrames);
        _autoPilot = new AutoPilot(options.Walk, options.AttackNearest, options.CastSlot,
                                   options.OpenOptions, _session, _targets, _cast, _basicInfo,
                                   _screenshot);
        _input = new InputController(world, _session, _cameraRig, _targets, _cast,
                                     captureRun: _screenshot.Enabled);

        _loginPanel.ConnectRequested += _session.ConnectAndLogin;
        _skillBar.SlotTriggered += _cast.Trigger;
        _basicInfo.OptionsRequested += OnOptionsRequested;
        _options.HelpToggled += OnHelpToggled;
        _options.DisconnectRequested += OnDisconnectRequested;

        // Um assinante por evento: a sequência de cada reação está escrita
        // dentro do handler, e não espalhada pela ordem das assinaturas.
        world.MapReady += OnMapReady;
        world.EnteredWorld += OnEnteredWorld;
        world.EntitySpawned += OnEntitySpawned;
        world.EntityDespawned += OnEntityDespawned;
        world.EntityWalkStarted += _worldView.OnEntityWalk;
        world.EntityStopped += _worldView.OnEntityStopped;
        world.LoginRejected += OnLoginRejected;
        world.EntityAttacked += OnEntityAttacked;
        world.EntityDied += OnEntityDied;
        world.EntityRespawned += OnEntityRespawned;
        world.AttackRejected += OnAttackRejected;

        _session.Disconnected += OnDisconnected;
        _session.ConnectionFailed += OnConnectionFailed;

        ApplyCommandLine(options);
    }

    /// O que a linha de comando muda depois de tudo montado: endereço, câmera,
    /// e então UM destino — pré-visualização do mapa, da folha, ou o login
    /// automático.
    private void ApplyCommandLine(LaunchOptions options)
    {
        if (options.Server != null)
        {
            _loginPanel.HostText = options.Server;
        }
        if (options.CameraAngles is { } angles)
        {
            _cameraRig.SetAngles(angles.Yaw, angles.Pitch);
        }
        // Terceiro valor de --camera, opcional: o zoom. Passa pelo MESMO clamp
        // da roda, para a captura provar os limites reais e não um valor que o
        // jogador nunca alcança.
        if (options.CameraSize is { } size)
        {
            _cameraRig.SetSize(size);
        }

        // A câmera já foi montada em BuildEnvironment, antes de aplicar a
        // linha de comando; --camera só vale depois de reaplicar — ângulos E
        // zoom. O zoom fica fora de ApplyCameraAngles de propósito, para o modo
        // de pré-visualização do mapa poder usar o próprio Size; ele roda
        // depois daqui e sobrescreve.
        _cameraRig.Apply();

        if (options.MapPreview)
        {
            MapPreview.Enter(_mapRenderer, _loginPanel, _cameraRig, _screenshot);
            return;
        }

        if (options.SpritePreviewKey != null)
        {
            SpritePreview.Enter(this, options.SpritePreviewKey, _loginPanel, _hud, _cameraRig.Camera,
                                _screenshot);
            return;
        }

        if (options.AutoLoginName != null)
        {
            _loginPanel.NameText = options.AutoLoginName;
            GD.Print($"[teste] autologin como '{options.AutoLoginName}' em {_loginPanel.HostText}");
            _loginPanel.Submit();
        }
    }

    // --- Montagem da cena --------------------------------------------------

    /// Ambiente, sol e câmera.
    private void BuildEnvironment()
    {
        AddChild(SceneEnvironment.Create());
        _cameraRig = new CameraRig(this);
    }

    private void BuildUi()
    {
        var layer = new CanvasLayer { Name = "Ui" };
        AddChild(layer);

        _loginPanel = new LoginPanel();
        layer.AddChild(_loginPanel);
        // A linha de status, hoje, é o rótulo do painel de login.
        _status = _loginPanel;

        _skillBar = new SkillBar();
        layer.AddChild(_skillBar);

        _hud = new Hud();
        layer.AddChild(_hud);

        // As janelas do Ragnarok nascem escondidas: aparecem ao entrar no
        // mundo, e a de opções só quando a engrenagem (ou --open-options) pede.
        _basicInfo = new BasicInfoWindow();
        layer.AddChild(_basicInfo);

        _minimap = new Minimap();
        layer.AddChild(_minimap);

        _options = new OptionsWindow();
        layer.AddChild(_options);
    }

    // --- Opções ------------------------------------------------------------

    /// A engrenagem: abre com a caixa da ajuda no estado REAL do HUD, ou fecha.
    private void OnOptionsRequested()
    {
        _options.Toggle(_hud.Visible);
    }

    private void OnHelpToggled(bool visible)
    {
        _hud.Visible = visible;
    }

    /// "Desconectar" volta ao login: a sessão derruba a conexão e avisa por
    /// Disconnected, que é o mesmo caminho de uma queda de rede.
    private void OnDisconnectRequested()
    {
        _session.Disconnect();
    }

    // --- Conexão -----------------------------------------------------------

    private void OnConnectionFailed(string reason)
    {
        _loginPanel.ShowFailure(reason);
    }

    private void OnDisconnected(string reason)
    {
        // A sessão já limpou o mundo antes de avisar, então as views que
        // sobrarem aqui são liberadas de uma vez.
        _loginPanel.ShowDisconnected(reason);
        _hud.Visible = false;
        _skillBar.Visible = false;
        _basicInfo.Visible = false;
        _minimap.Visible = false;
        _minimap.Clear();
        _options.Close();
        _worldView.Clear();
    }

    private void OnLoginRejected(LoginFailure reason)
    {
        _loginPanel.ShowFailure(LoginFailureText.Describe(reason));
    }

    // --- Mundo -------------------------------------------------------------

    private void OnMapReady()
    {
        var map = _session.World.Map;
        _mapRenderer.Build(map);
        _minimap.SetMap(map);
        GD.Print($"[teste] mapa '{map.Name}' {map.Width}x{map.Height} recebido");
    }

    private void OnEnteredWorld(RemoteEntity entity)
    {
        _loginPanel.Visible = false;
        _status.StatusText = string.Empty;
        _hud.Visible = true;
        _skillBar.Visible = true;
        _basicInfo.Show(entity);
        _minimap.Visible = true;

        _worldView.Add(entity, isLocal: true);
        _cameraRig.Follow(GridSpace.CellToWorld(entity.CellX, entity.CellY));
        UpdateHud();

        GD.Print($"[teste] entrou no mundo: entidade {entity.Id} '{entity.Name}' "
            + $"em ({entity.CellX}, {entity.CellY})");

        _autoPilot.OnEnteredWorld();
    }

    private void OnEntitySpawned(RemoteEntity entity)
    {
        // A view antes do piloto automático: selecionar o alvo acende o anel
        // dela, e sem a view não haveria anel.
        _worldView.Add(entity, isLocal: entity.Id == _session.World.LocalEntityId);
        UpdateHud();

        _autoPilot.OnEntitySpawned(entity);
    }

    private void OnEntityDespawned(RemoteEntity entity)
    {
        _worldView.Remove(entity.Id);
        // Antes de mexer no alvo: Select(0) procura a view antiga para
        // apagar o anel, e ela já saiu do dicionário — não há o que apagar.
        if (entity.Id == _targets.TargetId)
        {
            _targets.Select(0);
        }
        UpdateHud();
    }

    /// A view primeiro (golpe, dano flutuante, barra sobre a cabeça) e, se o
    /// alvo é o próprio jogador, a janela básica acompanha a vida nova.
    private void OnEntityAttacked(RemoteEntity? source, RemoteEntity target, int damage,
                                  int motionMs)
    {
        _worldView.OnEntityAttacked(source, target, damage, motionMs);

        if (target.Id == _session.World.LocalEntityId)
        {
            _basicInfo.Refresh(target);
        }
    }

    private void OnEntityDied(RemoteEntity dead, RemoteEntity? killer)
    {
        _worldView.ShowDeath(dead);

        if (dead.Id == _session.World.LocalEntityId)
        {
            _basicInfo.Refresh(dead);
            _status.StatusText = "Você morreu. Renascendo...";
            _status.StatusVisible = true;
            _cast.Cancel("morreu");
        }
        if (dead.Id == _targets.TargetId)
        {
            _targets.Select(0);
        }
        GD.Print($"[teste] {dead.Name} morreu (morto por {killer?.Name ?? "?"})");
    }

    private void OnEntityRespawned(RemoteEntity entity)
    {
        _worldView.ShowRespawn(entity);

        if (entity.Id == _session.World.LocalEntityId)
        {
            _basicInfo.Refresh(entity);
            _status.StatusText = string.Empty;
            _status.StatusVisible = false;
        }
        GD.Print($"[teste] {entity.Name} renasceu em ({entity.CellX}, {entity.CellY})");
    }

    private void OnAttackRejected(AttackRejection reason)
    {
        GD.Print($"[teste] ataque recusado: {AttackRejectionText.Describe(reason)}");
    }

    private void UpdateHud()
    {
        var world = _session.World;
        var local = world.Local;

        // Mostra a célula sob a posição VISUAL, não a autoritativa: durante uma
        // caminhada a autoritativa já é o destino, e exibi-la pareceria que o
        // personagem teleportou. O minimapa recebe a mesma posição, fracionária,
        // para o ponto deslizar junto com o personagem.
        (int X, int Y)? cell = null;
        Vector2? localCellPosition = null;
        if (local != null && _worldView.TryGetView(local.Id, out var view))
        {
            cell = GridSpace.WorldToCell(view.Position);
            localCellPosition = GridSpace.WorldToCellPosition(view.Position);
        }

        var target = _targets.TargetId == 0 ? null : world.Find(_targets.TargetId)?.Name;
        _hud.Refresh(world.MapName, cell, local, world.Entities.Count, target);
        _minimap.Refresh(world, localCellPosition);
    }

    // --- Entrada -----------------------------------------------------------

    public override void _UnhandledInput(InputEvent @event)
    {
        _input.Handle(@event);
    }

    /// Um quadro, NESTA ordem: relógio do conjuro, volta da câmera, conjuro,
    /// conjuro automático, câmera seguindo o personagem e HUD, e por fim a
    /// contagem da captura de tela.
    public override void _Process(double delta)
    {
        _cast.AdvanceClock(delta);
        _cameraRig.Advance(delta);
        _cast.Advance();
        _autoPilot.AdvanceAutoCast();

        var local = _session.World.Local;
        if (local != null && _worldView.TryGetView(local.Id, out var view))
        {
            // Câmera segue a posição INTERPOLADA, não a célula, senão ela
            // andaria aos saltos de um tile.
            _cameraRig.Follow(view.Position);
            UpdateHud();
        }

        _screenshot.Advance(GetViewport());
    }
}
