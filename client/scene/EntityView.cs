using System.Collections.Generic;
using Backlands.Art;
using Backlands.Model;
using Godot;

namespace Backlands.Scene;

/// <summary>
/// O nó visual de uma entidade: um sprite 2D billboard dentro do mundo 3D.
///
/// A posição e a direção autoritativas são sempre as que o servidor mandou.
/// Este nó só interpola entre células e escolhe a pose — nunca decide para
/// onde a entidade vai nem para onde ela encara.
///
/// Aqui moram o movimento e a máquina de estados da animação. O desenho do
/// corpo é do <see cref="EntitySprite"/>, e o que flutua sobre ele (barras,
/// nome, anel, dano) é do <see cref="EntityOverlay"/>.
/// </summary>
public sealed partial class EntityView : Node3D
{
    /// Tempo que o estado `walk` segura antes de voltar para `idle`. Sem esta
    /// folga há tremor garantido: a perseguição só recalcula a rota depois de
    /// parar, e cada emenda de trecho daria um quadro de `idle`.
    private const double WalkHysteresisSeconds = 0.2;

    private readonly Queue<(Vector3 Position, Direction Facing)> _waypoints = new();

    private EntitySprite _sprite = null!;
    private EntityOverlay _overlay = null!;

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

    public uint EntityId { get; private set; }

    public EntityKind Kind { get; private set; }

    public static EntityView Create(RemoteEntity entity, bool isLocal)
    {
        var view = new EntityView
        {
            Name = $"Entity{entity.Id}",
            EntityId = entity.Id,
            Kind = entity.Kind,
            Position = GridSpace.CellToWorld(entity.CellX, entity.CellY),
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

    /// O corpo primeiro e o que flutua depois: a ordem dos filhos é a ordem
    /// em que as partes os criam.
    private void BuildVisual(RemoteEntity entity, bool isLocal)
    {
        _sprite = new EntitySprite(this, entity, isLocal);
        _overlay = new EntityOverlay(this, entity, isLocal);
    }

    /// Liga ou desliga o anel de alvo.
    public void SetTargeted(bool targeted) => _overlay.SetRingVisible(targeted && !_dead);

    // --- Vida e morte ------------------------------------------------------

    public void UpdateHealth(RemoteEntity entity)
    {
        _overlay.UpdateHealth(entity.HpFraction, _dead, Kind);
    }

    /// Entidade morta mantém o nó, porque pode renascer com o mesmo id.
    public void SetDead(bool dead)
    {
        _dead = dead;
        _overlay.SetNameplateDead(dead);

        if (dead)
        {
            _waypoints.Clear();
            _overlay.HideHealth();
            // Morrer leva junto conjuro e seleção: cadáver não conjura, e um
            // anel sobre o corpo faria o jogador insistir num alvo que já era.
            EndCast();
            _overlay.SetRingVisible(false);
            EnterState(SpriteState.Dead);
        }
        else
        {
            _oneShotUntil = 0;
            EnterState(SpriteState.Idle);
        }

        _sprite.SetDead(dead);
    }

    public void ShowFloatingText(string text, Color color)
    {
        _overlay.ShowFloatingText(text, color);
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

        var authoritative = GridSpace.CellToWorld(order.FromX, order.FromY);
        if (Position.DistanceTo(authoritative) > 1.5f)
        {
            Position = authoritative;
        }

        var x = order.FromX;
        var y = order.FromY;
        foreach (var dir in order.Path)
        {
            var delta = Directions.Delta(dir);
            x += delta.X;
            y += delta.Y;
            _waypoints.Enqueue((GridSpace.CellToWorld(x, y), dir));
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
        Position = GridSpace.CellToWorld(cellX, cellY);
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

        _overlay.ShowCastLabel(skillName);
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
        _overlay.HideCast();
    }

    public bool Casting => _castUntil > _now;

    private void UpdateCastBar()
    {
        if (!Casting)
        {
            if (_overlay.CastBarVisible)
            {
                EndCast();
            }
            return;
        }

        var total = _castUntil - _castStartedAt;
        var fraction = (float)Mathf.Clamp((_now - _castStartedAt) / total, 0.0, 1.0);
        _overlay.ShowCastProgress(fraction);
    }

    private void EnterState(SpriteState state)
    {
        _state = state;
    }

    /// Leva o sprite para o estado e a direção desejados (ver
    /// <see cref="EntitySprite.ApplyPose"/>).
    private void ApplyPose()
    {
        _sprite.ApplyPose(_state, _renderFacing, _oneShotUntil, _now);
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
}
