using Godot;

namespace Backlands.Scene;

/// <summary>
/// A câmera isométrica e o sol que gira com ela.
///
/// Classe simples, e não um nó: ela cria os nós "Sun" e "Camera" no pai que
/// recebe e só guarda o estado vivo (ângulos, pivô, zoom e a volta à
/// isométrica). Quem decide QUANDO girar ou dar zoom é a entrada; aqui mora
/// só COMO.
/// </summary>
public sealed class CameraRig
{
    // Ângulos do Ragnarok: 45 graus de guinada e 45 de inclinação. É só o
    // ponto de partida — o jogador gira a câmera com o botão direito.
    private static readonly Vector3 DefaultAngles = new(-45.0f, 45.0f, 0.0f);

    // Graus por pixel arrastado. Baixo de propósito: a câmera decide a pose de
    // todo sprite da tela, e arrasto nervoso vira enxame de personagens trocando
    // de frente para costas.
    private const float DragDegrees = 0.35f;

    // A inclinação é presa longe dos extremos. De cima (-90°) nenhuma direção
    // tem componente vertical na tela e as oito ficariam iguais; rente ao chão
    // (0°) o mapa some numa linha.
    private const float PitchMin = -80.0f;
    private const float PitchMax = -20.0f;

    // O sol acompanha a guinada da câmera com esta diferença, que é a que ele
    // tinha quando estava preso no mundo — assim o ângulo padrão continua
    // exatamente como era.
    private const float SunPitch = -55.0f;
    private const float SunYawFromCamera = -10.0f;

    /// Ângulos VIVOS da câmera. Tudo que precisa saber para onde ela olha lê
    /// daqui ou da base da própria câmera, nunca mais da constante acima.
    private Vector3 _angles = DefaultAngles;

    /// Ponto para onde a câmera olha, em mundo.
    private Vector3 _pivot;

    // Zoom pela roda do mouse. Em câmera ortogonal zoom é o `Size`: a extensão
    // vertical em tiles. O passo é MULTIPLICATIVO, e não somado: tirar 1 tile
    // de 6 é enorme e de 30 é imperceptível, enquanto 10% é 10% em qualquer
    // nível. Os limites: em 6 o personagem de 3,2 tiles toma metade da tela;
    // em 30 cabe quase o mapa de 32 inteiro.
    public const float ZoomStep = 1.1f;
    private const float SizeMin = 6.0f;
    private const float SizeMax = 30.0f;

    /// Zoom VIVO, pelo mesmo motivo dos ângulos.
    private float _size = DefaultSize;

    // Quanto dura a volta à isométrica no duplo clique. Curto o bastante para
    // não parecer que a câmera travou, longo o bastante para o olho acompanhar
    // de onde ela veio — que é o ponto de animar em vez de saltar.
    private const double ResetSeconds = 0.35;

    private Vector3 _resetFrom;
    private float _resetSizeFrom;

    /// Tempo decorrido da volta; negativo quer dizer que não há volta em curso.
    private double _resetElapsed = -1.0;

    // `size` de uma câmera ortogonal é a extensão VERTICAL em unidades, e
    // 1 unidade = 1 tile. 15 mostra 15 tiles de altura de tela — o chão,
    // encurtado pela inclinação de 45 graus, aparece em ~21 tiles.
    private const float DefaultSize = 15.0f;

    private readonly Camera3D _camera;
    private readonly DirectionalLight3D _sun;

    /// Cria o sol e a câmera, nesta ordem, como filhos de <paramref name="parent"/>.
    public CameraRig(Node3D parent)
    {
        _sun = new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(SunPitch, DefaultAngles.Y + SunYawFromCamera, 0.0f),
            LightEnergy = 1.1f,
            ShadowEnabled = true,
        };
        parent.AddChild(_sun);

