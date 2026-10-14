using System;
using System.Collections.Generic;
using Godot;

namespace Backlands.Art;

/// <summary>
/// Carrega uma folha de sprites da pasta <c>res://spritesheets/</c>.
///
/// A folha é uma grade de quadros 32×48 separada por linhas de #e0e0e0, com
/// #9c8b93 como fundo (cor-chave, não alpha). A geometria da grade é
/// DETECTADA, não presumida: o carregador procura linhas e colunas inteiramente
/// da cor de grade. Assim funciona com separador de 1 px ou 2 px, com ou sem
/// borda externa.
///
/// O <see cref="SpriteFrames"/> resultante é compartilhado entre todas as
/// entidades do mesmo tipo — construir um por entidade alocaria dezenas de
/// recursos por monstro.
/// </summary>
public sealed class SpriteSheet
{
    private static readonly Dictionary<string, SpriteSheet?> Cache = new();

    private SpriteSheet(string key, SpriteFrames frames, int columns, int rows)
    {
        Key = key;
        Frames = frames;
        Columns = columns;
        Rows = rows;
    }

    public string Key { get; }

    public SpriteFrames Frames { get; }

    public int Columns { get; }

    public int Rows { get; }

    /// Carrega (ou devolve do cache) a folha de uma chave. Devolve null quando
    /// o arquivo não existe ou é inválido — quem chama cai no sprite
    /// procedural, para o jogo nunca parar por falta de arte.
    public static SpriteSheet? Load(string key)
    {
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        SpriteSheet? sheet = null;
        try
        {
            sheet = Build(key);
            if (sheet == null)
            {
                // Arte que ainda não existe é situação esperada, não defeito:
                // avisa uma vez por chave (o cache garante isso) e sem elevar a
                // severidade, senão cada entidade na tela despeja um rastro de
                // pilha e o log do playtest fica ilegível.
                GD.Print($"[sprite] '{key}' não encontrada em {PathFor(key)}, usando placeholder");
            }
        }
        catch (Exception error)
        {
            // Folha presente porém inválida é defeito de verdade: a arte existe
            // mas o recorte não bate, e isso precisa aparecer alto.
            GD.PushWarning($"[sprite] '{key}' é inválida: {error.Message}");
        }

        Cache[key] = sheet;
        return sheet;
    }

    /// Esquece tudo que foi carregado. Necessário depois de recompilar o C# no
    /// editor, senão a arte trocada não recarrega.
    public static void ClearCache() => Cache.Clear();

    public static string PathFor(string key) => $"res://spritesheets/{key}.png";

