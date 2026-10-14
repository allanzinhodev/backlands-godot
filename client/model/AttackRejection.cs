namespace Backlands.Model;

/// Por que o servidor recusou um pedido de ataque.
public enum AttackRejection : byte
{
    None = 0,
    NoSuchTarget = 1,
    TargetDead = 2,
    AttackerDead = 3,
    SelfTarget = 4,
    OutOfRange = 5,
    OnCooldown = 6,
}
