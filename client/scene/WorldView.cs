using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Backlands.Model;
using Godot;

namespace Backlands.Scene;

/// <summary>
/// O nó "Entities": um <see cref="EntityView"/> por entidade conhecida, e as
/// reações VISUAIS aos eventos do mundo.
///
/// Só aparência. Quem decide alvo, conjuro, HUD e mensagens ao jogador é a
/// camada de jogo; aqui a entidade só anda, para, bate, apanha, morre e
/// renasce na tela.
///
/// Os On* são assinados direto nos eventos do mundo — por isso têm a
/// assinatura do evento, mesmo com parâmetro sem uso. Os Show* são só a parte
/// visual de uma reação maior, que a raiz da cena encadeia com alvo, conjuro
/// e mensagens na ordem que importa.
/// </summary>
public sealed partial class WorldView : Node3D
{
    private readonly Dictionary<uint, EntityView> _views = new();
    private readonly WorldState _world;

    public WorldView(WorldState world)
    {
        _world = world;
    }

    public bool TryGetView(uint entityId, [MaybeNullWhen(false)] out EntityView view) =>
        _views.TryGetValue(entityId, out view);

    public void Add(RemoteEntity entity, bool isLocal)
    {
        if (_views.ContainsKey(entity.Id))
        {
            return;
        }
        var view = EntityView.Create(entity, isLocal);
        AddChild(view);
        _views[entity.Id] = view;
    }

    public void Remove(uint entityId)
    {
        if (_views.Remove(entityId, out var view))
        {
            view.QueueFree();
        }
    }

    /// Libera todas as views, como ao cair a conexão.
    public void Clear()
    {
        foreach (var view in _views.Values)
        {
            view.QueueFree();
        }
        _views.Clear();
    }

    public void OnEntityWalk(WalkOrder order, RemoteEntity entity)
    {
        if (_views.TryGetValue(order.EntityId, out var view))
        {
            view.FollowPath(order);
        }
        GD.Print($"[teste] entidade {order.EntityId} anda {order.Path.Length} passo(s) "
            + $"de ({order.FromX}, {order.FromY}) a ({order.ToX}, {order.ToY})");
    }

    public void OnEntityStopped(RemoteEntity entity)
    {
        if (_views.TryGetValue(entity.Id, out var view))
        {
            view.SnapTo(entity.CellX, entity.CellY, entity.Facing);
        }
        GD.Print($"[teste] entidade {entity.Id} parada em ({entity.CellX}, {entity.CellY})");
    }

    public void OnEntityAttacked(RemoteEntity? source, RemoteEntity target, int damage,
                                 int motionMs)
    {
        // O atacante toca o golpe e já encara o alvo: a direção vem do pacote,
        // não de um palpite do client.
        if (source != null && _views.TryGetValue(source.Id, out var attackerView))
        {
            attackerView.PlayAttack(source.Facing, motionMs);
        }

        if (_views.TryGetValue(target.Id, out var view))
        {
            view.UpdateHealth(target);

            // Dano 0 é erro de ataque: o servidor não manda pacote separado
            // para isso, então é aqui que a distinção aparece ao jogador.
            if (damage <= 0)
            {
                view.PlayEvade(motionMs);
                view.ShowFloatingText("Miss", new Color(0.75f, 0.75f, 0.8f));
                GD.Print($"[teste] {source?.Name ?? "?"} errou em {target.Name}");
            }
            else
            {
                var hurtMe = target.Id == _world.LocalEntityId;
                view.ShowFloatingText(damage.ToString(),
                                      hurtMe ? new Color(1.0f, 0.4f, 0.35f)
                                             : new Color(1.0f, 0.92f, 0.45f));
                GD.Print($"[teste] {source?.Name ?? "?"} causou {damage} em {target.Name} "
                    + $"(HP {target.Hp}/{target.MaxHp})");
            }
        }
    }

    /// A parte visual da morte: barra zerada e pose de cadáver.
    public void ShowDeath(RemoteEntity dead)
    {
        if (_views.TryGetValue(dead.Id, out var view))
        {
            view.UpdateHealth(dead);
            view.SetDead(true);
        }
    }

    /// A parte visual do renascimento: de pé, na célula nova, com a vida nova.
    public void ShowRespawn(RemoteEntity entity)
    {
        if (_views.TryGetValue(entity.Id, out var view))
        {
            view.SetDead(false);
            view.SnapTo(entity.CellX, entity.CellY, entity.Facing);
            view.UpdateHealth(entity);
        }
    }
}
