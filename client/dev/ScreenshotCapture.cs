using Godot;

namespace Backlands.Dev;

/// <summary>
/// A captura de tela do playtest (--screenshot): uma contagem de quadros e um
/// PNG no fim dela.
///
/// Sem caminho na linha de comando, nada acontece — agendar vira no-op. A
/// contagem é por QUADROS, não por tempo: o que importa é quantas vezes a cena
/// já desenhou, não quanto a máquina demorou.
/// </summary>
public sealed class ScreenshotCapture
{
    private readonly string? _path;
    private readonly int _delayFrames;
    private int _framesUntilScreenshot = -1;

    public ScreenshotCapture(string? path, int delayFrames)
    {
        _path = path;
        _delayFrames = delayFrames;
    }

    /// Se esta execução é de captura.
    public bool Enabled => _path != null;

    /// Recomeça a contagem; a captura sai depois de <c>delayFrames</c> quadros.
    public void Schedule()
    {
        if (_path != null)
        {
            _framesUntilScreenshot = _delayFrames;
        }
    }

    /// Um quadro a menos na contagem; no zero, salva.
    public void Advance(Viewport viewport)
    {
        if (_framesUntilScreenshot > 0)
        {
            _framesUntilScreenshot--;
        }
        else if (_framesUntilScreenshot == 0)
        {
            _framesUntilScreenshot = -1;
            Save(viewport);
        }
    }

    private void Save(Viewport viewport)
    {
        var image = viewport.GetTexture().GetImage();
        var error = image.SavePng(_path);
        GD.Print(error == Error.Ok
            ? $"[teste] screenshot salvo em {_path}"
            : $"[teste] falha ao salvar screenshot: {error}");

        // Não encerra aqui: com vários clientes, sair logo após a própria
        // captura faria cada um fotografar um mundo já esvaziado pelos outros.
        // Quem encerra é o --quit-after da linha de comando.
    }
}
