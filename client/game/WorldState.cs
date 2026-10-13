using System;
using System.Collections.Generic;
using Backlands.Net;

namespace Backlands.Game;

/// Uma entidade como o client a conhece. A verdade é sempre do servidor:
/// o client só guarda o que recebeu.
public sealed class RemoteEntity
{
    public RemoteEntity(uint id, string name, int cellX, int cellY, Direction facing,
                        EntityKind kind, int hp, int maxHp)
    {
        Id = id;
        Name = name;
        CellX = cellX;
        CellY = cellY;
        Facing = facing;
        Kind = kind;
        Hp = hp;
        MaxHp = maxHp;
    }

    public uint Id { get; }

    public string Name { get; }

    public EntityKind Kind { get; }

    public int Hp { get; internal set; }

    public int MaxHp { get; internal set; }

    public bool Alive => Hp > 0;

    /// Fração de vida em [0, 1], para a barra. MaxHp zero nunca deveria vir do
    /// servidor, mas dividir por ele travaria o client.
    public float HpFraction => MaxHp <= 0 ? 0.0f : System.Math.Clamp((float)Hp / MaxHp, 0f, 1f);

    /// Célula autoritativa: onde o servidor diz que a entidade está. A posição
    /// visual interpolada vive no nó da cena, não aqui.
    public int CellX { get; internal set; }

    public int CellY { get; internal set; }

    public Direction Facing { get; internal set; }
}

/// Trajeto recebido do servidor: por onde a entidade vai passar.
public sealed class WalkOrder
{
    public WalkOrder(uint entityId, int fromX, int fromY, int toX, int toY, int speedMsPerCell,
                     Direction[] path)
    {
        EntityId = entityId;
        FromX = fromX;
        FromY = fromY;
        ToX = toX;
        ToY = toY;
        SpeedMsPerCell = speedMsPerCell;
        Path = path;
    }

    public uint EntityId { get; }

    public int FromX { get; }

    public int FromY { get; }

    /// Célula final do trajeto, derivada do caminho recebido.
    public int ToX { get; }

    public int ToY { get; }

    public int SpeedMsPerCell { get; }

    public Direction[] Path { get; }
}

/// <summary>
/// Espelho local do estado do mundo. Traduz pacotes em eventos que a camada de
/// cena consome para criar, mover e remover nós.
///
/// Sem nenhuma dependência de Godot: é lógica pura e pode ser exercitada fora
/// do editor.
/// </summary>
public sealed class WorldState
{
    /// Deslocamento de cada direção, na mesma ordem do servidor
    /// (server/src/game/Pathfinder.cpp). y cresce para o sul.
    public static readonly (int X, int Y)[] DirectionDeltas =
    {
        (0, 1),   // South
        (-1, 1),  // SouthWest
        (-1, 0),  // West
        (-1, -1), // NorthWest
        (0, -1),  // North
        (1, -1),  // NorthEast
        (1, 0),   // East
        (1, 1),   // SouthEast
    };

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

    /// Alguém bateu em alguém. Dano 0 significa que o golpe errou.
    public event Action<RemoteEntity?, RemoteEntity, int>? EntityAttacked;

    public event Action<RemoteEntity, RemoteEntity?>? EntityDied;

    public event Action<RemoteEntity>? EntityRespawned;

    public event Action<AttackRejection>? AttackRejected;

    public RemoteEntity? Find(uint id) => _entities.TryGetValue(id, out var entity) ? entity : null;

    public RemoteEntity? Local => LocalEntityId == 0 ? null : Find(LocalEntityId);

    /// S_MAP_INFO: u16 largura, u16 altura, string nome.
    public bool HandleMapInfo(ref PacketReader reader)
    {
        var width = reader.ReadU16();
        var height = reader.ReadU16();
        var name = reader.ReadString();

        // Um mapa de dimensão zero ou absurda não é payload válido; recusar
        // aqui evita alocar um array gigante a pedido do servidor.
        if (reader.Failed || width == 0 || height == 0 || width > 1024 || height > 1024)
        {
            return false;
        }

        Map.Begin(name, width, height);
        return true;
    }

