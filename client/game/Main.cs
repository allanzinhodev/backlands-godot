using System.Collections.Generic;
using Backlands.Net;
using Godot;

namespace Backlands.Game;

/// <summary>
/// Raiz da cena. Monta a câmera isométrica, o mapa, as entidades e a tela de
/// conexão, e traduz clique do mouse em pedido de caminhada.
///
/// A cena é construída em código de propósito: tudo que importa aqui é
/// comportamento, e um .tscn gigante escrito à mão seria mais frágil que
/// código que o compilador verifica.
/// </summary>
public sealed partial class Main : Node3D
{
    // Ângulos do Ragnarok: 45 graus de guinada e 45 de inclinação.
    private static readonly Vector3 CameraAngles = new(-45.0f, 45.0f, 0.0f);

    // `size` de uma câmera ortogonal é a extensão VERTICAL em unidades, e
    // 1 unidade = 1 tile. 15 mostra 15 tiles de altura de tela — o chão,
    // encurtado pela inclinação de 45 graus, aparece em ~21 tiles.
    private const float CameraSize = 15.0f;

    private readonly Dictionary<uint, EntityView> _views = new();

    private uint _targetId;
    private GameSession _session = null!;
    private MapRenderer _mapRenderer = null!;
    private Camera3D _camera = null!;
    private Node3D _entityRoot = null!;

    private LineEdit _hostField = null!;
    private LineEdit _nameField = null!;
    private Button _connectButton = null!;
    private Label _statusLabel = null!;
    private Control _loginPanel = null!;
    private Label _hudLabel = null!;

    // --- Modo de teste automatizado ---------------------------------------
    // Ligado por argumentos depois de `--` na linha de comando, para que o
    // client real (e não um mock) possa ser exercitado sem alguém clicando:
    //   --autologin=Nome       entra assim que a cena carrega
    //   --server=host:porta    sobrescreve o endereço
    //   --walk=x,y             pede uma caminhada após entrar no mundo
    //   --screenshot=caminho   salva um PNG da tela e encerra
    //   --screenshot-delay=N   frames a esperar antes da captura
    //   --attack-nearest       ataca o monstro mais próximo assim que o vir
    private string? _autoLoginName;
    private (int X, int Y)? _autoWalk;
    private string? _screenshotPath;
    private bool _autoAttackNearest;
    private int _screenshotDelayFrames = 180;
    private int _framesUntilScreenshot = -1;

    public override void _Ready()
    {
        BuildEnvironment();
        BuildUi();

        _mapRenderer = new MapRenderer { Name = "MapRenderer" };
        AddChild(_mapRenderer);

        _entityRoot = new Node3D { Name = "Entities" };
        AddChild(_entityRoot);

        _session = new GameSession { Name = "GameSession" };
        AddChild(_session);

        var world = _session.World;
        world.MapReady += OnMapReady;
        world.EnteredWorld += OnEnteredWorld;
        world.EntitySpawned += OnEntitySpawned;
        world.EntityDespawned += OnEntityDespawned;
        world.EntityWalkStarted += OnEntityWalk;
        world.EntityStopped += OnEntityStopped;
        world.LoginRejected += OnLoginRejected;
        world.EntityAttacked += OnEntityAttacked;
        world.EntityDied += OnEntityDied;
        world.EntityRespawned += OnEntityRespawned;
        world.AttackRejected += OnAttackRejected;

        _session.Disconnected += OnDisconnected;
        _session.ConnectionFailed += OnConnectionFailed;

        ApplyCommandLine();
    }

