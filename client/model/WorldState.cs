using System;
using System.Collections.Generic;

namespace Backlands.Model;

/// <summary>
/// Espelho local do estado do mundo. Traduz pacotes em eventos que a camada de
/// cena consome para criar, mover e remover nós.
///
/// Sem nenhuma dependência de Godot: é lógica pura e pode ser exercitada fora
/// do editor.
///
/// Recebe as mensagens JÁ decodificadas (<see cref="ServerEvents"/>): ler os
/// bytes é do leitor de pacotes, na camada de rede. Cada Apply devolve false
/// quando a mensagem viola o contrato com o estado atual — quem chama derruba a
/// conexão, como faria com um payload truncado.
/// </summary>
public sealed class WorldState
{
    private readonly Dictionary<uint, RemoteEntity> _entities = new();

    /// Entidade controlada por este client. 0 = ainda não entrou no mundo.
    public uint LocalEntityId { get; private set; }

    public string MapName { get; private set; } = string.Empty;

    public MapData Map { get; } = new();

    public bool InWorld => LocalEntityId != 0;

    public IReadOnlyDictionary<uint, RemoteEntity> Entities => _entities;

    public event Action<RemoteEntity>? EntitySpawned;

    public event Action<RemoteEntity>? EntityDespawned;

    /// Disparado quando o próprio jogador entra: traz a entidade local.
    public event Action<RemoteEntity>? EnteredWorld;

    public event Action<LoginFailure>? LoginRejected;

    /// Mapa completo (todas as linhas chegaram), pronto para desenhar.
    public event Action? MapReady;

    /// Trajeto a percorrer. A cena interpola; a célula final já é autoritativa.
    public event Action<WalkOrder, RemoteEntity>? EntityWalkStarted;

    /// A entidade parou aqui — caminho bloqueado ou destino recusado.
    public event Action<RemoteEntity>? EntityStopped;

    /// Alguém bateu em alguém. Dano 0 significa que o golpe errou. A duração
    /// vem do servidor para a animação caber no tempo real do golpe.
    public event Action<RemoteEntity?, RemoteEntity, int, int>? EntityAttacked;

    public event Action<RemoteEntity, RemoteEntity?>? EntityDied;

    public event Action<RemoteEntity>? EntityRespawned;

    public event Action<AttackRejection>? AttackRejected;

    public RemoteEntity? Find(uint id) => _entities.TryGetValue(id, out var entity) ? entity : null;

    public RemoteEntity? Local => LocalEntityId == 0 ? null : Find(LocalEntityId);

    public bool ApplyMapInfo(ServerEvents.MapInfo message)
    {
        Map.Begin(message.Name, message.Width, message.Height);
        return true;
    }

    /// Uma linha que não bate com o mapa anunciado é violação de contrato.
    public bool ApplyMapRow(ServerEvents.MapRow message)
    {
        if (message.Cells.Length != Map.Width || !Map.SetRow(message.Y, message.Cells))
        {
            return false;
        }

        if (Map.Complete)
        {
            MapReady?.Invoke();
        }
        return true;
    }

    public bool ApplyEnterWorld(ServerEvents.EnterWorld message)
    {
        ClearEntities();
        LocalEntityId = message.Id;
        MapName = message.Map;

        var entity = new RemoteEntity(message.Id, message.Name, message.Sprite, message.X, message.Y,
                                      message.Facing, EntityKind.Player, message.Hp, message.MaxHp);
        _entities[message.Id] = entity;

        EnteredWorld?.Invoke(entity);
        return true;
    }

    public bool ApplyEntitySpawn(ServerEvents.EntitySpawn message)
    {
        // Spawn repetido do mesmo id não deve duplicar o nó na cena; o
        // servidor é a fonte, então a última informação vence.
        if (_entities.TryGetValue(message.Id, out var existing))
        {
            existing.CellX = message.X;
            existing.CellY = message.Y;
            existing.Facing = message.Facing;
            existing.Hp = message.Hp;
            existing.MaxHp = message.MaxHp;
            EntityStopped?.Invoke(existing);
            return true;
        }

        var entity = new RemoteEntity(message.Id, message.Name, message.Sprite, message.X, message.Y,
                                      message.Facing, message.Kind, message.Hp, message.MaxHp);
        _entities[message.Id] = entity;
        EntitySpawned?.Invoke(entity);
        return true;
    }

    public bool ApplyEntityDespawn(ServerEvents.EntityDespawn message)
    {
        // Despawn de id desconhecido não é erro de protocolo: pode ser uma
        // entidade que nunca chegamos a ver. Ignorar é o correto.
        if (_entities.Remove(message.Id, out var entity))
        {
            EntityDespawned?.Invoke(entity);
        }
        return true;
    }

