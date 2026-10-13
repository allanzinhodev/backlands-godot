using System.Collections.Generic;
using Backlands.Net;
using Godot;

namespace Backlands.Game;

/// <summary>
/// Desenha o mapa a partir do grid recebido do servidor.
///
/// Um MultiMeshInstance3D por terreno em vez de um nó por célula: 32x32 viram
/// 4 nós em vez de 1024, e trocar de mapa é só reconstruir as instâncias.
/// </summary>
public sealed partial class MapRenderer : Node3D
{
    /// 1 célula = 1 unidade de mundo, conforme a escala do projeto.
    public const float TileSize = 1.0f;

    private readonly Dictionary<Terrain, MultiMeshInstance3D> _layers = new();

    // Alturas distintas dão leitura imediata do relevo: a água afunda, a
    // ponte fica um fio acima do chão, a pedra sobe como bloco.
    private static readonly Dictionary<Terrain, (Color Color, float Height)> Styles = new()
    {
        [Terrain.Grass] = (new Color(0.30f, 0.52f, 0.25f), 0.00f),
        [Terrain.Dirt] = (new Color(0.45f, 0.36f, 0.24f), 0.00f),
        [Terrain.Bridge] = (new Color(0.52f, 0.37f, 0.20f), 0.02f),
        [Terrain.Water] = (new Color(0.16f, 0.34f, 0.60f), -0.18f),
        [Terrain.Rock] = (new Color(0.44f, 0.44f, 0.47f), 0.00f),
    };

    /// Converte célula lógica em posição no mundo, no CENTRO da célula.
    /// O +0.5 é o que faz o personagem ficar no meio do tile, não na quina.
    public static Vector3 CellToWorld(int cellX, int cellY, float y = 0.0f)
    {
        return new Vector3((cellX + 0.5f) * TileSize, y, (cellY + 0.5f) * TileSize);
    }

    /// Inverso: posição no mundo para célula lógica. floor e não round, senão
    /// a célula "escorregaria" meio tile.
    public static (int X, int Y) WorldToCell(Vector3 position)
    {
        return (Mathf.FloorToInt(position.X / TileSize), Mathf.FloorToInt(position.Z / TileSize));
    }

    public void Build(MapData map)
    {
        foreach (var layer in _layers.Values)
        {
            layer.QueueFree();
        }
        _layers.Clear();

        if (!map.Complete)
        {
            GD.PushWarning("[mapa] Build chamado com grid incompleto");
            return;
        }

        // Agrupa as células por terreno antes de criar as instâncias.
        var byTerrain = new Dictionary<Terrain, List<Vector3>>();
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var cell = map.At(x, y);
                if (!Styles.TryGetValue(cell.Terrain, out var style))
                {
                    style = Styles[Terrain.Grass];
                }

                if (!byTerrain.TryGetValue(cell.Terrain, out var list))
                {
                    list = new List<Vector3>();
                    byTerrain[cell.Terrain] = list;
                }
                list.Add(CellToWorld(x, y, style.Height));
            }
        }

        foreach (var (terrain, positions) in byTerrain)
        {
            var style = Styles.TryGetValue(terrain, out var s) ? s : Styles[Terrain.Grass];
            _layers[terrain] = CreateLayer(terrain, style.Color, positions, terrain == Terrain.Rock);
        }

        GD.Print($"[mapa] '{map.Name}' {map.Width}x{map.Height} desenhado em {_layers.Count} camadas");
    }

    private MultiMeshInstance3D CreateLayer(Terrain terrain, Color color, List<Vector3> positions,
                                            bool asBlock)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = color,
            // Pixel art e tiles pedem filtragem nearest; sem isto o Godot usa
            // linear com mipmaps e o resultado fica borrado.
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Roughness = 0.9f,
        };

        Mesh mesh;
        if (asBlock)
        {
            // Pedra como bloco, para ler como obstáculo e projetar sombra.
            mesh = new BoxMesh { Size = new Vector3(TileSize, 0.7f, TileSize), Material = material };
        }
        else
        {
            mesh = new PlaneMesh { Size = new Vector2(TileSize, TileSize), Material = material };
        }

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = positions.Count,
        };

        for (var i = 0; i < positions.Count; i++)
        {
            // O BoxMesh é centrado, então sobe meia altura para assentar no chão.
            var position = asBlock ? positions[i] + new Vector3(0, 0.35f, 0) : positions[i];
            multiMesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, position));
        }

        var instance = new MultiMeshInstance3D
        {
            Name = $"Terrain{terrain}",
            Multimesh = multiMesh,
        };
        AddChild(instance);
        return instance;
    }
}
