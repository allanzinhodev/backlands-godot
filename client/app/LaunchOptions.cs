using System.Collections.Generic;
using System.Globalization;

namespace Backlands.App;

/// <summary>
/// Os argumentos da linha de comando, lidos uma vez e sem efeito colateral.
///
/// Argumento desconhecido é ignorado, e repetido vale o último que for
/// válido. Aqui só se LÊ: presas e limites (da câmera, por exemplo) ficam com
/// quem usa o valor, para valerem igual no teste e no jogo.
/// </summary>
public sealed class LaunchOptions
{
    // --- Modo de teste automatizado ---------------------------------------
    // Ligado por argumentos depois de `--` na linha de comando, para que o
    // client real (e não um mock) possa ser exercitado sem alguém clicando:
    //   --autologin=Nome       entra assim que a cena carrega
    //   --server=host:porta    sobrescreve o endereço
    //   --walk=x,y             pede uma caminhada após entrar no mundo
    //   --screenshot=caminho   salva um PNG da tela e encerra
    //   --screenshot-delay=N   frames a esperar antes da captura
    //   --freeze-animations    congela a animação, para a captura ser comparável
    //   --sprite-preview=chave confere uma folha de sprites, sem servidor
    //   --cast=N               aciona o slot N da barra ao entrar no mundo
    //   --camera=guinada,incl  começa com a câmera nesse ângulo, em graus
    //   --attack-nearest       ataca o monstro mais próximo assim que o vir
    //   --map-preview          desenha o mapa inicial lido do Lua, sem servidor
    //   --open-options         abre a janela de opções ao entrar no mundo

    public string? AutoLoginName { get; private init; }

    public string? Server { get; private init; }

    public (int X, int Y)? Walk { get; private init; }

    public string? ScreenshotPath { get; private init; }

    public int ScreenshotDelayFrames { get; private init; } = 180;

    public bool FreezeAnimations { get; private init; }

    public string? SpritePreviewKey { get; private init; }

    public bool MapPreview { get; private init; }

    /// Slot de --cast, contando de 1; -1 quando não foi pedido.
    public int CastSlot { get; private init; } = -1;

    /// Guinada e inclinação de --camera, em graus, sem trava.
    public (float Yaw, float Pitch)? CameraAngles { get; private init; }

    /// Zoom de --camera (o terceiro valor), sem trava.
    public float? CameraSize { get; private init; }

    public bool AttackNearest { get; private init; }

    public bool OpenOptions { get; private init; }

    public static LaunchOptions Parse(IEnumerable<string> args)
    {
        string? autoLoginName = null;
        string? server = null;
        (int X, int Y)? walk = null;
        string? screenshotPath = null;
        var screenshotDelayFrames = 180;
        var freezeAnimations = false;
        string? spritePreviewKey = null;
        var mapPreview = false;
        var castSlot = -1;
        (float Yaw, float Pitch)? cameraAngles = null;
        float? cameraSize = null;
        var attackNearest = false;
        var openOptions = false;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--autologin=", System.StringComparison.Ordinal))
            {
                autoLoginName = arg["--autologin=".Length..];
            }
            else if (arg.StartsWith("--server=", System.StringComparison.Ordinal))
            {
                server = arg["--server=".Length..];
            }
            else if (arg.StartsWith("--walk=", System.StringComparison.Ordinal))
            {
                var parts = arg["--walk=".Length..].Split(',');
                if (parts.Length == 2 && int.TryParse(parts[0], out var x)
                    && int.TryParse(parts[1], out var y))
                {
                    walk = (x, y);
                }
            }
            else if (arg.StartsWith("--screenshot=", System.StringComparison.Ordinal))
            {
                screenshotPath = arg["--screenshot=".Length..];
            }
            else if (arg.StartsWith("--sprite-preview=", System.StringComparison.Ordinal))
            {
                spritePreviewKey = arg["--sprite-preview=".Length..];
            }
            else if (arg.StartsWith("--cast=", System.StringComparison.Ordinal)
                     && int.TryParse(arg["--cast=".Length..], out var slot))
            {
                castSlot = slot;
            }
            else if (arg.StartsWith("--camera=", System.StringComparison.Ordinal))
            {
                var culture = CultureInfo.InvariantCulture;
                var parts = arg["--camera=".Length..].Split(',');
                if (parts.Length is 2 or 3
                    && float.TryParse(parts[0], culture, out var yaw)
                    && float.TryParse(parts[1], culture, out var pitch))
                {
                    cameraAngles = (yaw, pitch);
                }
                // Terceiro valor, opcional: o zoom.
                if (parts.Length == 3 && float.TryParse(parts[2], culture, out var size))
                {
                    cameraSize = size;
                }
            }
            else if (arg == "--freeze-animations")
            {
                freezeAnimations = true;
            }
            else if (arg == "--map-preview")
            {
                mapPreview = true;
            }
            else if (arg == "--attack-nearest")
            {
                attackNearest = true;
            }
            else if (arg == "--open-options")
            {
                openOptions = true;
            }
            else if (arg.StartsWith("--screenshot-delay=", System.StringComparison.Ordinal)
                     && int.TryParse(arg["--screenshot-delay=".Length..], out var frames))
            {
                screenshotDelayFrames = System.Math.Max(frames, 1);
            }
        }

        return new LaunchOptions
        {
            AutoLoginName = autoLoginName,
            Server = server,
            Walk = walk,
            ScreenshotPath = screenshotPath,
            ScreenshotDelayFrames = screenshotDelayFrames,
            FreezeAnimations = freezeAnimations,
            SpritePreviewKey = spritePreviewKey,
            MapPreview = mapPreview,
            CastSlot = castSlot,
            CameraAngles = cameraAngles,
            CameraSize = cameraSize,
            AttackNearest = attackNearest,
            OpenOptions = openOptions,
        };
    }
}
