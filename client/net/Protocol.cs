namespace Backlands.Net;

/// <summary>
/// Constantes do framing. Contrato espelhado em docs/protocol.md e em
/// server/src/net/Protocol.hpp — mudou aqui, muda lá.
/// </summary>
public static class Protocol
{
    /// Header: uint16 opcode + uint16 length (tamanho TOTAL), little-endian.
    public const int HeaderSize = 4;

    /// Teto defensivo; frame maior é descartado e a conexão cai.
    public const int MaxPacketSize = 2048;

    public const int MaxPayloadSize = MaxPacketSize - HeaderSize;

    public const int MaxStringLength = 255;
}

/// Client -> server (0x0001-0x0FFF).
public enum ClientOpcode : ushort
{
    Ping = 0x0001,
}

/// Server -> client (0x1001-0x1FFF).
public enum ServerOpcode : ushort
{
    Pong = 0x1001,
}
