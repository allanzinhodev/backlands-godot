namespace Backlands.Net;

/// <summary>
/// Constantes do framing. Contrato espelhado em docs/protocol.md e em
/// server/src/protocol/Protocol.hpp — mudou aqui, muda lá.
/// </summary>
public static class Protocol
{
    /// Header: uint16 opcode + uint16 length (tamanho TOTAL), little-endian.
    public const int HeaderSize = 4;

    /// Teto defensivo; frame maior é descartado e a conexão cai.
    public const int MaxPacketSize = 2048;

    public const int MaxPayloadSize = MaxPacketSize - HeaderSize;

    public const int MaxStringLength = 255;

    /// Versão do contrato; precisa bater com kProtocolVersion no servidor.
    /// Sem isto, um client desatualizado só descobriria o problema como
    /// "payload truncado", que não diz nada a quem está jogando.
    public const ushort Version = 2;
}