    private static SpriteSheet? Build(string key)
    {
        var path = PathFor(key);
        if (!ResourceLoader.Exists(path) && !FileAccess.FileExists(path))
        {
            return null;
        }

        var image = LoadImage(path);
        if (image == null)
        {
            return null;
        }

        var columns = DetectBands(image, horizontal: true, SpriteLayout.FrameWidth);
        var rows = DetectBands(image, horizontal: false, SpriteLayout.FrameHeight);

        if (columns.Count == 0 || rows.Count == 0)
        {
            throw new InvalidOperationException(
                $"nenhuma grade de {SpriteLayout.FrameWidth}x{SpriteLayout.FrameHeight} "
                + $"encontrada em {image.GetWidth()}x{image.GetHeight()} — a folha usa linhas "
                + "#e0e0e0 entre os quadros?");
        }

        // A folha pode ser MAIOR que o mapa de células (a arte costuma trazer
        // quadros extras, retratos e paleta); o que não está no mapa é ignorado.
        // O que não se tolera é a grade não bater, porque uma folha deslocada
        // desenha uma pose plausível porém errada em todas as direções.

        ApplyColorKey(image);
        ApplyInnerOutline(image, rows, columns);
        var texture = ImageTexture.CreateFromImage(image);

        var frames = new SpriteFrames();
        // SpriteFrames nasce com uma animação "default" que não usamos.
        frames.RemoveAnimation("default");

        var missing = new List<string>();
        foreach (var (state, pose, cells) in SpriteLayout.All())
        {
            var name = SpriteLayout.AnimationName(state, pose);
            frames.AddAnimation(name);

            // Repetem enquanto o estado durar; os demais tocam uma vez e
            // seguram o último quadro, que é o comportamento do Ragnarok.
            var loops = state is SpriteState.Idle or SpriteState.Walk;
            frames.SetAnimationLoopMode(
                name, loops ? SpriteFrames.LoopMode.Linear : SpriteFrames.LoopMode.None);

            // Velocidade base 1: o ritmo real vem do SpeedScale de cada
            // entidade, porque este recurso é compartilhado entre todas elas.
            frames.SetAnimationSpeed(name, 1.0);

            var complete = true;
            foreach (var cell in cells)
            {
                if (cell.Row > rows.Count || cell.Column > columns.Count)
                {
                    complete = false;
                    break;
                }

                var region = new Rect2(columns[cell.Column - 1].Start, rows[cell.Row - 1].Start,
                                       SpriteLayout.FrameWidth, SpriteLayout.FrameHeight);
                var atlas = new AtlasTexture
                {
                    Atlas = texture,
                    Region = region,
                    // Margem precisa ser zero: AtlasTexture.GetWidth soma a
                    // margem, e o AnimatedSprite3D dimensiona o quadro por ela.
                    Margin = new Rect2(0, 0, 0, 0),
                    FilterClip = false,
                };
                frames.AddFrame(name, atlas);
            }

            if (!complete)
            {
                missing.Add(name);
            }
        }

        // Uma folha com menos linhas que o mapa é aceita: o que falta cai para
        // Idle. Dá para começar só com a primeira linha e crescer depois.
        foreach (var name in missing)
        {
            var fallbackPose = name.EndsWith("back", StringComparison.Ordinal)
                ? SpritePose.Back
                : SpritePose.Front;
            var fallback = SpriteLayout.AnimationName(SpriteState.Idle, fallbackPose);

            frames.Clear(name);
            for (var i = 0; i < frames.GetFrameCount(fallback); i++)
            {
                frames.AddFrame(name, frames.GetFrameTexture(fallback, i));
            }
        }

        if (missing.Count > 0)
        {
            GD.PushWarning(
                $"[sprite] '{key}': {missing.Count} animação(ões) fora da folha, caindo para idle: "
                + string.Join(", ", missing));
        }

        GD.Print($"[sprite] '{key}' carregada: {columns.Count}x{rows.Count} quadros");
        return new SpriteSheet(key, frames, columns.Count, rows.Count);
    }

    private static Image? LoadImage(string path)
    {
        // Carrega pelo FileAccess e não por ResourceLoader: a textura importada
        // viria com as configurações do editor, enquanto aqui precisamos dos
        // pixels crus para achar a grade e aplicar a cor-chave.
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushWarning($"[sprite] não foi possível abrir {path}");
            return null;
        }

        var image = new Image();
        if (image.LoadPngFromBuffer(file.GetBuffer((long)file.GetLength())) != Error.Ok)
        {
            GD.PushWarning($"[sprite] {path} não é um PNG válido");
            return null;
        }

        if (image.GetFormat() != Image.Format.Rgba8)
        {
            image.Convert(Image.Format.Rgba8);
        }
        return image;
    }

    private readonly record struct Band(int Start, int Size);

    /// <summary>
    /// Acha onde começa cada quadro, por PERIODICIDADE das linhas de grade.
    ///
    /// Exigir uma linha 100% da cor de grade não funciona com arte de verdade:
    /// os sprites transbordam da célula e cortam o separador em vários pontos.
    /// Em vez disso, cada linha recebe uma nota (fração de pixels da cor de
    /// grade) e procuramos o passo e a fase que maximizam a nota MÉDIA sobre
    /// todos os separadores — alguns separadores sujos não derrubam a média.
    /// </summary>
    private static List<Band> DetectBands(Image image, bool horizontal, int frameSize)
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
                if (Matches(color, SpriteLayout.GridColor))
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

    private static void ApplyColorKey(Image image)
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
    private static void ApplyInnerOutline(Image image, List<Band> rows, List<Band> columns)
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
    private static bool Matches(Color a, Color b)
    {
        const float tolerance = 2.0f / 255.0f;
        return a.A > 0.0f
            && Mathf.Abs(a.R - b.R) <= tolerance
            && Mathf.Abs(a.G - b.G) <= tolerance
            && Mathf.Abs(a.B - b.B) <= tolerance;
    }
}
