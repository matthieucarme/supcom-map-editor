using System.Numerics;

namespace SupremeCommanderEditor.Core.Models;

public class Decal
{
    public int Id { get; set; }
    public DecalType Type { get; set; } = DecalType.Albedo;
    public int Unknown { get; set; }
    public string TexturePath1 { get; set; } = string.Empty;
    public string TexturePath2 { get; set; } = string.Empty;
    public Vector3 Scale { get; set; } = Vector3.One;
    public Vector3 Position { get; set; }
    public Vector3 Rotation { get; set; }
    public float CutOffLod { get; set; } = 1000f;
    public float NearCutOffLod { get; set; }
    public int OwnerArmy { get; set; } = -1;
}
