namespace Backlands.Model;

/// <summary>
/// As mensagens do servidor já decodificadas, uma por pacote S_* que o
/// <see cref="WorldState"/> consome.
///
/// Existem para separar o FIO da REGRA: quem lê bytes (a camada de rede) só
/// produz estes valores, e o espelho do mundo só os aplica. Assim o espelho
/// pode ser exercitado sem montar pacote nenhum, e um campo novo no fio muda o
/// leitor sem tocar no estado.
/// </summary>
public static class ServerEvents
{
    public readonly record struct MapInfo(int Width, int Height, string Name);

    public readonly record struct MapRow(int Y, byte[] Cells);

    public readonly record struct EnterWorld(
        uint Id, int X, int Y, Direction Facing, int Hp, int MaxHp,
        string Sprite, string Map, string Name);

    public readonly record struct EntitySpawn(
        uint Id, int X, int Y, Direction Facing, EntityKind Kind, int Hp, int MaxHp,
        string Sprite, string Name);

    public readonly record struct EntityDespawn(uint Id);

    public readonly record struct EntityWalk(
        uint Id, int X, int Y, int SpeedMsPerCell, Direction[] Path);

    public readonly record struct EntityStop(uint Id, int X, int Y, Direction Facing);

    public readonly record struct EntityAttack(
        uint SourceId, uint TargetId, int SourceX, int SourceY, Direction SourceFacing,
        int MotionMs, int Damage, int Hp);

    public readonly record struct EntityDied(uint DeadId, uint KillerId, int X, int Y);

    public readonly record struct EntityRespawn(uint Id, int X, int Y, Direction Facing, int Hp);

    public readonly record struct AttackRejected(AttackRejection Reason);

    public readonly record struct LoginFailed(LoginFailure Reason);
}
