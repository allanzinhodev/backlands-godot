namespace Backlands.Net;

/// Client -> server (0x0001-0x0FFF).
public enum ClientOpcode : ushort
{
    Ping = 0x0001,
    Login = 0x0002,
    WalkTo = 0x0003,
    Attack = 0x0004,
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
    EntityAttack = 0x100A,
    EntityDied = 0x100B,
    EntityRespawn = 0x100C,
    AttackRejected = 0x100D,
}