    /// S_MAP_ROW: u16 y, u16 n, u8 células[n].
    public bool HandleMapRow(ref PacketReader reader)
    {
        var y = reader.ReadU16();
        var count = reader.ReadU16();
        if (reader.Failed || count != Map.Width || count > reader.Remaining)
        {
            return false;
        }

        var cells = new byte[count];
        for (var i = 0; i < count; i++)
        {
            cells[i] = reader.ReadU8();
        }
        if (reader.Failed || !Map.SetRow(y, cells))
        {
            return false;
        }

        if (Map.Complete)
        {
            MapReady?.Invoke();
        }
        return true;
    }

    /// S_ENTER_WORLD: u32 id, u16 x, u16 y, u8 dir, string mapa, string nome.
    public bool HandleEnterWorld(ref PacketReader reader)
    {
        var id = reader.ReadU32();
        var x = reader.ReadU16();
        var y = reader.ReadU16();
        var facing = reader.ReadU8();
        var hp = reader.ReadI32();
        var maxHp = reader.ReadI32();
        var map = reader.ReadString();
        var name = reader.ReadString();

        if (reader.Failed || id == 0 || facing > (byte)Direction.SouthEast)
        {
            return false;
        }

        ClearEntities();
        LocalEntityId = id;
        MapName = map;

        var entity = new RemoteEntity(id, name, x, y, (Direction)facing, EntityKind.Player, hp,
                                      maxHp);
        _entities[id] = entity;

        EnteredWorld?.Invoke(entity);
        return true;
    }

    /// S_ENTITY_SPAWN: u32 id, u16 x, u16 y, u8 dir, string nome.
    public bool HandleEntitySpawn(ref PacketReader reader)
    {
        var id = reader.ReadU32();
        var x = reader.ReadU16();
        var y = reader.ReadU16();
        var facing = reader.ReadU8();
        var kind = reader.ReadU8();
        var hp = reader.ReadI32();
        var maxHp = reader.ReadI32();
        var name = reader.ReadString();

        if (reader.Failed || id == 0 || facing > (byte)Direction.SouthEast
            || kind > (byte)EntityKind.Monster)
        {
            return false;
        }

        // Spawn repetido do mesmo id não deve duplicar o nó na cena; o
        // servidor é a fonte, então a última informação vence.
        if (_entities.TryGetValue(id, out var existing))
        {
            existing.CellX = x;
            existing.CellY = y;
            existing.Facing = (Direction)facing;
            existing.Hp = hp;
            existing.MaxHp = maxHp;
            EntityStopped?.Invoke(existing);
            return true;
        }

        var entity = new RemoteEntity(id, name, x, y, (Direction)facing, (EntityKind)kind, hp,
                                      maxHp);
        _entities[id] = entity;
        EntitySpawned?.Invoke(entity);
        return true;
    }

    /// S_ENTITY_DESPAWN: u32 id.
    public bool HandleEntityDespawn(ref PacketReader reader)
    {
        var id = reader.ReadU32();
        if (reader.Failed)
        {
            return false;
        }

        // Despawn de id desconhecido não é erro de protocolo: pode ser uma
        // entidade que nunca chegamos a ver. Ignorar é o correto.
        if (_entities.Remove(id, out var entity))
        {
            EntityDespawned?.Invoke(entity);
        }
        return true;
    }

    /// S_ENTITY_WALK: u32 id, u16 x, u16 y, u16 ms/célula, u8 n, u8 direções[n].
    public bool HandleEntityWalk(ref PacketReader reader)
    {
        var id = reader.ReadU32();
        var x = reader.ReadU16();
        var y = reader.ReadU16();
        var speed = reader.ReadU16();
        var count = reader.ReadU8();

        if (reader.Failed || speed == 0)
        {
            return false;
        }

        var path = new Direction[count];
        for (var i = 0; i < count; i++)
        {
            var raw = reader.ReadU8();
            if (raw > (byte)Direction.SouthEast)
            {
                return false;
            }
            path[i] = (Direction)raw;
        }
        if (reader.Failed)
        {
            return false;
        }

        if (!_entities.TryGetValue(id, out var entity))
        {
            // Trajeto de entidade desconhecida: pode ser uma que ainda não
            // chegou. Ignorar é melhor que derrubar a conexão.
            return true;
        }

        // A origem do trajeto é a posição autoritativa; o client se corrige
        // por ela em vez de confiar na sua própria interpolação.
        entity.CellX = x;
        entity.CellY = y;

        int destX = x;
        int destY = y;
        foreach (var dir in path)
        {
            destX += DirectionDeltas[(int)dir].X;
            destY += DirectionDeltas[(int)dir].Y;
        }

        // A célula autoritativa passa a ser o fim do trajeto: o servidor já
        // decidiu que a entidade chega lá, e qualquer desvio no meio vem como
        // um S_ENTITY_STOP explícito, que corrige. A posição que o jogador vê
        // é a interpolada no nó da cena, não esta.
        entity.CellX = destX;
        entity.CellY = destY;
        if (count > 0)
        {
            entity.Facing = path[count - 1];
        }

        EntityWalkStarted?.Invoke(new WalkOrder(id, x, y, destX, destY, speed, path), entity);
        return true;
    }

