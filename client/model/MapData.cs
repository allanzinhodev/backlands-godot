namespace Backlands.Model;

/// <summary>
/// O grid do mapa como o client o recebeu do servidor.
///
/// Não existe um segundo grid: o servidor envia o dele, o client só desenha.
/// É isso que elimina a classe de bug "visual divergir da lógica".
/// </summary>
public sealed class MapData
{
    private byte[] _cells = System.Array.Empty<byte>();

    public string Name { get; private set; } = string.Empty;

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// Quantas linhas já chegaram. O mapa vem uma linha por pacote.
    public int RowsReceived { get; private set; }

    public bool Complete => Width > 0 && Height > 0 && RowsReceived == Height;

    public void Begin(string name, int width, int height)
    {
        Name = name;
        Width = width;
        Height = height;
        RowsReceived = 0;
        _cells = new byte[width * height];
    }

    /// Devolve false se a linha não couber no mapa anunciado — isso é payload
    /// fora do contrato, e o chamador derruba a conexão.
    public bool SetRow(int y, byte[] cells)
    {
        if (Width == 0 || y < 0 || y >= Height || cells.Length != Width)
        {
            return false;
        }

        cells.CopyTo(_cells, y * Width);
        RowsReceived++;
        return true;
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// Fora do mapa devolve uma célula sólida, para o desenho e o cursor
    /// tratarem a borda como o servidor trata.
    public MapCell At(int x, int y) =>
        InBounds(x, y) ? new MapCell(_cells[y * Width + x]) : new MapCell(0);

    public void Clear()
    {
        Name = string.Empty;
        Width = 0;
        Height = 0;
        RowsReceived = 0;
        _cells = System.Array.Empty<byte>();
    }
}
