using Backlands.Model;
using Godot;

namespace Backlands.Ui;

/// <summary>
/// A linha de informação no canto da tela: mapa, célula, vida, quantas
/// entidades, alvo e a ajuda dos controles.
///
/// Só formata. Quem decide QUAL célula e QUAL alvo mostrar é quem chama.
/// </summary>
public sealed partial class Hud : Label
{
    public Hud()
    {
        Name = "Hud";
        Text = string.Empty;
        Position = new Vector2(12, 8);
        Visible = false;
    }

    /// <param name="cell">Célula a exibir, ou null quando não há personagem na tela.</param>
    /// <param name="local">O personagem do jogador, ou null antes de entrar.</param>
    /// <param name="targetName">Nome do alvo, ou null quando não há alvo.</param>
    public void Refresh(string mapName, (int X, int Y)? cell, RemoteEntity? local, int entityCount,
                        string? targetName)
    {
        var cellText = cell is { } c ? $"({c.X}, {c.Y})" : "-";
        var hp = local == null ? "-" : $"{local.Hp}/{local.MaxHp}";
        var target = targetName ?? "nenhum";
        Text = $"{mapName}  |  célula {cellText}  |  HP {hp}  |  "
            + $"{entityCount} entidade(s)  |  alvo: {target}\n"
            + "clique no chão para andar, no inimigo para atacar e selecionar  |  "
            + "F1-F12 conjuram no alvo  |  botão direito gira a câmera";
    }
}
