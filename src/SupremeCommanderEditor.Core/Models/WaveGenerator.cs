using System.Numerics;

namespace SupremeCommanderEditor.Core.Models;

public class WaveGenerator
{
    public string TextureName { get; set; } = string.Empty;
    public string RampName { get; set; } = string.Empty;
    public Vector3 Position { get; set; }
    public float Rotation { get; set; }
    public Vector3 Velocity { get; set; }
    public float LifetimeFirst { get; set; }
    public float LifetimeSecond { get; set; }
    public float PeriodFirst { get; set; }
    public float PeriodSecond { get; set; }
    public float ScaleFirst { get; set; }
    public float ScaleSecond { get; set; }
    public float FrameCount { get; set; }
    public float FrameRateFirst { get; set; }
    public float FrameRateSecond { get; set; }
    public float StripCount { get; set; }
}
