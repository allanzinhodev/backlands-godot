using Backlands.Model;

namespace Backlands.Net;

/// <summary>
/// Decodifica os payloads S_* em <see cref="ServerEvents"/>.
///
/// Só o FIO mora aqui: tamanho, bytes e os valores que o contrato proíbe
/// (id zero, direção fora das 8, mapa de dimensão absurda). O que depende do
/// estado do mundo — uma linha que não bate com o mapa anunciado, uma entidade
/// desconhecida — é decidido por quem aplica a mensagem. Devolver false aqui
/// derruba a conexão, exatamente como o handler devolvendo false.
/// </summary>
public static class ServerPacketReader
{
    /// Assinatura comum dos decodificadores, para quem registra um por opcode.
    public delegate bool Decoder<T>(ref PacketReader reader, out T message);

    /// S_PONG: u32 nonce.
    public static bool TryReadPong(ref PacketReader reader, out uint nonce)
    {
        nonce = reader.ReadU32();
        return !reader.Failed;
    }

    /// S_MAP_INFO: u16 largura, u16 altura, string nome.
    public static bool TryReadMapInfo(ref PacketReader reader, out ServerEvents.MapInfo message)
    {
        var width = reader.ReadU16();
        var height = reader.ReadU16();
        var name = reader.ReadString();

        // Um mapa de dimensão zero ou absurda não é payload válido; recusar
        // aqui evita alocar um array gigante a pedido do servidor.
        if (reader.Failed || width == 0 || height == 0 || width > 1024 || height > 1024)
        {
            message = default;
            return false;
        }

        message = new ServerEvents.MapInfo(width, height, name);
        return true;
    }

    /// S_MAP_ROW: u16 y, u16 n, u8 células[n].
    public static bool TryReadMapRow(ref PacketReader reader, out ServerEvents.MapRow message)
    {
        var y = reader.ReadU16();
        var count = reader.ReadU16();
        if (reader.Failed || count > reader.Remaining)
        {
            message = default;
            return false;
        }

        var cells = new byte[count];
        for (var i = 0; i < count; i++)
        {
            cells[i] = reader.ReadU8();
        }
        if (reader.Failed)
        {
            message = default;
            return false;
        }

        message = new ServerEvents.MapRow(y, cells);
        return true;
    }

    /// S_ENTER_WORLD: u32 id, u16 x, u16 y, u8 dir, string mapa, string nome.
    public static bool TryReadEnterWorld(ref PacketReader reader, out ServerEvents.EnterWorld message)
    {
        var id = reader.ReadU32();
        var x = reader.ReadU16();
        var y = reader.ReadU16();
        var facing = reader.ReadU8();
        var hp = reader.ReadI32();
        var maxHp = reader.ReadI32();
        var sprite = reader.ReadString();
        var map = reader.ReadString();
        var name = reader.ReadString();

        if (reader.Failed || id == 0 || facing > (byte)Direction.SouthEast)
        {
            message = default;
            return false;
        }

        message = new ServerEvents.EnterWorld(id, x, y, (Direction)facing, hp, maxHp, sprite, map, name);
        return true;
    }

    /// S_ENTITY_SPAWN: u32 id, u16 x, u16 y, u8 dir, string nome.
    public static bool TryReadEntitySpawn(ref PacketReader reader, out ServerEvents.EntitySpawn message)
    {
        var id = reader.ReadU32();
        var x = reader.ReadU16();
        var y = reader.ReadU16();
        var facing = reader.ReadU8();
        var kind = reader.ReadU8();
        var hp = reader.ReadI32();
        var maxHp = reader.ReadI32();
        var sprite = reader.ReadString();
        var name = reader.ReadString();

        if (reader.Failed || id == 0 || facing > (byte)Direction.SouthEast
            || kind > (byte)EntityKind.Monster)
        {
            message = default;
            return false;
        }

        message = new ServerEvents.EntitySpawn(id, x, y, (Direction)facing, (EntityKind)kind, hp, maxHp,
                                               sprite, name);
        return true;
    }

