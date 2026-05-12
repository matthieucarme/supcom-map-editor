using System.Numerics;

namespace SupremeCommanderEditor.Core.Models;

public class SkyBox
{
    public Vector3 Position { get; set; } = new(256f, 0f, 256f);
    public float HorizonHeight { get; set; } = -42.5f;
    public float Scale { get; set; } = 1171.5814f;
    public float SubHeight { get; set; } = 1.256637f;
    public int SubdivisionAxis { get; set; } = 16;
    public int SubdivisionHeight { get; set; } = 6;
    public float ZenithHeight { get; set; } = 293.50708f;
    public Vector3 HorizonColor { get; set; }
    public Vector3 ZenithColor { get; set; }
    public float DecalGlowMultiplier { get; set; } = 0.1f;
    public string AlbedoTexturePath { get; set; } = string.Empty;
    public string GlowTexturePath { get; set; } = string.Empty;
    public List<Planet> Planets { get; set; } = [];

    // Mid color stored as 3 bytes in the file
    public byte MidColorRed { get; set; }
    public byte MidColorBlue { get; set; }
    public byte MidColorGreen { get; set; }

    public float CirrusMultiplier { get; set; }
    public Vector3 CirrusColor { get; set; }
    public string CirrusTexturePath { get; set; } = string.Empty;
    public List<CirrusLayer> CirrusLayers { get; set; } = [];
}
