using System.Collections.Generic;
using Godot;

namespace Backlands.Art;

/// <summary>
/// Acha a grade de quadros de uma folha: onde começa cada coluna e cada linha.
///
/// Separado do carregador porque é a parte delicada — tolera arte suja,
/// separador de 1 ou 2 px e folha sem grade — e se lê melhor sozinha.
/// </summary>
internal static class SpriteSheetGrid
{
    public readonly record struct Band(int Start, int Size);

    /// <summary>
    /// Acha onde começa cada quadro, por PERIODICIDADE das linhas de grade.
    ///
    /// Exigir uma linha 100% da cor de grade não funciona com arte de verdade:
    /// os sprites transbordam da célula e cortam o separador em vários pontos.
    /// Em vez disso, cada linha recebe uma nota (fração de pixels da cor de
    /// grade) e procuramos o passo e a fase que maximizam a nota MÉDIA sobre
    /// todos os separadores — alguns separadores sujos não derrubam a média.
    /// </summary>
    public static List<Band> DetectBands(Image image, bool horizontal, int frameSize)
    {
        var length = horizontal ? image.GetWidth() : image.GetHeight();
        var across = horizontal ? image.GetHeight() : image.GetWidth();

        // Nota de cada linha: quanto dela é da cor de grade.
        var scores = new float[length];
        for (var i = 0; i < length; i++)
        {
            var hits = 0;
            for (var j = 0; j < across; j++)
            {
                var color = horizontal ? image.GetPixel(i, j) : image.GetPixel(j, i);
                if (SpriteSheetPixels.Matches(color, SpriteLayout.GridColor))
                {
                    hits++;
                }
            }
            scores[i] = (float)hits / across;
        }

        var bestScore = 0.0f;
        var bestSeparator = -1;
        var bestPhase = -1;

        // Separador de 1 px é o caso comum; 2 px aparece em folhas feitas à
        // mão; 0 px é a folha sem grade nenhuma.
        foreach (var separator in new[] { 1, 2, 0 })
        {
            var pitch = frameSize + separator;
            if (pitch <= 0 || pitch > length)
            {
                continue;
            }

            for (var phase = 0; phase < pitch; phase++)
            {
                var total = 0.0f;
                var count = 0;
                for (var at = phase; at + frameSize <= length; at += pitch)
                {
                    // Com separador 0 não há linha para medir: a nota é neutra
                    // e só a geometria decide.
                    total += separator == 0 ? 0.5f : scores[at];
                    count++;
                }

                if (count == 0)
                {
                    continue;
                }

                var average = total / count;
                if (average > bestScore)
                {
                    bestScore = average;
                    bestSeparator = separator;
                    bestPhase = phase;
                }
            }
        }

        var bands = new List<Band>();
        if (bestPhase < 0 || bestScore < 0.4f)
        {
            return bands; // nenhuma grade reconhecível
        }

        var step = frameSize + bestSeparator;
        for (var start = bestPhase + bestSeparator; start + frameSize <= length; start += step)
        {
            bands.Add(new Band(start, frameSize));
        }
        return bands;
    }
}