    public bool ApplyEntityWalk(ServerEvents.EntityWalk message)
    {
        if (!_entities.TryGetValue(message.Id, out var entity))
        {
            // Trajeto de entidade desconhecida: pode ser uma que ainda não
            // chegou. Ignorar é melhor que derrubar a conexão.
            return true;
        }

        var x = message.X;
        var y = message.Y;
        var path = message.Path;

        // A origem do trajeto é a posição autoritativa; o client se corrige
        // por ela em vez de confiar na sua própria interpolação.
        entity.CellX = x;
        entity.CellY = y;

        int destX = x;
        int destY = y;
        foreach (var dir in path)
        {
            destX += Directions.Delta(dir).X;
            destY += Directions.Delta(dir).Y;
        }

        // A célula autoritativa passa a ser o fim do trajeto: o servidor já
        // decidiu que a entidade chega lá, e qualquer desvio no meio vem como
        // um S_ENTITY_STOP explícito, que corrige. A posição que o jogador vê
        // é a interpolada no nó da cena, não esta.
        entity.CellX = destX;
        entity.CellY = destY;
        if (path.Length > 0)
        {
            entity.Facing = path[path.Length - 1];
        }

        EntityWalkStarted?.Invoke(
            new WalkOrder(message.Id, x, y, destX, destY, message.SpeedMsPerCell, path), entity);
        return true;
    }

    public bool ApplyEntityStop(ServerEvents.EntityStop message)
    {
        if (!_entities.TryGetValue(message.Id, out var entity))
        {
            return true;
        }

        entity.CellX = message.X;
        entity.CellY = message.Y;
        entity.Facing = message.Facing;
        EntityStopped?.Invoke(entity);
        return true;
    }

    public bool ApplyEntityAttack(ServerEvents.EntityAttack message)
    {
        // O atacante é corrigido para a posição e direção autoritativas: no
        // servidor, golpear limpa a caminhada sem emitir parada, então sem isto
        // o client continuaria percorrendo o trajeto abandonado e o atacante
        // deslizaria pelo chão enquanto ataca.
        if (_entities.TryGetValue(message.SourceId, out var source))
        {
            source.CellX = message.SourceX;
            source.CellY = message.SourceY;
            source.Facing = message.SourceFacing;
            EntityStopped?.Invoke(source);
        }

        if (!_entities.TryGetValue(message.TargetId, out var target))
        {
            return true; // alvo que nunca chegamos a ver
        }

        // A vida vem no pacote em vez de ser acumulada aqui: se um pacote de
        // dano se perdesse, uma barra calculada por subtração ficaria errada
        // para sempre.
        target.Hp = message.Hp;

        EntityAttacked?.Invoke(source, target, message.Damage, message.MotionMs);
        return true;
    }

    public bool ApplyEntityDied(ServerEvents.EntityDied message)
    {
        if (!_entities.TryGetValue(message.DeadId, out var dead))
        {
            return true;
        }

        // Onde o corpo caiu. Morrer no meio de uma caminhada não gera parada,
        // e sem esta correção o cadáver deslizaria até o destino antigo.
        dead.CellX = message.X;
        dead.CellY = message.Y;
        dead.Hp = 0;

        _entities.TryGetValue(message.KillerId, out var killer);
        EntityDied?.Invoke(dead, killer);
        return true;
    }

    public bool ApplyEntityRespawn(ServerEvents.EntityRespawn message)
    {
        if (!_entities.TryGetValue(message.Id, out var entity))
        {
            return true;
        }

        entity.CellX = message.X;
        entity.CellY = message.Y;
        entity.Facing = message.Facing;
        entity.Hp = message.Hp;
        EntityRespawned?.Invoke(entity);
        return true;
    }

    public bool ApplyAttackRejected(ServerEvents.AttackRejected message)
    {
        AttackRejected?.Invoke(message.Reason);
        return true;
    }

    public bool ApplyLoginFailed(ServerEvents.LoginFailed message)
    {
        LoginRejected?.Invoke(message.Reason);
        return true;
    }

    /// Limpa o estado ao desconectar, senão uma reconexão herdaria entidades
    /// fantasmas da sessão anterior.
    public void Reset()
    {
        ClearEntities();
        Map.Clear();
        MapName = string.Empty;
    }

    private void ClearEntities()
    {
        foreach (var entity in new List<RemoteEntity>(_entities.Values))
        {
            EntityDespawned?.Invoke(entity);
        }
        _entities.Clear();
        LocalEntityId = 0;
    }
}
