using Godot;

namespace Backlands.Scene;

/// Fundo e luz ambiente do mundo. Quem chama adiciona o nó à cena.
public static class SceneEnvironment
{
    public static WorldEnvironment Create()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.08f, 0.10f, 0.14f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.55f, 0.58f, 0.65f),
            AmbientLightEnergy = 0.6f,
        };
        return new WorldEnvironment { Name = "WorldEnvironment", Environment = environment };
    }
}
