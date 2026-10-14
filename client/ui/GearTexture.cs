using Godot;

namespace Backlands.Ui;

/// <summary>
/// A engrenagem do botão de opções, desenhada em código: círculo com 8
/// dentes e um furo no meio.
///
/// Não há arte de UI no repositório, e um PNG de 20 px inventado seria mais
/// um arquivo para manter — como o anel de alvo e o bonequinho placeholder,
/// o desenho é uma função. É feita em tamanho maior que o botão e reduzida
/// por ele, para a borda sair suave.
/// </summary>
internal static class GearTexture
{
    public static ImageTexture Create(int size, Color color)
    {
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 0, 0));

        var center = (size - 1) / 2.0f;
        var outer = size * 0.48f;
        var inner = size * 0.37f;
        var hole = size * 0.14f;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var radius = Mathf.Sqrt(dx * dx + dy * dy);
                var angle = Mathf.Atan2(dy, dx);

                // 8 dentes: o raio alterna entre o externo e o interno a cada
                // 22,5°, e cos(8·ângulo) troca de sinal exatamente nesse passo.
                var rim = Mathf.Cos(angle * 8.0f) > 0.0f ? outer : inner;

                // Cobertura aproximada de 1 px nas duas bordas (dente e furo):
                // é o que evita o serrilhado sem precisar de amostragem.
                var coverage = Mathf.Clamp(rim - radius + 0.5f, 0.0f, 1.0f)
                             * Mathf.Clamp(radius - hole + 0.5f, 0.0f, 1.0f);
                if (coverage > 0.0f)
                {
                    image.SetPixel(x, y, new Color(color, coverage));
                }
            }
        }

        return ImageTexture.CreateFromImage(image);
    }
}
