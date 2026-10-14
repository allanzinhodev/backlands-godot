namespace Backlands.Model;

/// Regras de nome aplicadas pelo servidor (server/src/game/Entity.hpp).
/// Replicadas aqui só para dar erro na UI antes de gastar um round-trip —
/// a validação que vale é a do servidor.
public static class CharacterName
{
    public const int MinLength = 3;

    public const int MaxLength = 16;

    public static bool IsValid(string name)
    {
        if (name.Length < MinLength || name.Length > MaxLength)
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