    /// S_ENTITY_DESPAWN: u32 id.
    public static bool TryReadEntityDespawn(ref PacketReader reader, out ServerEvents.EntityDespawn message)
    {
        var id = reader.ReadU32();
        if (reader.Failed)
        {
            message = default;
            return false;
        }

        message = new ServerEvents.EntityDespawn(id);
        return true;
    }

    /// S_ENTITY_WALK: u32 id, u16 x, u16 y, u16 ms/célula, u8 n, u8 direções[n].
    public static bool TryReadEntityWalk(ref PacketReader reader, out ServerEvents.EntityWalk message)
    {
        var id = reader.ReadU32();
        var x = reader.ReadU16();
        var y = reader.ReadU16();
        var speed = reader.ReadU16();
        var count = reader.ReadU8();

        message = default;
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

        message = new ServerEvents.EntityWalk(id, x, y, speed, path);
        return true;
    }

    /// S_ENTITY_STOP: u32 id, u16 x, u16 y, u8 dir.
    public static bool TryReadEntityStop(ref PacketReader reader, out ServerEvents.EntityStop message)
    {
        var id = reader.ReadU32();
        var x = reader.ReadU16();
        var y = reader.ReadU16();
        var facing = reader.ReadU8();

        if (reader.Failed || facing > (byte)Direction.SouthEast)
        {
            message = default;
            return false;
        }

        message = new ServerEvents.EntityStop(id, x, y, (Direction)facing);
        return true;
    }

    /// S_ENTITY_ATTACK: u32 atacante, u32 alvo, i32 dano, i32 vida restante.
    public static bool TryReadEntityAttack(ref PacketReader reader, out ServerEvents.EntityAttack message)
    {
        var sourceId = reader.ReadU32();
        var targetId = reader.ReadU32();
        var sourceX = reader.ReadU16();
        var sourceY = reader.ReadU16();
        var sourceFacing = reader.ReadU8();
        var motionMs = reader.ReadU16();
        var damage = reader.ReadI32();
        var hp = reader.ReadI32();

        if (reader.Failed || sourceFacing > (byte)Direction.SouthEast)
        {
            message = default;
            return false;
        }

        message = new ServerEvents.EntityAttack(sourceId, targetId, sourceX, sourceY,
                                                (Direction)sourceFacing, motionMs, damage, hp);
        return true;
    }

    /// S_ENTITY_DIED: u32 morto, u32 matador, u16 x, u16 y.
    public static bool TryReadEntityDied(ref PacketReader reader, out ServerEvents.EntityDied message)
    {
        var deadId = reader.ReadU32();
        var killerId = reader.ReadU32();
        var x = reader.ReadU16();
        var y = reader.ReadU16();
        if (reader.Failed)
        {
            message = default;
            return false;
        }

        message = new ServerEvents.EntityDied(deadId, killerId, x, y);
        return true;
    }

    /// S_ENTITY_RESPAWN: u32 id, u16 x, u16 y, u8 dir, i32 vida.
    public static bool TryReadEntityRespawn(ref PacketReader reader, out ServerEvents.EntityRespawn message)
    {
        var id = reader.ReadU32();
        var x = reader.ReadU16();
        var y = reader.ReadU16();
        var facing = reader.ReadU8();
        var hp = reader.ReadI32();

        if (reader.Failed || facing > (byte)Direction.SouthEast)
        {
            message = default;
            return false;
        }

        message = new ServerEvents.EntityRespawn(id, x, y, (Direction)facing, hp);
        return true;
    }

    /// S_ATTACK_REJECTED: u8 motivo.
    public static bool TryReadAttackRejected(ref PacketReader reader, out ServerEvents.AttackRejected message)
    {
        var reason = reader.ReadU8();
        if (reader.Failed)
        {
            message = default;
            return false;
        }

        message = new ServerEvents.AttackRejected((AttackRejection)reason);
        return true;
    }

    /// S_LOGIN_FAILED: u8 motivo.
    public static bool TryReadLoginFailed(ref PacketReader reader, out ServerEvents.LoginFailed message)
    {
        var reason = reader.ReadU8();
        if (reader.Failed)
        {
            message = default;
            return false;
        }

        message = new ServerEvents.LoginFailed((LoginFailure)reason);
        return true;
    }
}
