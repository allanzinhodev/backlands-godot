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

    /// Altura do personagem em unidades de mundo (1 unidade = 1 célula).
    /// Tudo que flutua sobre ele (nome, barra de vida, dano) é ancorado a
    /// partir daqui, então mexer só neste número reposiciona o resto junto.
    public const float BodyHeightUnits = 3.2f;

    /// Quantas unidades de mundo cada pixel da arte ocupa.
    ///
    /// Sem suavização em canto nenhum: a textura é construída em tempo de
    /// execução a partir do PNG cru (sem mipmaps) e desenhada com filtro
    /// Nearest, então dobrar a escala dobra o pixel em vez de borrá-lo.
    public const float PixelSize = BodyHeightUnits / FrameHeight;

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

    /// <summary>
    /// Para que lado da tela as poses estão DESENHADAS.
    ///
    /// Medido na arte: nas células de frente das folhas em spritesheets/, os
    /// olhos ficam à esquerda do centro da cabeça, então o personagem encara a
    /// esquerda. Trocar este booleano inverte todos os espelhamentos de uma
    /// vez, que é o ajuste necessário se um dia a arte vier desenhada ao
    /// contrário.
    /// </summary>
    public const bool ArtFacesScreenLeft = true;

    /// (pose, espelhar) para cada direção lógica.
    ///
    /// A pose vem do componente VERTICAL na tela (descendo mostra a frente,
    /// subindo mostra as costas) e o espelho vem do componente HORIZONTAL:
    /// espelha quem aponta para o lado OPOSTO àquele em que a arte foi
    /// desenhada. As duas direções de perfil puro (NorthEast e SouthWest) não
    /// têm componente vertical e usam a frente, que é como o personagem é
    /// mais visto.
    public static (SpritePose Pose, bool Flip) PoseFor(Direction direction)
    {
        var (pose, pointsScreenRight) = direction switch
        {
            // Descem na tela: mostram a frente.
            Direction.SouthEast => (SpritePose.Front, (bool?)null), // reto para baixo
            Direction.South => (SpritePose.Front, false),
            Direction.SouthWest => (SpritePose.Front, false),
            Direction.East => (SpritePose.Front, true),
            Direction.NorthEast => (SpritePose.Front, true),

            // Sobem na tela: mostram as costas.
            Direction.NorthWest => (SpritePose.Back, (bool?)null), // reto para cima
            Direction.West => (SpritePose.Back, false),
            Direction.North => (SpritePose.Back, true),

            _ => (SpritePose.Front, (bool?)null),
        };

        // Direção sem componente horizontal é simétrica: espelhar não muda nada.
        var flip = pointsScreenRight.HasValue
                   && pointsScreenRight.Value == ArtFacesScreenLeft;
        return (pose, flip);
    }

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
            // Espelha quem aponta para o lado oposto ao que a arte foi desenhada.
            var flipFromScreen = ArtFacesScreenLeft ? screenX > 0.01f : screenX < -0.01f;

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
