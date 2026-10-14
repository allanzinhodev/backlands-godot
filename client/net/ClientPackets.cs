namespace Backlands.Net;

/// <summary>
/// Monta os pacotes que o client envia, um método por opcode.
///
/// O formato de cada pacote mora aqui e não em quem pede: assim o layout de
/// um C_* fica num lugar só, ao lado do contrato em docs/protocol.md, e quem
/// joga só diz O QUE quer.
/// </summary>
public static class ClientPackets
{
    /// C_PING: u32 nonce.
    public static PacketWriter Ping(uint nonce)
    {
        var writer = new PacketWriter(ClientOpcode.Ping);
        writer.WriteU32(nonce);
        return writer;
    }

    /// C_LOGIN: u16 versão do protocolo, string nome.
    public static PacketWriter Login(string characterName)
    {
        var writer = new PacketWriter(ClientOpcode.Login);
        writer.WriteU16(Protocol.Version);
        writer.WriteString(characterName);
        return writer;
    }

    /// C_WALK_TO: u16 x, u16 y.
    public static PacketWriter WalkTo(ushort cellX, ushort cellY)
    {
        var writer = new PacketWriter(ClientOpcode.WalkTo);
        writer.WriteU16(cellX);
        writer.WriteU16(cellY);
        return writer;
    }

    /// C_ATTACK: u32 alvo.
    public static PacketWriter Attack(uint targetId)
    {
        var writer = new PacketWriter(ClientOpcode.Attack);
        writer.WriteU32(targetId);
        return writer;
    }
}
