using System;
using System.Text;

namespace Backlands.Net;

/// <summary>
/// Leitor de payload com bounds-check em toda operação, espelhando
/// server/src/protocol/PacketReader.hpp. Nunca lança: ao faltar byte marca
/// <see cref="Failed"/> e devolve zero, e a falha é persistente.
/// </summary>
public struct PacketReader
{
    private readonly ReadOnlyMemory<byte> _payload;
    private int _cursor;

    public PacketReader(ReadOnlyMemory<byte> payload)
    {
        _payload = payload;
        _cursor = 0;
        Failed = false;
    }

    public bool Failed { get; private set; }

    public bool Ok => !Failed;

    public int Remaining => _payload.Length - _cursor;

    public byte ReadU8()
    {
        if (!Take(1, out var span))
        {
            return 0;
        }
        return span[0];
    }

    public ushort ReadU16()
    {
        if (!Take(2, out var span))
        {
            return 0;
        }
        return (ushort)(span[0] | (span[1] << 8));
    }

    public uint ReadU32()
    {
        if (!Take(4, out var span))
        {
            return 0;
        }
        return (uint)(span[0] | (span[1] << 8) | (span[2] << 16) | (span[3] << 24));
    }

    public ulong ReadU64()
    {
        if (!Take(8, out var span))
        {
            return 0;
        }
        ulong value = 0;
        for (var i = 0; i < 8; i++)
        {
            value |= (ulong)span[i] << (8 * i);
        }
        return value;
    }

    public sbyte ReadI8() => (sbyte)ReadU8();

    public short ReadI16() => (short)ReadU16();

    public int ReadI32() => (int)ReadU32();

    public float ReadF32() => BitConverter.UInt32BitsToSingle(ReadU32());

    /// Strings no fio: uint16 length + bytes UTF-8, sem terminador nulo.
    public string ReadString()
    {
        var length = ReadU16();
        if (Failed)
        {
            return string.Empty;
        }
        if (length > Protocol.MaxStringLength || length > Remaining)
        {
            Failed = true;
            return string.Empty;
        }
        if (!Take(length, out var span))
        {
            return string.Empty;
        }
        return Encoding.UTF8.GetString(span);
    }

    private bool Take(int count, out ReadOnlySpan<byte> span)
    {
        if (Failed || Remaining < count)
        {
            Failed = true;
            span = default;
            return false;
        }
        span = _payload.Span.Slice(_cursor, count);
        _cursor += count;
        return true;
    }
}
