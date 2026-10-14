using Backlands.Model;
using Backlands.Scene;

namespace Backlands.Game;

/// <summary>
/// O alvo escolhido pelo jogador e o anel que o marca.
///
/// O alvo é uma decisão LOCAL: o servidor só fica sabendo dele quando o
/// jogador ataca. Guardá-lo aqui, num lugar só, é o que mantém o anel, o HUD,
/// o conjuro e o clique de acordo sobre quem é o alvo.
/// </summary>
public sealed class TargetSelection
{
    private readonly WorldState _world;
    private readonly WorldView _views;

    public TargetSelection(WorldState world, WorldView views)
    {
        _world = world;
        _views = views;
    }

    /// Entidade selecionada; 0 = nenhuma.
    public uint TargetId { get; private set; }

    /// Marca a entidade escolhida e apaga a marca da anterior.
    public void Select(uint entityId)
    {
        if (TargetId == entityId)
        {
            return;
        }

        if (TargetId != 0 && _views.TryGetView(TargetId, out var previous))
        {
            previous.SetTargeted(false);
        }

        TargetId = entityId;

        if (TargetId != 0 && _views.TryGetView(TargetId, out var current))
        {
            current.SetTargeted(true);
        }
    }

    /// A view do alvo atual, ou null se não há alvo, ele sumiu ou morreu.
    public EntityView? LivingTarget()
    {
        if (TargetId == 0 || !_views.TryGetView(TargetId, out var view))
        {
            return null;
        }
        var entity = _world.Find(TargetId);
        return entity is { Alive: true } ? view : null;
    }
}
