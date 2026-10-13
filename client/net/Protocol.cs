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

    /// Regras de nome aplicadas pelo servidor (server/src/game/Entity.hpp).
    /// Replicadas aqui só para dar erro na UI antes de gastar um round-trip —
    /// a validação que vale é a do servidor.
    public const int MinNameLength = 3;

    public const int MaxNameLength = 16;

    public static bool IsValidCharacterName(string name)
    {
        if (name.Length < MinNameLength || name.Length > MaxNameLength)
        {
            return false;
        }
        foreach (var c in name)
        {
            var allowed = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_';
            if (!allowed)
            {
                return false;
            }
        }
        return true;
    }
}

/// Client -> server (0x0001-0x0FFF).
public enum ClientOpcode : ushort
{
    Ping = 0x0001,
    Login = 0x0002,
    WalkTo = 0x0003,
}

/// Server -> client (0x1001-0x1FFF).
public enum ServerOpcode : ushort
{
    Pong = 0x1001,
    EnterWorld = 0x1002,
    LoginFailed = 0x1003,
    EntitySpawn = 0x1004,
    EntityDespawn = 0x1005,
    MapInfo = 0x1006,
    MapRow = 0x1007,
    EntityWalk = 0x1008,
    EntityStop = 0x1009,
}

/// Tipo de terreno de uma célula, nos bits altos do byte do grid.
public enum Terrain : byte
{
    Grass = 0,
    Water = 1,
    Rock = 2,
    Bridge = 3,
    Dirt = 4,
}

/// <summary>
/// Decodifica o byte de célula enviado pelo servidor
/// (ver server/src/game/MapGrid.hpp).
///
/// O client usa estas flags para DESENHAR e para dar feedback de cursor.
/// Nunca para decidir passagem: essa decisão é sempre do servidor.
/// </summary>
public readonly struct MapCell
{
    private const byte WalkableBit = 0x01;
    private const byte BlocksLosBit = 0x02;
    private const int TerrainShift = 4;

    public MapCell(byte raw)
    {
        Raw = raw;
    }

    public byte Raw { get; }

    public bool Walkable => (Raw & WalkableBit) != 0;

    public bool BlocksLineOfSight => (Raw & BlocksLosBit) != 0;

    public Terrain Terrain => (Terrain)(Raw >> TerrainShift);
}

/// Motivos de S_LOGIN_FAILED (EntityRegistry::SpawnError no servidor).
public enum LoginFailure : byte
{
    None = 0,
    InvalidName = 1,
    NameInUse = 2,
    AlreadySpawned = 3,
}

public static class LoginFailureText
{
    /// Mensagem para a UI. Um valor desconhecido não pode virar exceção: o
    /// servidor pode ganhar motivos novos antes do client ser atualizado.
    public static string Describe(LoginFailure reason) => reason switch
    {
        LoginFailure.InvalidName =>
            $"Nome inválido. Use {Protocol.MinNameLength} a {Protocol.MaxNameLength} caracteres, "
            + "apenas letras, números e _",
        LoginFailure.NameInUse => "Esse nome já está em uso.",
        LoginFailure.AlreadySpawned => "Esta conexão já entrou no mundo.",
        _ => $"Login recusado pelo servidor (motivo {(byte)reason}).",
    };
}

/// As 8 direções, na ordem usada no fio (0 = sul, sentido anti-horário).
public enum Direction : byte
{
    South = 0,
    SouthWest = 1,
    West = 2,
    NorthWest = 3,
    North = 4,
    NorthEast = 5,
    East = 6,
    SouthEast = 7,
}