    /// S_ENTITY_STOP: u32 id, u16 x, u16 y, u8 dir.
    public bool HandleEntityStop(ref PacketReader reader)
    {
        var id = reader.ReadU32();
        var x = reader.ReadU16();
        var y = reader.ReadU16();
        var facing = reader.ReadU8();

        if (reader.Failed || facing > (byte)Direction.SouthEast)
        {
            return false;
        }

        if (!_entities.TryGetValue(id, out var entity))
        {
            return true;
        }

        entity.CellX = x;
        entity.CellY = y;
        entity.Facing = (Direction)facing;
        EntityStopped?.Invoke(entity);
        return true;
    }

    /// S_ENTITY_ATTACK: u32 atacante, u32 alvo, i32 dano, i32 vida restante.
    public bool HandleEntityAttack(ref PacketReader reader)
    {
        var sourceId = reader.ReadU32();
        var targetId = reader.ReadU32();
        var damage = reader.ReadI32();
        var hp = reader.ReadI32();

        if (reader.Failed)
        {
            return false;
        }

        if (!_entities.TryGetValue(targetId, out var target))
        {
            return true; // alvo que nunca chegamos a ver
        }

        // A vida vem no pacote em vez de ser acumulada aqui: se um pacote de
        // dano se perdesse, uma barra calculada por subtração ficaria errada
        // para sempre.
        target.Hp = hp;

        _entities.TryGetValue(sourceId, out var source);
        EntityAttacked?.Invoke(source, target, damage);
        return true;
    }

    /// S_ENTITY_DIED: u32 morto, u32 matador.
    public bool HandleEntityDied(ref PacketReader reader)
    {
        var deadId = reader.ReadU32();
        var killerId = reader.ReadU32();
        if (reader.Failed)
        {
            return false;
        }

        if (!_entities.TryGetValue(deadId, out var dead))
        {
            return true;
        }

        dead.Hp = 0;
        _entities.TryGetValue(killerId, out var killer);
        EntityDied?.Invoke(dead, killer);
        return true;
    }

    /// S_ENTITY_RESPAWN: u32 id, u16 x, u16 y, i32 vida.
    public bool HandleEntityRespawn(ref PacketReader reader)
    {
        var id = reader.ReadU32();
        var x = reader.ReadU16();
        var y = reader.ReadU16();
        var hp = reader.ReadI32();

        if (reader.Failed)
        {
            return false;
        }

        if (!_entities.TryGetValue(id, out var entity))
        {
            return true;
        }

        entity.CellX = x;
        entity.CellY = y;
        entity.Hp = hp;
        EntityRespawned?.Invoke(entity);
        return true;
    }

    /// S_ATTACK_REJECTED: u8 motivo.
    public bool HandleAttackRejected(ref PacketReader reader)
    {
        var reason = reader.ReadU8();
        if (reader.Failed)
        {
            return false;
        }
        AttackRejected?.Invoke((AttackRejection)reason);
        return true;
    }

    /// S_LOGIN_FAILED: u8 motivo.
    public bool HandleLoginFailed(ref PacketReader reader)
    {
        var reason = reader.ReadU8();
        if (reader.Failed)
        {
            return false;
        }
        LoginRejected?.Invoke((LoginFailure)reason);
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
