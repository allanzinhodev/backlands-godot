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

    /// <summary>
    /// Quanto preto é misturado na borda INTERNA de cada quadro, para o
    /// personagem ter contorno de 1 pixel.
    ///
    /// O contorno é assado na arte no carregamento, em pixels DA FOLHA, e não
    /// desenhado por shader: assim ele tem 1 pixel de arte de espessura de
    /// verdade, e cresce junto com o personagem na ampliação em vez de virar um
    /// fio fino de 1 pixel de tela. É o mesmo motivo de a arte ser filtrada com
    /// Nearest.
    ///
    /// É preto MISTURADO, não preto com alpha 0,5: o sprite é desenhado com
    /// corte de alpha em 0,5 (AlphaCut.Discard), onde alpha é binário — meio
    /// alpha não existe, viraria opaco ou sumiria. Misturar dá a mesma leitura
    /// de "preto a 50%" que um editor de imagem produz, sem depender de
    /// transparência que este modo de desenho não tem.
    /// </summary>
    public const float OutlineBlend = 0.5f;

    /// Abaixo deste alpha o pixel não é desenhado, porque o corte do sprite é
    /// em 0,5. O contorno usa o MESMO limiar, senão ele seria calculado contra
    /// pixels que o jogador nunca vê.
    public const float OutlineSolidAlpha = 0.5f;

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

    /// Abaixo deste valor a projeção é tratada como zero. Perto do zero o sinal
    /// é ruído de ponto flutuante, e decidir por ele faria a pose oscilar
    /// enquanto o jogador gira a câmera devagar.
    private const float Deadzone = 0.01f;

    /// <summary>
    /// (pose, espelhar) para uma direção lógica, do ponto de vista da câmera
    /// dada.
    ///
    /// Calculado, e não tabelado: a câmera gira em jogo, e uma tabela só valeria
    /// para a guinada em que foi escrita — bastaria girar 90° para todo mundo
    /// passar a andar de costas.
    ///
    /// A pose vem do componente VERTICAL na tela (descendo mostra a frente,
    /// subindo mostra as costas) e o espelho vem do componente HORIZONTAL:
    /// espelha quem aponta para o lado OPOSTO àquele em que a arte foi
    /// desenhada. Direção de perfil puro não tem componente vertical e usa a
    /// frente, que é como o personagem é mais visto.
    /// </summary>
    public static (SpritePose Pose, bool Flip) PoseFor(Direction direction, Godot.Basis cameraBasis)
    {
        var delta = WorldState.DirectionDeltas[(int)direction];
        // Célula x vira eixo X do mundo; célula y vira eixo Z.
        var world = new Godot.Vector3(delta.X, 0, delta.Y);

        var screenX = world.Dot(cameraBasis.Column0); // direita da tela, no mundo
        var screenY = world.Dot(cameraBasis.Column1); // cima da tela, no mundo

        var pose = screenY < -Deadzone ? SpritePose.Front
                 : screenY > Deadzone ? SpritePose.Back
                 : SpritePose.Front;

        var flip = ArtFacesScreenLeft ? screenX > Deadzone : screenX < -Deadzone;
        return (pose, flip);
    }

    /// O que a projeção DEVE dar na câmera padrão do jogo (guinada de 45°).
    ///
    /// Não é usada para desenhar: existe só para <see cref="VerifyAgainstCamera"/>
    /// prender a convenção da arte. Se alguém inverter ArtFacesScreenLeft, trocar
    /// a ordem do enum de direções ou mudar o ângulo padrão da câmera, isso vira
    /// erro no log em vez de arte sutilmente trocada.
    private static readonly (SpritePose Pose, bool Flip)[] ReferencePoses =
    {
        (SpritePose.Front, false), // South      — desce à esquerda
        (SpritePose.Front, false), // SouthWest  — perfil para a esquerda
        (SpritePose.Back, false),  // West       — sobe à esquerda
        (SpritePose.Back, false),  // NorthWest  — reto para cima
        (SpritePose.Back, true),   // North      — sobe à direita
        (SpritePose.Front, true),  // NorthEast  — perfil para a direita
        (SpritePose.Front, true),  // East       — desce à direita
        (SpritePose.Front, false), // SouthEast  — reto para baixo
    };

    /// <summary>
    /// Confere a projeção contra a referência documentada e devolve o que
    /// estiver errado.
    ///
    /// Só faz sentido com a câmera PADRÃO do jogo: a referência foi escrita para
    /// a guinada de 45°, e girar a câmera em jogo muda legitimamente quais
    /// direções são espelho de quais. Chame na inicialização, antes de qualquer
    /// rotação — é o que prende a convenção da arte e a ordem do enum.
    /// </summary>
    public static List<string> VerifyAgainstCamera(Godot.Basis cameraBasis)
    {
        var problems = new List<string>();

        for (var d = 0; d < 8; d++)
        {
            var direction = (Direction)d;
            var expected = ReferencePoses[d];
            var actual = PoseFor(direction, cameraBasis);

            if (expected != actual)
            {
                problems.Add(
                    $"{direction}: a referência diz ({expected.Pose}, espelho={expected.Flip}) "
                    + $"mas esta câmera projeta em ({actual.Pose}, espelho={actual.Flip})");
            }

            // O espelho tem de ser a direção cuja projeção tem X oposto e Y igual.
            var delta = WorldState.DirectionDeltas[d];
            var world = new Godot.Vector3(delta.X, 0, delta.Y);
            var mirrorDelta = WorldState.DirectionDeltas[(int)Mirror(direction)];
            var mirrorWorld = new Godot.Vector3(mirrorDelta.X, 0, mirrorDelta.Y);

            if (!Godot.Mathf.IsEqualApprox(mirrorWorld.Dot(cameraBasis.Column0),
                                           -world.Dot(cameraBasis.Column0))
                || !Godot.Mathf.IsEqualApprox(mirrorWorld.Dot(cameraBasis.Column1),
                                              world.Dot(cameraBasis.Column1)))
            {
                problems.Add($"{direction}: {Mirror(direction)} não é o espelho dela nesta câmera");
            }
        }

        return problems;
    }
}
