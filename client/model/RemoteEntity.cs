namespace Backlands.Model;

/// Uma entidade como o client a conhece. A verdade é sempre do servidor:
/// o client só guarda o que recebeu.
public sealed class RemoteEntity
{
    public RemoteEntity(uint id, string name, string sprite, int cellX, int cellY,
                        Direction facing, EntityKind kind, int hp, int maxHp)
    {
        Id = id;
        Name = name;
        Sprite = sprite;
        CellX = cellX;
        CellY = cellY;
        Facing = facing;
        Kind = kind;
        Hp = hp;
        MaxHp = maxHp;
    }

    public uint Id { get; }

    public string Name { get; }

    /// Chave da folha de sprites, vinda do servidor. Separada do nome de
    /// exibição de propósito: nome é texto do usuário e colidiria entre
    /// jogador e monstro.
    public string Sprite { get; }

    public EntityKind Kind { get; }

    public int Hp { get; internal set; }

    public int MaxHp { get; internal set; }

    public bool Alive => Hp > 0;

    /// Fração de vida em [0, 1], para a barra. MaxHp zero nunca deveria vir do
    /// servidor, mas dividir por ele travaria o client.
    public float HpFraction => MaxHp <= 0 ? 0.0f : System.Math.Clamp((float)Hp / MaxHp, 0f, 1f);

    /// Célula autoritativa: onde o servidor diz que a entidade está. A posição
    /// visual interpolada vive no nó da cena, não aqui.
    public int CellX { get; internal set; }

    public int CellY { get; internal set; }

    /// Direção em repouso, dita pelo servidor em spawn, parada, golpe e
    /// respawn. NÃO é a direção de render durante uma caminhada — essa vem do
    /// trecho que está sendo percorrido, e vive no nó da cena.
    public Direction Facing { get; internal set; }
}
