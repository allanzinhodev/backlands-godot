namespace Backlands.Scene;

/// <summary>
/// Trava a animação para a captura de tela seguir comparável entre
/// execuções do playtest.
///
/// Um interruptor só, lido por tudo que anima — personagens, efeitos e os
/// shaders do mapa —, em vez de cada um consultar a linha de comando por
/// conta própria. Quem o liga é a raiz da cena, ANTES de qualquer mapa,
/// entidade ou efeito existir.
/// </summary>
public static class AnimationFreeze
{
    public static bool Enabled { get; set; }
}
