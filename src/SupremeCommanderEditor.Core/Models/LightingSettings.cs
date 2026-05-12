using System.Numerics;

namespace SupremeCommanderEditor.Core.Models;

public class LightingSettings
{
    public float LightingMultiplier { get; set; } = 1.5f;
    public Vector3 SunDirection { get; set; } = new(0.707f, 0.707f, 0f);
    public Vector3 SunAmbience { get; set; } = new(0.2f, 0.2f, 0.2f);
    public Vector3 SunColor { get; set; } = new(1f, 1f, 1f);
    public Vector3 ShadowFillColor { get; set; } = new(0.7f, 0.7f, 0.75f);
    public Vector4 SpecularColor { get; set; } = new(0f, 0f, 0f, 0f);
    public float Bloom { get; set; }
    public Vector3 FogColor { get; set; } = new(1f, 1f, 1f);
    public float FogStart { get; set; }
    public float FogEnd { get; set; } = 1000f;
}
