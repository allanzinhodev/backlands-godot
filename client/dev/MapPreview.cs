using Backlands.Model;
using Backlands.Scene;
using Backlands.Ui;
using Godot;

namespace Backlands.Dev;

/// <summary>
/// Pré-visualização do mapa sem servidor (--map-preview): lê o layout direto
/// do Lua do servidor e desenha, com a câmera aberta sobre o mapa inteiro.
///
/// Serve para conferir a ARTE do mapa; quem manda no grid de verdade continua
/// sendo o servidor, que o envia ao entrar no mundo.
/// </summary>
public static class MapPreview
{
    public static void Enter(MapRenderer renderer, LoginPanel loginPanel, CameraRig camera,
                             ScreenshotCapture screenshot)
    {
        renderer.Build(LoadStarterFields());
        loginPanel.Visible = false;
        camera.Camera.Size = 39;
        // Por Follow, para o pivô nascer coerente com a posição: girar
        // antes de o pivô existir saltaria a câmera para a origem do mapa.
        camera.Follow(new Vector3(16, 0, 16));
        screenshot.Schedule();
    }

    /// O mapa inicial como o servidor o carrega, traduzido para os bytes de
    /// célula do fio.
    private static MapData LoadStarterFields()
    {
        // Read the actual server layout for an offline art preview.
        var source = FileAccess.GetFileAsString("res://server/maps/starter_fields.lua");
        var rows = System.Text.RegularExpressions.Regex.Matches(source, "\"([#.~=:T]{32})\"");
        var map = new MapData();
        map.Begin("starter_fields", 32, rows.Count);
        for (var y = 0; y < rows.Count; y++)
        {
            var row = new byte[32];
            for (var x = 0; x < 32; x++)
            {
                row[x] = rows[y].Groups[1].Value[x] switch
                {
                    '~' => 0x10, '#' => 0x22, '=' => 0x31, ':' => 0x41,
                    'T' => 0x02, _ => 0x01,
                };
            }
            map.SetRow(y, row);
        }
        return map;
    }
}
