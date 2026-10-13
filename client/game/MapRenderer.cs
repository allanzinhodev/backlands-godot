using System.Collections.Generic;
using Backlands.Net;
using Godot;

namespace Backlands.Game;

/// <summary>Low-poly ruins, batched by mesh; server cells remain authoritative.</summary>
public sealed partial class MapRenderer : Node3D
{
    public const float TileSize = 1.0f;
    private Node3D? _geometry;
    private readonly Dictionary<string, List<Transform3D>> _batches = new();
    private readonly Dictionary<string, Mesh> _meshes = new();

    public static Vector3 CellToWorld(int x, int y, float height = 0) => new(x + 0.5f, height, y + 0.5f);
    public static (int X, int Y) WorldToCell(Vector3 p) => (Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Z));

    public void Build(MapData map)
    {
        if (!map.Complete) { GD.PushWarning("[mapa] grid incompleto"); return; }
        if (_geometry != null) { RemoveChild(_geometry); _geometry.QueueFree(); }
        _geometry = new Node3D { Name = "Ruins" };
        AddChild(_geometry);
        _batches.Clear();
        _meshes.Clear();
        _colors.Clear();
        var water = new ShaderMaterial { Shader = GD.Load<Shader>("res://client/art/shaders/map_water.gdshader") };
        var foliage = new ShaderMaterial { Shader = GD.Load<Shader>("res://client/art/shaders/map_foliage.gdshader") };
        water.SetShaderParameter("frozen", System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--freeze-animations") >= 0);
        foliage.SetShaderParameter("frozen", System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--freeze-animations") >= 0);
        _meshes["water"] = new PlaneMesh { Size = Vector2.One, Material = water };
        _meshes["leaves"] = new SphereMesh { RadialSegments = 6, Rings = 2, Radius = 0.5f, Height = 1, Material = foliage };
        _meshes["trunk"] = new CylinderMesh { TopRadius = 0.10f, BottomRadius = 0.18f, Height = 1.6f, RadialSegments = 5, Rings = 1, Material = Material("655637") };
        _meshes["bush"] = new SphereMesh { RadialSegments = 5, Rings = 1, Radius = 0.5f, Height = 1, Material = foliage };
        _meshes["moss"] = new BoxMesh { Size = Vector3.One, Material = Material("527735") };
        _meshes["earth"] = new BoxMesh { Size = Vector3.One, Material = Material("8e8960") };
        for (var i = 0; i < 3; i++)
        {
            _meshes[$"grass{i}"] = new PlaneMesh { Size = Vector2.One, Material = Material(new[] { "46713a", "527e3e", "5e8745" }[i]) };
            _meshes[$"stone{i}"] = new BoxMesh { Size = Vector3.One, Material = Material(new[] { "aaa477", "929064", "b8b183" }[i]) };
            _meshes[$"paving{i}"] = new PlaneMesh { Size = Vector2.One, Material = _meshes[$"stone{i}"].SurfaceGetMaterial(0) };
        }
        // One solid foundation rather than a cube under every tile.
        Add("earth", new Vector3(map.Width / 2f, -0.65f, map.Height / 2f), new Vector3(map.Width, 0.9f, map.Height));
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var cell = map.At(x, y);
            var seed = (x * 73 + y * 137) % 101;
            var p = CellToWorld(x, y);
            switch (cell.Terrain)
            {
                case Terrain.Water:
                    Add("water", p + Vector3.Down * 0.16f, Vector3.One);
                    break;
                case Terrain.Rock:
                    // Broken masonry with a moss cap. Never placed on walkable cells.
                    var h = 0.45f + seed % 3 * 0.20f;
                    Add($"stone{seed % 3}", p + Vector3.Up * h / 2, new Vector3(0.97f, h, 0.97f));
                    if (seed % 3 != 0) Add("moss", p + Vector3.Up * (h + 0.015f), new Vector3(0.9f, 0.03f, 0.88f));
                    break;
                case Terrain.Dirt:
                case Terrain.Bridge:
                    // Four inexpensive slabs per cell create readable paving joints.
                    for (var a = 0; a < 2; a++)
                    for (var b = 0; b < 2; b++)
                        Add($"paving{(seed + a + b) % 3}", p + new Vector3(a * 0.5f - 0.25f, 0, b * 0.5f - 0.25f), new Vector3(0.48f, 1, 0.48f));
                    break;
                default:
                    Add($"grass{seed % 3}", p, Vector3.One);
                    if (!cell.Walkable)
                    {
                        Add("trunk", p + Vector3.Up * 0.8f, Vector3.One);
                        Add("leaves", p + new Vector3(0, 2.0f, 0), new Vector3(1.75f, 1.25f, 1.65f), "75a83f");
                        Add("leaves", p + new Vector3(-0.48f, 1.65f, 0.25f), new Vector3(1.1f, 0.85f, 1.05f), "3d742f");
                        Add("leaves", p + new Vector3(0.5f, 1.85f, -0.2f), new Vector3(1.1f, 0.9f, 1.1f), "8bbb48");
                    }
                    else if (seed % 5 == 0)
                    {
                        // Small ground cover leaves walking routes readable.
                        Add("bush", p + new Vector3(0.28f, 0.12f, 0.26f), new Vector3(0.42f, 0.28f, 0.48f), "3b6c32");
                    }
                    break;
            }
        }
        var triangles = 0;
        foreach (var (key, transforms) in _batches)
        {
            var mesh = _meshes[key];
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = transforms.Count };
            for (var i = 0; i < transforms.Count; i++)
            {
                mm.SetInstanceTransform(i, transforms[i]);
                mm.SetInstanceColor(i, _colors[key][i]);
            }
            _geometry.AddChild(new MultiMeshInstance3D { Name = key, Multimesh = mm,
                CastShadow = key is "leaves" or "trunk" || key.StartsWith("stone") ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off });
            triangles += mesh.GetFaces().Length / 3 * transforms.Count;
        }
        GD.Print($"[mapa] {map.Name}: {_batches.Count} lotes, {triangles} triângulos (sem passe de sombras)");
        _colors.Clear();
    }

    private readonly Dictionary<string, List<Color>> _colors = new();
    private void Add(string key, Vector3 position, Vector3 scale, string color = "ffffff")
    {
        if (!_batches.ContainsKey(key)) { _batches[key] = new(); _colors[key] = new(); }
        _batches[key].Add(new Transform3D(Basis.Identity.Scaled(scale), position));
        _colors[key].Add(new Color(color));
    }
    private static StandardMaterial3D Material(string color) => new() { AlbedoColor = new Color(color), Roughness = 1.0f };
}
