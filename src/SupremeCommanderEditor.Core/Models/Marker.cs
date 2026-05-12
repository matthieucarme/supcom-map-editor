using System.Numerics;

namespace SupremeCommanderEditor.Core.Models;

public class Marker
{
    public string Name { get; set; } = string.Empty;
    public MarkerType Type { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Orientation { get; set; }

    // Resource markers
    public bool Resource { get; set; }
    public float Amount { get; set; } = 100f;

    // AI path markers
    public string? Color { get; set; }
    public string? Hint { get; set; }
    public List<string> AdjacentMarkers { get; set; } = [];
    public string? Graph { get; set; }

    // Camera
    public float? Zoom { get; set; }
    public bool? CanSetCamera { get; set; }
    public bool? CanSyncCamera { get; set; }

    // Effect markers
    public string? EffectTemplate { get; set; }
    public float? Scale { get; set; }

    // Weather
    public string? WeatherType { get; set; }
    public float? WeatherDriftDirection { get; set; }
    public string? CloudType { get; set; }
    public float? CloudEmitterScale { get; set; }
    public float? CloudEmitterScaleRange { get; set; }
    public float? CloudSpread { get; set; }
    public float? CloudHeightRange { get; set; }
    public float? CloudCountRange { get; set; }
}
