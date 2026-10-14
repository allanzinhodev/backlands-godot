using System;
using System.Collections.Generic;
using System.Text;

namespace Backlands.Net;

/// <summary>
/// Monta um frame completo (header + payload), espelhando
/// server/src/protocol/PacketWriter.hpp. O campo length só é conhecido no fim, então
/// os 4 bytes do header são reservados na construção e preenchidos em
/// <see cref="Finish"/>.
/// </summary>
public sealed class PacketWriter
{
    private readonly List<byte> _buffer = new(64);

    public PacketWriter(ClientOpcode opcode)
    {
        WriteU16((ushort)opcode);
        WriteU16(0); // placeholder do length
    }

    public void WriteU8(byte value) => _buffer.Add(value);

    public void WriteU16(ushort value)
    {
        _buffer.Add((byte)(value & 0xFF));
        _buffer.Add((byte)((value >> 8) & 0xFF));
    }

    public void WriteU32(uint value)
    {
        for (var i = 0; i < 4; i++)
        {
            _buffer.Add((byte)((value >> (8 * i)) & 0xFF));
        }
    }

    public void WriteU64(ulong value)
    {
        for (var i = 0; i < 8; i++)
        {
            _buffer.Add((byte)((value >> (8 * i)) & 0xFF));
        }
    }

    public void WriteI8(sbyte value) => WriteU8((byte)value);

    public void WriteI16(short value) => WriteU16((ushort)value);

    public void WriteI32(int value) => WriteU32((uint)value);

    public void WriteF32(float value) => WriteU32(BitConverter.SingleToUInt32Bits(value));

    /// Truncada em MaxStringLength; o contrário permitiria estourar o frame.
    public void WriteString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var length = Math.Min(bytes.Length, Protocol.MaxStringLength);
        WriteU16((ushort)length);
        for (var i = 0; i < length; i++)
        {
            _buffer.Add(bytes[i]);
        }
    }

    /// Preenche o header com o tamanho final e devolve o frame pronto.
    public byte[] Finish()
    {
        if (_buffer.Count > Protocol.MaxPacketSize)
        {
            throw new InvalidOperationException(
                $"frame de {_buffer.Count} bytes excede MaxPacketSize ({Protocol.MaxPacketSize})");
        }

        var length = (ushort)_buffer.Count;
        _buffer[2] = (byte)(length & 0xFF);
        _buffer[3] = (byte)((length >> 8) & 0xFF);
        return _buffer.ToArray();
    }
}
