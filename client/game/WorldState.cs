using System;
using System.Collections.Generic;
using Backlands.Net;

namespace Backlands.Game;

/// Uma entidade como o client a conhece. A verdade é sempre do servidor:
/// o client só guarda o que recebeu.
public sealed class RemoteEntity
{
    public RemoteEntity(uint id, string name, int cellX, int cellY, Direction facing)
    {
        Id = id;
        Name = name;
        CellX = cellX;
        CellY = cellY;
        Facing = facing;
    }

    public uint Id { get; }

    public string Name { get; }

    public int CellX { get; internal set; }

    public int CellY { get; internal set; }

    public Direction Facing { get; internal set; }
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
    private readonly Dictionary<uint, RemoteEntity> _entities = new();

    /// Entidade controlada por este client. 0 = ainda não entrou no mundo.
    public uint LocalEntityId { get; private set; }

    public string MapName { get; private set; } = string.Empty;

    public bool InWorld => LocalEntityId != 0;

    public IReadOnlyDictionary<uint, RemoteEntity> Entities => _entities;

    public event Action<RemoteEntity>? EntitySpawned;

    public event Action<RemoteEntity>? EntityDespawned;

    /// Disparado quando o próprio jogador entra: traz a entidade local.
    public event Action<RemoteEntity>? EnteredWorld;

    public event Action<LoginFailure>? LoginRejected;

    public RemoteEntity? Find(uint id) => _entities.TryGetValue(id, out var entity) ? entity : null;

    public RemoteEntity? Local => LocalEntityId == 0 ? null : Find(LocalEntityId);

    /// S_ENTER_WORLD: u32 id, u16 x, u16 y, u8 dir, string mapa, string nome.
    public bool HandleEnterWorld(ref PacketReader reader)
    {
        var id = reader.ReadU32();
        var x = reader.ReadU16();
        var y = reader.ReadU16();
        var facing = reader.ReadU8();
        var map = reader.ReadString();
        var name = reader.ReadString();

        if (reader.Failed || id == 0 || facing > (byte)Direction.SouthEast)
        {
            return false;
        }

        Reset();
        LocalEntityId = id;
        MapName = map;

        var entity = new RemoteEntity(id, name, x, y, (Direction)facing);
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
        var name = reader.ReadString();

        if (reader.Failed || id == 0 || facing > (byte)Direction.SouthEast)
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
            return true;
        }

        var entity = new RemoteEntity(id, name, x, y, (Direction)facing);
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
        foreach (var entity in new List<RemoteEntity>(_entities.Values))
        {
            EntityDespawned?.Invoke(entity);
        }
        _entities.Clear();
        LocalEntityId = 0;
        MapName = string.Empty;
    }
}