        _camera = new Camera3D
        {
            Name = "Camera",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = _size,
            RotationDegrees = _angles,
            // Ortogonal: a distância não muda o enquadramento, só precisa
            // passar do `near` e manter a cena dentro do `far`.
            Position = new Vector3(0, 30, 30),
            Far = 200.0f,
            Current = true,
        };
        parent.AddChild(_camera);
    }

    /// O nó da câmera, para as cenas de conferência que montam o próprio
    /// enquadramento sem passar pelo estado vivo.
    public Camera3D Camera => _camera;

    /// A base da câmera PADRÃO do jogo, na guinada de 45°, ignorando qualquer
    /// giro do jogador. É contra ela que a convenção da arte é conferida.
    public static Basis DefaultBasis =>
        Basis.FromEuler(new Vector3(Mathf.DegToRad(DefaultAngles.X),
                                    Mathf.DegToRad(DefaultAngles.Y), 0.0f));

    /// Troca os ângulos vivos, com a mesma trava de inclinação do arrasto.
    /// Só guarda: quem chama leva à câmera com <see cref="Apply"/>.
    public void SetAngles(float yaw, float pitch)
    {
        _angles = new Vector3(Mathf.Clamp(pitch, PitchMin, PitchMax), yaw, 0.0f);
    }

    /// Troca o zoom vivo, preso aos mesmos limites da roda. Só guarda: quem
    /// chama leva à câmera com <see cref="Apply"/>.
    public void SetSize(float size)
    {
        _size = Mathf.Clamp(size, SizeMin, SizeMax);
    }

    /// Leva o zoom e os ângulos vivos para a câmera, o sol e a posição.
    public void Apply()
    {
        _camera.Size = _size;
        ApplyCameraAngles();
    }

    public void Follow(Vector3 target)
    {
        // Guardado para o arrasto poder reposicionar a câmera sem esperar o
        // próximo quadro: girar com o personagem parado não chamaria isto.
        _pivot = target;
        _camera.Position = target + Offset();
    }

    /// Projeta o clique no plano do chão (y = 0). Interseção com o plano, e não
    /// raycast de física: é exato e não exige corpos de colisão no mapa.
    public (int X, int Y)? ScreenToCell(Vector2 screenPosition)
    {
        var origin = _camera.ProjectRayOrigin(screenPosition);
        var direction = _camera.ProjectRayNormal(screenPosition);

        // Câmera olhando para baixo: direction.Y é negativo. Paralelo ao chão
        // não intersecta.
        if (Mathf.Abs(direction.Y) < 0.0001f)
        {
            return null;
        }

        var distance = -origin.Y / direction.Y;
        if (distance < 0.0f)
        {
            return null;
        }

        var hit = origin + direction * distance;
        return GridSpace.WorldToCell(hit);
    }

    /// Deslocamento que mantém os ângulos da câmera apontados para o alvo.
    /// Com projeção ortogonal a distância não altera o enquadramento.
    private Vector3 Offset()
    {
        const float distance = 40.0f;
        // Dos ângulos VIVOS: lendo a constante, girar a câmera giraria a direção
        // do olhar mas não de onde ela olha, e o alvo sairia do quadro.
        var basis = Basis.FromEuler(new Vector3(Mathf.DegToRad(_angles.X),
                                                Mathf.DegToRad(_angles.Y), 0.0f));
        // -Z é o "para frente" da câmera no Godot, então recuamos por +Z local.
        return basis * new Vector3(0, 0, distance);
    }

    /// Começa a volta suave para a câmera de partida — ângulo E zoom. O pedido
    /// foi "câmera inicial", e zoom é parte dela.
    public void Reset()
    {
        _resetFrom = _angles;
        _resetSizeFrom = _size;
        _resetElapsed = 0.0;
    }

    /// <summary>
    /// Aproxima ou afasta por um fator. Cancela a volta à isométrica em curso
    /// pelo mesmo motivo do arrasto: o jogador tomou o controle.
    /// </summary>
    public void Zoom(float factor)
    {
        _resetElapsed = -1.0;
        _size = Mathf.Clamp(_size * factor, SizeMin, SizeMax);
        _camera.Size = _size;
    }

    /// <summary>
    /// Leva a câmera de volta à isométrica ao longo de
    /// <see cref="ResetSeconds"/>.
    ///
    /// A guinada é interpolada pelo MENOR giro, não pela diferença crua: parado
    /// em 350° e voltando para 45°, a diferença crua manda girar 305° para o
    /// lado errado — a tela daria quase uma volta inteira para andar 55°.
    /// </summary>
    public void Advance(double delta)
    {
        if (_resetElapsed < 0.0)
        {
            return;
        }

        _resetElapsed += delta;
        var t = (float)System.Math.Clamp(_resetElapsed / ResetSeconds, 0.0, 1.0);
        // Suave nas duas pontas: sair e chegar devagar lê como a câmera sendo
        // reposicionada, enquanto a velocidade constante lê como um defeito.
        var eased = t * t * (3.0f - 2.0f * t);

        _angles.X = Mathf.Lerp(_resetFrom.X, DefaultAngles.X, eased);
        _angles.Y = Mathf.Wrap(
            _resetFrom.Y + ShortestTurn(_resetFrom.Y, DefaultAngles.Y) * eased,
            0.0f, 360.0f);
        // O zoom volta pela mesma curva, para ângulo e enquadramento chegarem
        // juntos em vez de um terminar antes do outro.
        _size = Mathf.Lerp(_resetSizeFrom, DefaultSize, eased);

        if (t >= 1.0f)
        {
            // Fecha no valor exato: a interpolação pararia a frações de grau do
            // alvo, e a referência da arte é conferida contra ESTE ângulo.
            _angles = DefaultAngles;
            _size = DefaultSize;
            _resetElapsed = -1.0;
        }

        _camera.Size = _size;
        ApplyCameraAngles();
    }

    /// Menor giro, com sinal, que leva de um ângulo a outro em graus.
    private static float ShortestTurn(float from, float to) =>
        Mathf.Wrap(to - from + 180.0f, 0.0f, 360.0f) - 180.0f;

    /// <summary>
    /// Gira a câmera com o arrasto do botão direito: horizontal guina, vertical
    /// inclina.
    ///
    /// Girar a câmera muda qual pose cada personagem mostra, porque só existem
    /// duas desenhadas e as outras seis saem de espelho — por isso a pose é
    /// projetada na base viva da câmera a cada quadro, e não tabelada.
    /// </summary>
    public void Rotate(Vector2 dragPixels)
    {
        // Arrastar toma o controle de volta: insistir na volta enquanto o
        // jogador gira faria a câmera lutar contra a mão dele.
        _resetElapsed = -1.0;

        _angles.Y -= dragPixels.X * DragDegrees;
        _angles.X = Mathf.Clamp(
            _angles.X - dragPixels.Y * DragDegrees,
            PitchMin,
            PitchMax);

        // A guinada é mantida em [0, 360) para não crescer sem limite numa
        // sessão longa e perder precisão.
        _angles.Y = Mathf.Wrap(_angles.Y, 0.0f, 360.0f);

        ApplyCameraAngles();
    }

    /// <summary>
    /// Leva os ângulos vivos para a câmera, para a posição dela e para o sol.
    ///
    /// O sol gira JUNTO, mantendo a inclinação. Preso no mundo, ele estava
    /// afinado para a guinada padrão: as sombras caem para longe do observador
    /// a 45°, e para CIMA dele a 225° — cobrindo justo os tiles por onde se
    /// anda, com cada árvore iluminada por trás. Metade do giro ficava chapada
    /// e escura. Girando junto, a leitura é a mesma em qualquer ângulo.
    /// </summary>
    private void ApplyCameraAngles()
    {
        _camera.RotationDegrees = _angles;
        _camera.Position = _pivot + Offset();
        _sun.RotationDegrees = new Vector3(
            SunPitch, _angles.Y + SunYawFromCamera, 0.0f);
    }
}
