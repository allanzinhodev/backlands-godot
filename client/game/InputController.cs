using Backlands.Model;
using Backlands.Scene;
using Godot;

namespace Backlands.Game;

/// <summary>
/// Traduz mouse em intenção: girar e aproximar a câmera, escolher alvo e
/// atacar, ou pedir caminhada.
///
/// Recebe o evento já não tratado pela UI (a raiz da cena repassa o
/// <c>_UnhandledInput</c>) e decide o que ele significa; quem executa são a
/// câmera, a seleção de alvo e os pedidos ao servidor.
/// </summary>
public sealed class InputController
{
    private readonly WorldState _world;
    private readonly IPlayerCommands _commands;
    private readonly CameraRig _camera;
    private readonly TargetSelection _targets;
    private readonly CastController _cast;
    private readonly bool _captureRun;

    /// <param name="captureRun">Execução de captura de tela: o mouse é ignorado.</param>
    public InputController(WorldState world, IPlayerCommands commands, CameraRig camera,
                           TargetSelection targets, CastController cast, bool captureRun)
    {
        _world = world;
        _commands = commands;
        _camera = camera;
        _targets = targets;
        _cast = cast;
        _captureRun = captureRun;
    }

    public void Handle(InputEvent @event)
    {
        if (!_world.InWorld)
        {
            return;
        }
        // Só a execução de CAPTURA ignora o mouse: a janela abre em foco e um
        // clique acidental já tornou um teste não determinístico. Entrar por
        // --autologin para jogar continua respondendo normalmente, senão o
        // atalho de abrir o jogo logado seria injogável.
        if (_captureRun)
        {
            return;
        }
        // Botão direito gira a câmera. O estado vem do PRÓPRIO evento de
        // movimento, não de uma trava ligada na descida e desligada na subida:
        // um Control com MouseFilter.Stop engole o evento antes daqui, e os
        // slots da barra de skills são Stop — soltar o botão em cima de um
        // deles nunca desligaria a trava, e o mundo passaria a girar com o
        // mouse solto. Ler a máscara é autocorretivo.
        if (@event is InputEventMouseMotion motion)
        {
            if (motion.ButtonMask.HasFlag(MouseButtonMask.Right))
            {
                _camera.Rotate(motion.Relative);
            }
            return;
        }
        // Roda do mouse dá zoom. Só na DESCIDA: cada rolagem chega como um par
        // pressionado/solto, e tratar os dois daria passo dobrado.
        if (@event is InputEventMouseButton { Pressed: true } wheel
            && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            _camera.Zoom(wheel.ButtonIndex == MouseButton.WheelUp
                ? 1.0f / CameraRig.ZoomStep
                : CameraRig.ZoomStep);
            return;
        }

        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right } rightButton)
        {
            // Duplo clique volta para a isométrica, como no Ragnarok: depois de
            // girar um pouco é fácil perder a noção de onde fica o norte, e
            // acertar 45° no arrasto é impossível.
            if (rightButton.DoubleClick)
            {
                _camera.Reset();
            }
            return;
        }

        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            return;
        }

        var cell = _camera.ScreenToCell(click.Position);
        if (cell == null)
        {
            return;
        }

        // Clicar em cima de outra entidade seleciona alvo; no chão, anda.
        // Compara por CÉLULA e não por pixel: é a mesma unidade em que o
        // servidor decide alcance, então o que o jogador clica é o que o
        // servidor vê.
        // Conjurando, o clique é IGNORADO — não cancela. No Ragnarok andar e
        // atacar não interrompem o conjuro: eles são proibidos enquanto ele
        // dura, e o pedido simplesmente não acontece. Com meio segundo de
        // conjuro a diferença é grande: ignorar faz o personagem plantar os pés
        // e soltar a skill, enquanto cancelar perderia o conjuro a cada clique
        // apressado.
        if (_cast.IsCasting)
        {
            return;
        }

        var targetId = FindEntityAtCell(cell.Value.X, cell.Value.Y);
        if (targetId != 0)
        {
            // Seleciona E ataca: a seleção é o que as skills precisam, e trocar
            // o clique por "só seleciona" tiraria a única forma de atacar.
            _targets.Select(targetId);
            _commands.RequestAttack(targetId);
            return;
        }

        // Manda só o DESTINO. Quem decide o caminho — e se ele existe — é o
        // servidor; aqui nem checamos se a célula é caminhável.
        _commands.RequestWalk(cell.Value.X, cell.Value.Y);
    }

    /// Entidade viva ocupando a célula, ou 0. Ignora a própria entidade:
    /// clicar em si mesmo deve andar, não atacar.
    private uint FindEntityAtCell(int cellX, int cellY)
    {
        foreach (var (id, entity) in _world.Entities)
        {
            if (id == _world.LocalEntityId || !entity.Alive)
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
}
