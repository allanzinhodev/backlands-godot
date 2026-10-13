using System.Collections.Generic;
using Backlands.Game;
using Backlands.Net;

namespace Backlands.Art;

/// Estados de animação. A folha traz todos; os que ainda não têm evento no
/// protocolo (Cast, Levelup) ficam carregados e parados até existirem.
public enum SpriteState
{
    Idle,
    Walk,
    Attack,
    Dead,
    Evade,
    Cast,
    Levelup,
}

/// As duas poses desenhadas. O lado esquerdo sai por espelhamento.
public enum SpritePose
{
    Front = 0,
    Back = 1,
}

/// Uma célula da grade, 1-indexada como no mapa que o artista usa.
public readonly record struct SheetCell(int Row, int Column);

/// <summary>
/// Onde cada (estado, pose) mora na folha, e como as 8 direções lógicas viram
/// (pose, espelhar).
///
/// A regra é a mesma para toda folha, então mora aqui e não num descritor por
/// arquivo que teria de ser mantido em sincronia com a arte.
/// </summary>
public static class SpriteLayout
{
    public const int FrameWidth = 32;

    public const int FrameHeight = 48;

    /// Cor das linhas que separam a grade.
    public static readonly Godot.Color GridColor = Godot.Color.FromHtml("e0e0e0");

    /// Cor tratada como fundo. Não é alpha no arquivo: é cor-chave.
    public static readonly Godot.Color BackgroundColor = Godot.Color.FromHtml("9c8b93");

    /// 48 px de altura viram ~1,6 unidade de mundo, deixando o personagem com
    /// cerca de 1,6 célula de altura — a proporção do Ragnarok.
    public const float PixelSize = 1.6f / FrameHeight;

    /// Células de cada (estado, pose), na ordem dos quadros.
    private static readonly Dictionary<(SpriteState, SpritePose), SheetCell[]> Cells = new()
    {
        [(SpriteState.Idle, SpritePose.Front)] = new[] { new SheetCell(1, 1) },
        [(SpriteState.Idle, SpritePose.Back)] = new[] { new SheetCell(1, 2) },

        [(SpriteState.Walk, SpritePose.Front)] = new[] { new SheetCell(1, 3), new SheetCell(1, 4) },
        [(SpriteState.Walk, SpritePose.Back)] = new[] { new SheetCell(1, 5), new SheetCell(1, 6) },

        [(SpriteState.Levelup, SpritePose.Front)] = new[] { new SheetCell(1, 7) },
        [(SpriteState.Levelup, SpritePose.Back)] = new[] { new SheetCell(1, 8) },

        [(SpriteState.Evade, SpritePose.Front)] = new[] { new SheetCell(2, 1) },
        [(SpriteState.Evade, SpritePose.Back)] = new[] { new SheetCell(2, 2) },

        [(SpriteState.Dead, SpritePose.Front)] = new[] { new SheetCell(2, 3) },
        [(SpriteState.Dead, SpritePose.Back)] = new[] { new SheetCell(2, 4) },

        [(SpriteState.Cast, SpritePose.Front)] = new[] { new SheetCell(2, 5) },
        [(SpriteState.Cast, SpritePose.Back)] = new[] { new SheetCell(2, 6) },

        [(SpriteState.Attack, SpritePose.Front)] = new[] { new SheetCell(3, 1), new SheetCell(3, 2) },
        [(SpriteState.Attack, SpritePose.Back)] = new[] { new SheetCell(3, 3), new SheetCell(3, 4) },
    };

    public static SheetCell[] CellsFor(SpriteState state, SpritePose pose) => Cells[(state, pose)];

    public static IEnumerable<(SpriteState State, SpritePose Pose, SheetCell[] Frames)> All()
    {
        foreach (var ((state, pose), cells) in Cells)
        {
            yield return (state, pose, cells);
        }
    }

    /// Nome da animação dentro do SpriteFrames.
    public static string AnimationName(SpriteState state, SpritePose pose) =>
        $"{state}_{pose}".ToLowerInvariant();

