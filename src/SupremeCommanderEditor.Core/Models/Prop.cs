using System.Numerics;

namespace SupremeCommanderEditor.Core.Models;

public class Prop
{
    public string BlueprintPath { get; set; } = string.Empty;
    public Vector3 Position { get; set; }
    public Vector3 RotationX { get; set; } = Vector3.UnitX;
    public Vector3 RotationY { get; set; } = Vector3.UnitY;
    public Vector3 RotationZ { get; set; } = Vector3.UnitZ;
    public Vector3 Scale { get; set; } = Vector3.One;
}
