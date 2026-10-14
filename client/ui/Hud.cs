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
    private const int SideMargin = 12;

    /// Folga entre o texto e a faixa da barra de skills.
    private const int GapAboveSkillBar = 8;

    public Hud()
    {
        Name = "Hud";
        Text = string.Empty;
        Visible = false;

        // Canto INFERIOR esquerdo: o superior é da janela básica. A linha de
        // baixo fica logo acima da faixa da barra de skills — a barra é
        // centralizada, mas a segunda linha da ajuda é larga o bastante para
        // passar por baixo dela se o texto encostasse na borda. Ancorado em
        // baixo e crescendo para CIMA, para a altura do texto decidir o topo.
        AnchorTop = 1.0f;
        AnchorBottom = 1.0f;
        OffsetLeft = SideMargin;
        OffsetTop = -(SkillBar.BottomLine + GapAboveSkillBar);
        OffsetBottom = -(SkillBar.BottomLine + GapAboveSkillBar);
        GrowVertical = GrowDirection.Begin;

        // Sobre o mundo, e não mais sobre o céu escuro do topo: o contorno é
        // o que mantém o texto legível em cima da grama.
        AddThemeColorOverride("font_outline_color", UiStyle.Outline);
        AddThemeConstantOverride("outline_size", 4);
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
