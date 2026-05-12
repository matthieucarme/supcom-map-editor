using System.Numerics;

namespace SupremeCommanderEditor.Core.Models;

public class Planet
{
    public Vector3 Position { get; set; }
    public float Rotation { get; set; }
    public Vector2 Scale { get; set; } = Vector2.One;
    public Vector4 Uv { get; set; }
}
