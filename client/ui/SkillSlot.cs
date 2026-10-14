namespace Backlands.Ui;

/// Uma habilidade da barra de testes: o que ela dispara e como aparece.
public readonly record struct SkillSlot(
    string VfxId,
    string Label,
    int IconRow,
    int IconColumn);

/// O conteúdo da barra de testes, separado da barra que o desenha.
public static class TestSkills
{
    /// Os 12 slots de teste. Cada um aponta para um efeito em res://vfx/ e
    /// para um ícone da folha.
    public static readonly SkillSlot[] Slots =
    {
        new("impact_fire_explosion", "Explosão de Fogo", 1, 3),
        new("impact_frost_nova", "Nova de Gelo", 1, 11),
        new("impact_lightning_strike", "Raio", 1, 7),
        new("impact_holy_nova", "Nova Sagrada", 6, 6),
        new("impact_healing_burst", "Cura", 2, 10),
        new("impact_shadow_burst", "Explosão Sombria", 3, 1),
        new("impact_meteor", "Meteoro", 1, 2),
        new("impact_earthquake", "Terremoto", 7, 8),
        new("impact_blade_slash", "Corte", 3, 10),
        new("impact_tornado", "Tornado", 2, 4),
        new("circle_inferno_ring", "Anel Infernal", 7, 3),
        new("circle_healing_spring", "Fonte de Cura", 9, 1),
    };
}
