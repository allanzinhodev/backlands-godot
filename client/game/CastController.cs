using Backlands.Model;
using Backlands.Scene;
using Backlands.Ui;
using Godot;

namespace Backlands.Game;

/// <summary>
/// O conjuro das skills da barra de teste: começa, espera o prazo, confere o
/// alvo e solta o efeito — ou é interrompido.
///
/// Só decide; a aparência do conjuro (pose, barra, nome da skill) é da
/// <see cref="EntityView"/>.
/// </summary>
public sealed class CastController
{
    // --- Conjuro -----------------------------------------------------------

    /// Quanto dura o conjuro das skills de teste.
    ///
    /// Vive no cliente porque a barra de skills é banco de provas: não há
    /// protocolo de skill, e nada disto chega ao servidor ou aos outros
    /// jogadores. Quando as skills existirem, quem manda neste número — e em
    /// quando o efeito sai — é o servidor.
    private const double CastSeconds = 0.5;

    private readonly WorldState _world;
    private readonly WorldView _views;
    private readonly TargetSelection _targets;
    private readonly IStatusLine _status;

    /// Onde os efeitos entram na cena.
    private readonly Node _effectsParent;

    /// Relógio local, só para prazos de conjuro.
    private double _now;

    private SkillSlot _castSlot;
    private int _castSlotIndex = -1;
    private uint _castTargetId;
    private double _castEndsAt;

    public CastController(WorldState world, WorldView views, TargetSelection targets,
                          IStatusLine status, Node effectsParent)
    {
        _world = world;
        _views = views;
        _targets = targets;
        _status = status;
        _effectsParent = effectsParent;
    }

    /// Se o personagem local está conjurando agora.
    public bool IsCasting => _castSlotIndex >= 0;

    /// Avança o relógio dos prazos. Separado de <see cref="Advance"/> porque o
    /// relógio anda no começo do quadro, e o conjuro só depois da câmera.
    public void AdvanceClock(double delta)
    {
        _now += delta;
    }

    /// <summary>
    /// Toca o efeito do slot na posição do personagem.
    ///
    /// É um banco de PROVA de arte: o efeito sai localmente, sem passar pelo
    /// servidor. Quando as skills existirem, a tecla vira um pedido e o efeito
    /// só toca quando o servidor confirmar — como já acontece com o ataque.
    /// </summary>
    public void Trigger(int index, SkillSlot slot)
    {
        var local = _world.Local;
        if (local == null || !_views.TryGetView(local.Id, out var casterView))
        {
            return;
        }

        // Uma skill de cada vez: no Ragnarok o conjuro também bloqueia outras
        // skills, e sem isto apertar F2 no meio de F1 trocaria o efeito sem
        // reiniciar a barra.
        if (IsCasting)
        {
            return;
        }

        if (!VfxPlayer.IsAvailable(slot.VfxId))
        {
            GD.Print($"[skills] F{index + 1} ({slot.Label}): efeito '{slot.VfxId}' indisponível");
            return;
        }

        // Alvo conferido no INÍCIO; o fim do conjuro confere de novo, porque meio
        // segundo é tempo bastante para o alvo morrer ou sumir.
        var target = _targets.LivingTarget();
        if (target == null)
        {
            _status.StatusText = "Selecione um alvo: clique no inimigo.";
            _status.StatusVisible = true;
            return;
        }

        _status.StatusVisible = false;
        _castSlot = slot;
        _castSlotIndex = index;
        _castTargetId = _targets.TargetId;
        _castEndsAt = _now + CastSeconds;

        casterView.BeginCast(
            GridSpace.FacingTowards(casterView.Position, target.Position), CastSeconds, slot.Label);
        GD.Print($"[teste] F{index + 1} conjurando {slot.Label} em {_targets.TargetId}");
    }

    /// Interrompe o conjuro em andamento, se houver.
    public void Cancel(string reason)
    {
        if (_castSlotIndex < 0)
        {
            return;
        }

        var local = _world.Local;
        if (local != null && _views.TryGetView(local.Id, out var view))
        {
            view.EndCast();
        }

        GD.Print($"[teste] conjuro de {_castSlot.Label} interrompido: {reason}");
        _castSlotIndex = -1;
        _castTargetId = 0;
    }

    /// Leva o conjuro até o fim e solta o efeito NO ALVO.
    public void Advance()
    {
        if (_castSlotIndex < 0)
        {
            return;
        }

        if (_targets.TargetId != _castTargetId)
        {
            Cancel("alvo trocado");
            return;
        }

        var target = _targets.LivingTarget();
        if (target == null)
        {
            Cancel("alvo sumiu");
            return;
        }

        if (_now < _castEndsAt)
        {
            return;
        }

        // Posição INTERPOLADA do alvo, não a célula: se ele estiver andando,
        // a célula já é o destino e o efeito sairia à frente dele.
        var effect = VfxPlayer.CreateById(_castSlot.VfxId, target.Position);
        if (effect != null)
        {
            _effectsParent.AddChild(effect);
        }

        GD.Print($"[teste] F{_castSlotIndex + 1} -> {_castSlot.Label} em {_castTargetId}");

        var local = _world.Local;
        if (local != null && _views.TryGetView(local.Id, out var view))
        {
            view.EndCast();
        }
        _castSlotIndex = -1;
        _castTargetId = 0;
    }
}