    private void ApplyCommandLine()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--autologin=", System.StringComparison.Ordinal))
            {
                _autoLoginName = arg["--autologin=".Length..];
            }
            else if (arg.StartsWith("--server=", System.StringComparison.Ordinal))
            {
                _hostField.Text = arg["--server=".Length..];
            }
            else if (arg.StartsWith("--walk=", System.StringComparison.Ordinal))
            {
                var parts = arg["--walk=".Length..].Split(',');
                if (parts.Length == 2 && int.TryParse(parts[0], out var x)
                    && int.TryParse(parts[1], out var y))
                {
                    _autoWalk = (x, y);
                }
            }
            else if (arg.StartsWith("--screenshot=", System.StringComparison.Ordinal))
            {
                _screenshotPath = arg["--screenshot=".Length..];
            }
            else if (arg == "--map-preview")
            {
                _mapPreview = true;
            }
            else if (arg == "--attack-nearest")
            {
                _autoAttackNearest = true;
            }
            else if (arg.StartsWith("--screenshot-delay=", System.StringComparison.Ordinal)
                     && int.TryParse(arg["--screenshot-delay=".Length..], out var frames))
            {
                _screenshotDelayFrames = Mathf.Max(frames, 1);
            }
        }

        if (_mapPreview)
        {
            EnterMapPreview();
            return;
        }

        if (_autoLoginName != null)
        {
            _nameField.Text = _autoLoginName;
            GD.Print($"[teste] autologin como '{_autoLoginName}' em {_hostField.Text}");
            OnConnectPressed();
        }
    }

    // --- Montagem da cena --------------------------------------------------

    private bool _mapPreview;

    private void EnterMapPreview()
    {
        // Read the actual server layout for an offline art preview.
        var source = FileAccess.GetFileAsString("res://server/maps/starter_fields.lua");
        var rows = System.Text.RegularExpressions.Regex.Matches(source, "\"([#.~=:T]{32})\"");
        var map = new MapData();
        map.Begin("starter_fields", 32, rows.Count);
        for (var y = 0; y < rows.Count; y++)
        {
            var row = new byte[32];
            for (var x = 0; x < 32; x++)
                row[x] = rows[y].Groups[1].Value[x] switch
                {
                    '~' => 0x10, '#' => 0x22, '=' => 0x31, ':' => 0x41,
                    'T' => 0x02, _ => 0x01,
                };
            map.SetRow(y, row);
        }
        _mapRenderer.Build(map);
        _loginPanel.Visible = false;
        _camera.Size = 39;
        _camera.Position = new Vector3(16, 0, 16) + CameraOffset();
        if (_screenshotPath != null) _framesUntilScreenshot = _screenshotDelayFrames;
    }

    private void BuildEnvironment()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.08f, 0.10f, 0.14f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.55f, 0.58f, 0.65f),
            AmbientLightEnergy = 0.6f,
        };
        AddChild(new WorldEnvironment { Name = "WorldEnvironment", Environment = environment });

        var light = new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-55.0f, 35.0f, 0.0f),
            LightEnergy = 1.1f,
            ShadowEnabled = true,
        };
        AddChild(light);

        _camera = new Camera3D
        {
            Name = "Camera",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = CameraSize,
            RotationDegrees = CameraAngles,
            // Ortogonal: a distância não muda o enquadramento, só precisa
            // passar do `near` e manter a cena dentro do `far`.
            Position = new Vector3(0, 30, 30),
            Far = 200.0f,
            Current = true,
        };
        AddChild(_camera);
    }

    private void BuildUi()
    {
        var layer = new CanvasLayer { Name = "Ui" };
        AddChild(layer);

        _loginPanel = new PanelContainer
        {
            Name = "LoginPanel",
            AnchorsPreset = (int)Control.LayoutPreset.Center,
        };
        _loginPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        layer.AddChild(_loginPanel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 20);
        _loginPanel.AddChild(margin);

        var column = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
        margin.AddChild(column);

        column.AddChild(new Label { Text = "Backlands", HorizontalAlignment = HorizontalAlignment.Center });

        column.AddChild(new Label { Text = "Servidor" });
        _hostField = new LineEdit { Text = "127.0.0.1:7777" };
        column.AddChild(_hostField);

        column.AddChild(new Label { Text = "Personagem" });
        _nameField = new LineEdit
        {
            Text = "Heroi",
            MaxLength = Protocol.MaxNameLength,
            PlaceholderText = "3 a 16 caracteres",
        };
        column.AddChild(_nameField);

        _connectButton = new Button { Text = "Entrar" };
        _connectButton.Pressed += OnConnectPressed;
        column.AddChild(_connectButton);

        _statusLabel = new Label { Text = string.Empty, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_statusLabel);

        _hudLabel = new Label
        {
            Name = "Hud",
            Text = string.Empty,
            Position = new Vector2(12, 8),
            Visible = false,
        };
        layer.AddChild(_hudLabel);

        // Enter no campo de nome também entra.
        _nameField.TextSubmitted += _ => OnConnectPressed();
    }

    // --- Conexão -----------------------------------------------------------

    private void OnConnectPressed()
    {
        var name = _nameField.Text.Trim();
        if (!Protocol.IsValidCharacterName(name))
        {
            // Validação local só para evitar um round-trip óbvio; a validação
            // que vale é a do servidor.
            _statusLabel.Text = $"Nome inválido: use {Protocol.MinNameLength} a "
                + $"{Protocol.MaxNameLength} caracteres, apenas letras, números e _";
            return;
        }

        var (host, port) = ParseHost(_hostField.Text);
        _statusLabel.Text = $"Conectando em {host}:{port}...";
        _connectButton.Disabled = true;
        _session.ConnectAndLogin(host, port, name);
    }

    private static (string Host, int Port) ParseHost(string text)
    {
        var trimmed = text.Trim();
        var separator = trimmed.LastIndexOf(':');
        if (separator > 0 && int.TryParse(trimmed[(separator + 1)..], out var port))
        {
            return (trimmed[..separator], port);
        }
        return (trimmed.Length == 0 ? "127.0.0.1" : trimmed, 7777);
    }

    private void OnConnectionFailed(string reason)
    {
        _statusLabel.Text = reason;
        _connectButton.Disabled = false;
    }

    private void OnDisconnected(string reason)
    {
        _statusLabel.Text = $"Desconectado: {reason}";
        _connectButton.Disabled = false;
        _loginPanel.Visible = true;
        _hudLabel.Visible = false;

        foreach (var view in _views.Values)
        {
            view.QueueFree();
        }
        _views.Clear();
    }

    private void OnLoginRejected(LoginFailure reason)
    {
        _statusLabel.Text = LoginFailureText.Describe(reason);
        _connectButton.Disabled = false;
    }

    // --- Mundo -------------------------------------------------------------

    private void OnMapReady()
    {
        var map = _session.World.Map;
        _mapRenderer.Build(map);
        GD.Print($"[teste] mapa '{map.Name}' {map.Width}x{map.Height} recebido");
    }

    private void OnEnteredWorld(RemoteEntity entity)
    {
        _loginPanel.Visible = false;
        _statusLabel.Text = string.Empty;
        _hudLabel.Visible = true;

        AddView(entity, isLocal: true);
        CenterCameraOn(entity.CellX, entity.CellY);
        UpdateHud();

        GD.Print($"[teste] entrou no mundo: entidade {entity.Id} '{entity.Name}' "
            + $"em ({entity.CellX}, {entity.CellY})");

        if (_autoWalk != null)
        {
            GD.Print($"[teste] pedindo caminhada para ({_autoWalk.Value.X}, {_autoWalk.Value.Y})");
            _session.RequestWalk(_autoWalk.Value.X, _autoWalk.Value.Y);
        }
        if (_screenshotPath != null)
        {
            // Tempo para o mapa e os sprites desenharem e, num teste com vários
            // clientes, para todos estarem no mundo ao mesmo tempo.
            _framesUntilScreenshot = _screenshotDelayFrames;
        }
    }

    private void OnEntitySpawned(RemoteEntity entity)
    {
        AddView(entity, isLocal: entity.Id == _session.World.LocalEntityId);
        UpdateHud();

        if (_autoAttackNearest && entity.Kind == EntityKind.Monster && _targetId == 0)
        {
            _targetId = entity.Id;
            GD.Print($"[teste] atacando '{entity.Name}' (entidade {entity.Id}) "
                + $"em ({entity.CellX}, {entity.CellY})");
            _session.RequestAttack(entity.Id);
        }
    }

    private void OnEntityDespawned(RemoteEntity entity)
    {
        if (_views.Remove(entity.Id, out var view))
        {
            view.QueueFree();
        }
        UpdateHud();
    }

    private void OnEntityWalk(WalkOrder order, RemoteEntity entity)
    {
        if (_views.TryGetValue(order.EntityId, out var view))
        {
            view.FollowPath(order);
        }
        GD.Print($"[teste] entidade {order.EntityId} anda {order.Path.Length} passo(s) "
            + $"de ({order.FromX}, {order.FromY}) a ({order.ToX}, {order.ToY})");
    }

    private void OnEntityStopped(RemoteEntity entity)
    {
        if (_views.TryGetValue(entity.Id, out var view))
        {
            view.SnapTo(entity.CellX, entity.CellY);
        }
        GD.Print($"[teste] entidade {entity.Id} parada em ({entity.CellX}, {entity.CellY})");
    }

    private void OnEntityAttacked(RemoteEntity? source, RemoteEntity target, int damage)
    {
        if (_views.TryGetValue(target.Id, out var view))
        {
            view.UpdateHealth(target);

            // Dano 0 é erro de ataque: o servidor não manda pacote separado
            // para isso, então é aqui que a distinção aparece ao jogador.
            if (damage <= 0)
            {
                view.ShowFloatingText("Miss", new Color(0.75f, 0.75f, 0.8f));
                GD.Print($"[teste] {source?.Name ?? "?"} errou em {target.Name}");
            }
            else
            {
                var hurtMe = target.Id == _session.World.LocalEntityId;
                view.ShowFloatingText(damage.ToString(),
                                      hurtMe ? new Color(1.0f, 0.4f, 0.35f)
                                             : new Color(1.0f, 0.92f, 0.45f));
                GD.Print($"[teste] {source?.Name ?? "?"} causou {damage} em {target.Name} "
                    + $"(HP {target.Hp}/{target.MaxHp})");
            }
        }
    }

    private void OnEntityDied(RemoteEntity dead, RemoteEntity? killer)
    {
        if (_views.TryGetValue(dead.Id, out var view))
        {
            view.UpdateHealth(dead);
            view.SetDead(true);
        }

        if (dead.Id == _session.World.LocalEntityId)
        {
            _statusLabel.Text = "Você morreu. Renascendo...";
            _statusLabel.Visible = true;
        }
        if (dead.Id == _targetId)
        {
            _targetId = 0;
        }
        GD.Print($"[teste] {dead.Name} morreu (morto por {killer?.Name ?? "?"})");
    }

    private void OnEntityRespawned(RemoteEntity entity)
    {
        if (_views.TryGetValue(entity.Id, out var view))
        {
            view.SetDead(false);
            view.SnapTo(entity.CellX, entity.CellY);
            view.UpdateHealth(entity);
        }

        if (entity.Id == _session.World.LocalEntityId)
        {
            _statusLabel.Text = string.Empty;
            _statusLabel.Visible = false;
        }
        GD.Print($"[teste] {entity.Name} renasceu em ({entity.CellX}, {entity.CellY})");
    }

    private void OnAttackRejected(AttackRejection reason)
    {
        GD.Print($"[teste] ataque recusado: {AttackRejectionText.Describe(reason)}");
    }

    /// Entidade viva ocupando a célula, ou 0. Ignora a própria entidade:
    /// clicar em si mesmo deve andar, não atacar.
    private uint FindEntityAtCell(int cellX, int cellY)
    {
        foreach (var (id, entity) in _session.World.Entities)
        {
            if (id == _session.World.LocalEntityId || !entity.Alive)
            {
                continue;
            }
            if (entity.CellX == cellX && entity.CellY == cellY)
            {
                return id;
            }
        }
        return 0;
    }

    private void AddView(RemoteEntity entity, bool isLocal)
    {
        if (_views.ContainsKey(entity.Id))
        {
            return;
        }
        var view = EntityView.Create(entity, isLocal);
        _entityRoot.AddChild(view);
        _views[entity.Id] = view;
    }

    private void UpdateHud()
    {
        var world = _session.World;
        var local = world.Local;

        // Mostra a célula sob a posição VISUAL, não a autoritativa: durante uma
        // caminhada a autoritativa já é o destino, e exibi-la pareceria que o
        // personagem teleportou.
        var cell = "-";
        if (local != null && _views.TryGetValue(local.Id, out var view))
        {
            var (x, y) = MapRenderer.WorldToCell(view.Position);
            cell = $"({x}, {y})";
        }

        var hp = local == null ? "-" : $"{local.Hp}/{local.MaxHp}";
        _hudLabel.Text = $"{world.MapName}  |  célula {cell}  |  HP {hp}  |  "
            + $"{world.Entities.Count} entidade(s)  |  clique no chão para andar, "
            + "no inimigo para atacar";
    }

    // --- Entrada -----------------------------------------------------------

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_session.World.InWorld)
        {
            return;
        }
        if (_mapPreview)
        {
            EnterMapPreview();
            return;
        }

        if (_autoLoginName != null)
        {
            // Em execução automatizada o jogo ignora o mouse: a janela abre em
            // foco e um clique acidental do operador já causou uma caminhada
            // inesperada no meio de um teste, tornando-o não determinístico.
            return;
        }
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            return;
        }

        var cell = ScreenToCell(click.Position);
        if (cell == null)
        {
            return;
        }

        // Clicar em cima de outra entidade seleciona alvo; no chão, anda.
        // Compara por CÉLULA e não por pixel: é a mesma unidade em que o
        // servidor decide alcance, então o que o jogador clica é o que o
        // servidor vê.
        var targetId = FindEntityAtCell(cell.Value.X, cell.Value.Y);
        if (targetId != 0)
        {
            _session.RequestAttack(targetId);
            return;
        }

        // Manda só o DESTINO. Quem decide o caminho — e se ele existe — é o
        // servidor; aqui nem checamos se a célula é caminhável.
        _session.RequestWalk(cell.Value.X, cell.Value.Y);
    }

    /// Projeta o clique no plano do chão (y = 0). Interseção com o plano, e não
    /// raycast de física: é exato e não exige corpos de colisão no mapa.
    private (int X, int Y)? ScreenToCell(Vector2 screenPosition)
    {
        var origin = _camera.ProjectRayOrigin(screenPosition);
        var direction = _camera.ProjectRayNormal(screenPosition);

        // Câmera olhando para baixo: direction.Y é negativo. Paralelo ao chão
        // não intersecta.
        if (Mathf.Abs(direction.Y) < 0.0001f)
        {
            return null;
        }

        var distance = -origin.Y / direction.Y;
        if (distance < 0.0f)
        {
            return null;
        }

        var hit = origin + direction * distance;
        return MapRenderer.WorldToCell(hit);
    }

    public override void _Process(double delta)
    {
        var local = _session.World.Local;
        if (local != null && _views.TryGetValue(local.Id, out var view))
        {
            // Câmera segue a posição INTERPOLADA, não a célula, senão ela
            // andaria aos saltos de um tile.
            FollowCamera(view.Position);
            UpdateHud();
        }

        if (_framesUntilScreenshot > 0)
        {
            _framesUntilScreenshot--;
        }
        else if (_framesUntilScreenshot == 0)
        {
            _framesUntilScreenshot = -1;
            SaveScreenshot();
        }
    }

    private void SaveScreenshot()
    {
        var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(_screenshotPath);
        GD.Print(error == Error.Ok
            ? $"[teste] screenshot salvo em {_screenshotPath}"
            : $"[teste] falha ao salvar screenshot: {error}");

        // Não encerra aqui: com vários clientes, sair logo após a própria
        // captura faria cada um fotografar um mundo já esvaziado pelos outros.
        // Quem encerra é o --quit-after da linha de comando.
    }

    private void CenterCameraOn(int cellX, int cellY)
    {
        var target = MapRenderer.CellToWorld(cellX, cellY);
        _camera.Position = target + CameraOffset();
    }

    private void FollowCamera(Vector3 target)
    {
        _camera.Position = target + CameraOffset();
    }

    /// Deslocamento que mantém os ângulos da câmera apontados para o alvo.
    /// Com projeção ortogonal a distância não altera o enquadramento.
    private static Vector3 CameraOffset()
    {
        const float distance = 40.0f;
        var basis = Basis.FromEuler(new Vector3(Mathf.DegToRad(CameraAngles.X),
                                                Mathf.DegToRad(CameraAngles.Y), 0.0f));
        // -Z é o "para frente" da câmera no Godot, então recuamos por +Z local.
        return basis * new Vector3(0, 0, distance);
    }
}
