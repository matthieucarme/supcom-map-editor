using System.Numerics;

namespace SupremeCommanderEditor.Core.Models;

public class WaterSettings
{
    public bool HasWater { get; set; }
    public float Elevation { get; set; } = 25f;
    public float ElevationDeep { get; set; } = 20f;
    public float ElevationAbyss { get; set; } = 10f;

    public Vector3 SurfaceColor { get; set; } = new(0f, 0.7f, 1.5f);
    public Vector2 ColorLerp { get; set; } = new(0.064f, 0.119f);
    public float RefractionScale { get; set; } = 0.375f;
    public float FresnelBias { get; set; } = 0.15f;
    public float FresnelPower { get; set; } = 1.377f;
    public float UnitReflection { get; set; } = 0.5f;
    public float SkyReflection { get; set; } = 1.5f;

    public float SunShininess { get; set; } = 100f;
    public float SunStrength { get; set; } = 10f;
    public Vector3 SunDirection { get; set; } = new(0.09954818f, -0.9626309f, 0.2518569f);
    public Vector3 SunColor { get; set; } = new(0.81f, 0.47f, 0.3f);
    public float SunReflection { get; set; } = 5f;
    public float SunGlow { get; set; } = 0.1f;

    public string CubemapFile { get; set; } = string.Empty;
    public string WaterRampFile { get; set; } = string.Empty;

    public float[] NormalRepeats { get; set; } = new float[4];
    public WaveTexture[] WaveTextures { get; set; } = [new(), new(), new(), new()];
}
