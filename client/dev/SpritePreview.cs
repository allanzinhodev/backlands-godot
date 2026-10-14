using System.Collections.Generic;
using Backlands.Art;
using Backlands.Model;
using Backlands.Scene;
using Backlands.Ui;
using Godot;

namespace Backlands.Dev;

/// <summary>
/// Cena de conferência da arte, sem servidor: desenha as 8 direções lógicas
/// lado a lado e, abaixo, cada estado de animação.
///
/// É o que mostra se a folha foi recortada certo e se o espelhamento casa com
/// a câmera — um erro de uma linha na grade renderiza uma pose
/// plausível-porém-errada em todas as direções, e é quase invisível no jogo.
/// </summary>
public sealed partial class SpritePreview : Node3D
{
    private const float Spacing = 1.6f;

    private readonly string _key;

    /// A base da câmera do JOGO, não a desta cena.
    ///
    /// A cena de conferência olha de frente, e de frente nenhuma direção tem
    /// componente vertical — todas as oito sairiam de frente e a conferência não
    /// mostraria nada. Quem decide pose e espelho é o ângulo em que o jogo
    /// desenha, então é ele que entra aqui.
    private readonly Basis _gameCameraBasis;

    public SpritePreview(string key, Basis gameCameraBasis)
    {
        _key = key;
        _gameCameraBasis = gameCameraBasis;
    }

    /// Problemas encontrados; vazio quer dizer que a folha está coerente.
    public List<string> Problems { get; } = new();

    /// Monta a cena de conferência de arte: nenhuma rede, câmera de frente,
    /// e o resultado da checagem do mapeamento impresso no log.
    public static void Enter(Node3D root, string key, LoginPanel loginPanel, Hud hud, Camera3D camera,
                             ScreenshotCapture screenshot)
    {
        loginPanel.Visible = false;
        hud.Visible = true;
        hud.Text = $"pré-visualização da folha '{key}'";

        // Em jogo o HUD mora embaixo, acima da barra de skills; aqui não há
        // barra nem janela básica, e a legenda volta ao canto superior esquerdo
        // onde sempre esteve, para a captura deste modo seguir comparável com
        // as antigas. Crescendo para BAIXO, senão o texto subiria para fora da
        // tela a partir do canto.
        hud.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
        hud.GrowVertical = Control.GrowDirection.End;
        hud.Position = new Vector2(12, 8);

        camera.Projection = Camera3D.ProjectionType.Orthogonal;
        camera.Size = 9.0f;
        camera.RotationDegrees = Vector3.Zero;
        camera.Position = new Vector3(0, 0, 12);

        // A conferência usa a câmera PADRÃO do jogo, não a desta cena nem a
        // girada pelo jogador: a referência de poses foi escrita para a guinada
        // de 45°, e é a convenção da arte que ela prende.
        var gameBasis = CameraRig.DefaultBasis;

        var preview = new SpritePreview(key, gameBasis) { Name = "SpritePreview" };
        root.AddChild(preview);

        preview.Problems.AddRange(SpriteLayout.VerifyAgainstCamera(gameBasis));

        if (preview.Problems.Count == 0)
        {
            GD.Print("[sprite] mapeamento de direções coerente com a câmera do jogo");
        }
        else
        {
            foreach (var problem in preview.Problems)
            {
                GD.PrintErr($"[sprite] {problem}");
            }
        }

        screenshot.Schedule();
    }

    public override void _Ready()
    {
        BuildEnvironment();

        var sheet = SpriteSheet.Load(_key);
        if (sheet == null)
        {
            Problems.Add($"folha '{_key}' não carregou (esperada em {SpriteSheet.PathFor(_key)})");
            return;
        }

        BuildDirectionRow(sheet);
        BuildStateRow(sheet);
    }

    private void BuildEnvironment()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.12f, 0.12f, 0.16f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Colors.White,
            AmbientLightEnergy = 1.0f,
        };
        AddChild(new WorldEnvironment { Environment = environment });
    }

    /// Linha de cima: as 8 direções lógicas, cada uma com o nome e a pose que
    /// a tabela escolheu.
    private void BuildDirectionRow(SpriteSheet sheet)
    {
        for (var d = 0; d < Directions.Count; d++)
        {
            var direction = (Direction)d;
            var (pose, flip) = SpriteLayout.PoseFor(direction, _gameCameraBasis);
            var x = (d - 3.5f) * Spacing;

            AddSprite(sheet, SpriteLayout.AnimationName(SpriteState.Idle, pose), flip,
                      new Vector3(x, 1.4f, 0));

            AddLabel($"{direction}\n{pose}{(flip ? " ↔" : "")}", new Vector3(x, 1.1f, 0), 28,
                     flip ? new Color(1.0f, 0.85f, 0.4f) : Colors.White);
        }

        AddLabel("direções lógicas  (↔ = espelhado)", new Vector3(0, 3.3f, 0), 36,
                 new Color(0.7f, 0.85f, 1.0f));
    }

    /// Linha de baixo: cada estado, nas duas poses desenhadas.
    private void BuildStateRow(SpriteSheet sheet)
    {
        var states = new[]
        {
            SpriteState.Idle, SpriteState.Walk, SpriteState.Attack, SpriteState.Dead,
            SpriteState.Evade, SpriteState.Cast, SpriteState.Levelup,
        };

        for (var i = 0; i < states.Length; i++)
        {
            var x = (i - (states.Length - 1) / 2.0f) * Spacing * 1.4f;

            AddSprite(sheet, SpriteLayout.AnimationName(states[i], SpritePose.Front), false,
                      new Vector3(x - 0.35f, -1.6f, 0));
            AddSprite(sheet, SpriteLayout.AnimationName(states[i], SpritePose.Back), false,
                      new Vector3(x + 0.35f, -1.6f, 0));

            AddLabel($"{states[i]}\nfront   back", new Vector3(x, -1.9f, 0), 26, Colors.White);
        }

        AddLabel("estados", new Vector3(0, -0.3f, 0), 36, new Color(0.7f, 0.85f, 1.0f));
    }

    private void AddSprite(SpriteSheet sheet, string animation, bool flip, Vector3 position)
    {
        var sprite = new AnimatedSprite3D
        {
            SpriteFrames = sheet.Frames,
            Animation = animation,
            FlipH = flip,
            PixelSize = SpriteLayout.PixelSize,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
            // Sem billboard aqui: a pré-visualização olha de frente, e girar os
            // sprites esconderia justamente o que queremos conferir.
            Billboard = BaseMaterial3D.BillboardModeEnum.Disabled,
            Position = position,
        };
        AddChild(sprite);
        sprite.Play(animation);
        // Primeiro quadro parado: a conferência é da arte, não do movimento.
        sprite.SpeedScale = 0.0f;
        sprite.SetFrameAndProgress(0, 0.0f);
    }

    private void AddLabel(string text, Vector3 position, int fontSize, Color color)
    {
        AddChild(new Label3D
        {
            Text = text,
            Position = position,
            FontSize = fontSize,
            PixelSize = 0.004f,
            Modulate = color,
            OutlineSize = 12,
            Billboard = BaseMaterial3D.BillboardModeEnum.Disabled,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
    }
}
