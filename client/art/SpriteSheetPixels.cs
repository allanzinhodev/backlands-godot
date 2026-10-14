using System.Collections.Generic;
using Godot;

namespace Backlands.Art;

/// <summary>
/// O tratamento de pixels que a folha recebe antes de virar textura:
/// cor-chave vira transparência, e cada quadro ganha o contorno interno.
///
/// Mexe na imagem crua, no lugar; o carregador decide a ordem.
/// </summary>
internal static class SpriteSheetPixels
{
    public static void ApplyColorKey(Image image)
    {
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var color = image.GetPixel(x, y);
                if (Matches(color, SpriteLayout.BackgroundColor)
                    || Matches(color, SpriteLayout.GridColor))
                {
                    image.SetPixel(x, y, new Color(0, 0, 0, 0));
                }
            }
        }
    }

    /// <summary>
    /// Escurece a borda interna de cada quadro, dando ao personagem um contorno
    /// de 1 pixel de arte.
    ///
    /// Interno, e não externo: o contorno come o pixel mais externo do desenho
    /// em vez de inchar a silhueta, então o personagem não muda de tamanho nem
    /// invade o quadro vizinho.
    ///
    /// Vai QUADRO A QUADRO, e não na folha inteira, porque fora da célula é
    /// "fora do personagem". A folha pode vir com separador de 0 px entre
    /// células (DetectBands aceita), e aí varrer a imagem toda faria o pixel na
    /// borda enxergar o quadro vizinho como continuação do desenho — e esse
    /// lado ficaria sem contorno.
    ///
    /// Cada célula é visitada UMA vez. Passar duas vezes não engrossaria o
    /// contorno, porque a borda continua sendo a mesma, mas escureceria de novo
    /// os mesmos pixels.
    /// </summary>
    public static void ApplyInnerOutline(Image image, List<SpriteSheetGrid.Band> rows,
                                         List<SpriteSheetGrid.Band> columns)
    {
        var black = new Color(0, 0, 0);

        // Só as células que o mapa usa, sem repetir: a folha costuma trazer
        // retratos e paleta que não são personagem.
        var cells = new HashSet<(int Row, int Column)>();
        foreach (var (_, _, frames) in SpriteLayout.All())
        {
            foreach (var cell in frames)
            {
                if (cell.Row <= rows.Count && cell.Column <= columns.Count)
                {
                    cells.Add((cell.Row, cell.Column));
                }
            }
        }

        foreach (var (row, column) in cells)
        {
            var left = columns[column - 1].Start;
            var top = rows[row - 1].Start;
            var right = left + SpriteLayout.FrameWidth;
            var bottom = top + SpriteLayout.FrameHeight;

            // Só o RGB muda; o alpha fica intacto. É o que permite ler o alpha
            // durante a própria passada sem copiar a célula antes: o que conta
            // como desenho não se altera enquanto escurecemos.
            for (var y = top; y < bottom; y++)
            {
                for (var x = left; x < right; x++)
                {
                    if (image.GetPixel(x, y).A < SpriteLayout.OutlineSolidAlpha)
                    {
                        continue;
                    }

                    var onBorder =
                        IsOutside(image, x - 1, y, left, top, right, bottom)
                        || IsOutside(image, x + 1, y, left, top, right, bottom)
                        || IsOutside(image, x, y - 1, left, top, right, bottom)
                        || IsOutside(image, x, y + 1, left, top, right, bottom);

                    if (onBorder)
                    {
                        var color = image.GetPixel(x, y);
                        var dark = color.Lerp(black, SpriteLayout.OutlineBlend);
                        image.SetPixel(x, y, new Color(dark.R, dark.G, dark.B, color.A));
                    }
                }
            }
        }
    }

    /// Vizinhos em diagonal não contam: eles engrossariam o contorno nos cantos
    /// e em cada degrau de uma borda diagonal, que é a maior parte da silhueta.
    private static bool IsOutside(Image image, int x, int y,
                                  int left, int top, int right, int bottom)
    {
        // Passar da célula é passar do personagem: um desenho que encosta na
        // borda do quadro (os pés, quase sempre) precisa de contorno ali também.
        if (x < left || x >= right || y < top || y >= bottom)
        {
            return true;
        }
        return image.GetPixel(x, y).A < SpriteLayout.OutlineSolidAlpha;
    }

    /// Comparação com tolerância de meio nível de 8 bits: PNG é sem perdas, mas
    /// um editor de imagem pode gravar um valor adjacente ao converter perfil
    /// de cor, e aí a grade não seria detectada sem explicação visível.
    public static bool Matches(Color a, Color b)
    {
        const float tolerance = 2.0f / 255.0f;
        return a.A > 0.0f
            && Mathf.Abs(a.R - b.R) <= tolerance
            && Mathf.Abs(a.G - b.G) <= tolerance
            && Mathf.Abs(a.B - b.B) <= tolerance;
    }
}
