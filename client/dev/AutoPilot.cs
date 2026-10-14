using Backlands.Game;
using Backlands.Model;
using Backlands.Ui;
using Godot;

namespace Backlands.Dev;

/// <summary>
/// O jogador automático dos testes: anda (--walk), ataca o primeiro monstro
/// que aparecer (--attack-nearest), conjura (--cast) e agenda a captura de
/// tela ao entrar no mundo.
///
/// Usa os MESMOS caminhos do jogador de verdade — pedidos ao servidor, seleção
/// de alvo, conjuro —, para o teste exercitar o client real e não um atalho.
/// </summary>
public sealed class AutoPilot
{
    private readonly (int X, int Y)? _autoWalk;
    private readonly bool _autoAttackNearest;
    private int _autoCastSlot;

    private readonly IPlayerCommands _commands;
    private readonly TargetSelection _targets;
    private readonly CastController _cast;
    private readonly ScreenshotCapture _screenshot;

    /// <param name="castSlot">Slot de --cast, contando de 1; fora de 1..12 desliga.</param>
    public AutoPilot((int X, int Y)? walk, bool attackNearest, int castSlot,
                     IPlayerCommands commands, TargetSelection targets, CastController cast,
                     ScreenshotCapture screenshot)
    {
        _autoWalk = walk;
        _autoAttackNearest = attackNearest;
        _autoCastSlot = castSlot;
        _commands = commands;
        _targets = targets;
        _cast = cast;
        _screenshot = screenshot;
    }

    /// O fim de "entrou no mundo": a caminhada pedida e a captura agendada.
    public void OnEnteredWorld()
    {
        if (_autoWalk != null)
        {
            GD.Print($"[teste] pedindo caminhada para ({_autoWalk.Value.X}, {_autoWalk.Value.Y})");
            _commands.RequestWalk(_autoWalk.Value.X, _autoWalk.Value.Y);
        }
        // Tempo para o mapa e os sprites desenharem e, num teste com vários
        // clientes, para todos estarem no mundo ao mesmo tempo.
        _screenshot.Schedule();
    }

    /// Ataca o primeiro monstro que aparecer, se pedido. A view dele PRECISA
    /// já existir, senão o anel de alvo não tem onde aparecer.
    public void OnEntitySpawned(RemoteEntity entity)
    {
        if (_autoAttackNearest && entity.Kind == EntityKind.Monster && _targets.TargetId == 0)
        {
            _targets.Select(entity.Id);
            GD.Print($"[teste] atacando '{entity.Name}' (entidade {entity.Id}) "
                + $"em ({entity.CellX}, {entity.CellY})");
            _commands.RequestAttack(entity.Id);
        }
    }

    /// <summary>
    /// Dispara o slot de --cast assim que houver um alvo vivo.
    ///
    /// Esperar o alvo, em vez de conjurar ao entrar no mundo: agora a skill
    /// exige alvo, e os monstros chegam depois do próprio jogador. Dispara uma
    /// vez só.
    /// </summary>
    public void AdvanceAutoCast()
    {
        if (_autoCastSlot < 1 || _autoCastSlot > TestSkills.Slots.Length
            || _targets.LivingTarget() == null)
        {
            return;
        }

        var index = _autoCastSlot - 1;
        _autoCastSlot = -1;
        _cast.Trigger(index, TestSkills.Slots[index]);
    }
}
