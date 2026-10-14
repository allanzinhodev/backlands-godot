using System;
using Godot;

namespace Backlands.Ui;

/// A folha de ícones de habilidade, carregada uma vez e recortada por célula.
public sealed class IconSheet
{
    /// A folha de ícones tem 14 colunas por 10 linhas. O tamanho da célula não
    /// é inteiro (1536/14), então a região de cada ícone é calculada por
    /// arredondamento das bordas, em vez de multiplicar um tamanho fixo — o
    /// que acumularia erro e cortaria os ícones da direita.
    private const string IconSheetPath = "res://icons/spell_icons.png";
    private const int IconColumns = 14;
    private const int IconRows = 10;

    private readonly Texture2D _sheet;

    private IconSheet(Texture2D sheet)
    {
        _sheet = sheet;
    }

    /// A folha, ou null quando ela falta ou não é PNG — a barra segue sem ícones.
    public static IconSheet? Load()
    {
        if (!FileAccess.FileExists(IconSheetPath))
        {
            GD.Print($"[skills] folha de ícones ausente em {IconSheetPath}");
            return null;
        }

        using var file = FileAccess.Open(IconSheetPath, FileAccess.ModeFlags.Read);
        var image = new Image();
        if (image.LoadPngFromBuffer(file.GetBuffer((long)file.GetLength())) != Error.Ok)
        {
            GD.PushWarning("[skills] folha de ícones não é um PNG válido");
            return null;
        }
        return new IconSheet(ImageTexture.CreateFromImage(image));
    }

    /// O ícone da célula (linha, coluna), 1-indexadas como no mapa da folha.
    public Texture2D Icon(int iconRow, int iconColumn)
    {
        var width = (float)_sheet.GetWidth() / IconColumns;
        var height = (float)_sheet.GetHeight() / IconRows;

        var column = Math.Clamp(iconColumn - 1, 0, IconColumns - 1);
        var row = Math.Clamp(iconRow - 1, 0, IconRows - 1);

        var left = Mathf.RoundToInt(column * width);
        var top = Mathf.RoundToInt(row * height);
        var right = Mathf.RoundToInt((column + 1) * width);
        var bottom = Mathf.RoundToInt((row + 1) * height);

        return new AtlasTexture
        {
            Atlas = _sheet,
            Region = new Rect2(left, top, right - left, bottom - top),
            Margin = new Rect2(0, 0, 0, 0),
        };
    }
}