    /// <summary>
    /// A direção que é o espelho horizontal desta, na projeção da câmera.
    ///
    /// A fórmula natural seria (6 - d) % 8, mas <c>%</c> em C# é resto com o
    /// sinal do dividendo: para SouthEast (7) daria -1 e estouraria o índice
    /// justamente na direção mais comum, a de frente para a câmera. Somar 8
    /// antes mantém o numerador positivo sem ramificação.
    ///
    /// O 6 vem da guinada de 45 graus da câmera. <see cref="VerifyAgainstCamera"/>
    /// confere isso contra a câmera real na inicialização.
    /// </summary>
    public static Direction Mirror(Direction direction) =>
        (Direction)((14 - (int)direction) % 8);

    /// (pose, espelhar) para cada direção lógica.
    ///
    /// A pose vem do componente VERTICAL na tela (descendo mostra a frente,
    /// subindo mostra as costas) e o espelho vem do componente HORIZONTAL
    /// (indo para a esquerda). As duas direções de perfil puro (NorthEast e
    /// SouthWest) não têm componente vertical e usam a frente, que é como o
    /// personagem é mais visto.
    public static (SpritePose Pose, bool Flip) PoseFor(Direction direction) => direction switch
    {
        Direction.SouthEast => (SpritePose.Front, false),
        Direction.East => (SpritePose.Front, false),
        Direction.NorthEast => (SpritePose.Front, false),
        Direction.South => (SpritePose.Front, true),
        Direction.SouthWest => (SpritePose.Front, true),
        Direction.NorthWest => (SpritePose.Back, false),
        Direction.North => (SpritePose.Back, false),
        Direction.West => (SpritePose.Back, true),
        _ => (SpritePose.Front, false),
    };

    /// <summary>
    /// Confere a tabela acima contra a câmera de verdade e devolve o que estiver
    /// errado. Mexer no ângulo da câmera muda quais direções são espelho de
    /// quais — virando erro visível aqui, em vez de arte sutilmente trocada.
    /// </summary>
    public static List<string> VerifyAgainstCamera(Godot.Basis cameraBasis)
    {
        var problems = new List<string>();

        var right = cameraBasis.Column0;  // direita da tela, no mundo
        var up = cameraBasis.Column1;     // cima da tela, no mundo

        for (var d = 0; d < 8; d++)
        {
            var direction = (Direction)d;
            var delta = WorldState.DirectionDeltas[d];
            // Célula x vira eixo X do mundo; célula y vira eixo Z.
            var world = new Godot.Vector3(delta.X, 0, delta.Y);

            var screenX = world.Dot(right);
            var screenY = world.Dot(up);

            var expected = PoseFor(direction);
            var poseFromScreen = screenY switch
            {
                < -0.01f => SpritePose.Front, // descendo: vem na direção da câmera
                > 0.01f => SpritePose.Back,   // subindo: vai embora
                _ => SpritePose.Front,        // perfil puro
            };
            var flipFromScreen = screenX < -0.01f;

            if (expected.Pose != poseFromScreen || expected.Flip != flipFromScreen)
            {
                problems.Add(
                    $"{direction}: tabela diz ({expected.Pose}, espelho={expected.Flip}) mas a "
                    + $"câmera projeta em ({poseFromScreen}, espelho={flipFromScreen})");
            }

            // O espelho tem de ser a direção cuja projeção tem X oposto e Y igual.
            var mirror = Mirror(direction);
            var mirrorDelta = WorldState.DirectionDeltas[(int)mirror];
            var mirrorWorld = new Godot.Vector3(mirrorDelta.X, 0, mirrorDelta.Y);
            if (!Godot.Mathf.IsEqualApprox(mirrorWorld.Dot(right), -screenX)
                || !Godot.Mathf.IsEqualApprox(mirrorWorld.Dot(up), screenY))
            {
                problems.Add($"{direction}: {mirror} não é o espelho dela nesta câmera");
            }
        }

        return problems;
    }
}
