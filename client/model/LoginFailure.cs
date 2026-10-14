namespace Backlands.Model;

/// Motivos de S_LOGIN_FAILED (protocol::LoginFailure no servidor,
/// server/src/protocol/LoginFailure.hpp).
public enum LoginFailure : byte
{
    None = 0,
    InvalidName = 1,
    NameInUse = 2,
    AlreadySpawned = 3,
    ProtocolMismatch = 4,
}
