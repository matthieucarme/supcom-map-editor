namespace SupremeCommanderEditor.Core.Models;

/// <summary>
/// Splat map controlling stratum blending weights.
/// Stored as raw ARGB DDS data (with 128-byte DDS header).
/// ARGB channels map to 4 strata weights.
/// Low mask: strata 1-4 (A=1, R=2, G=3, B=4).
/// High mask: strata 5-8 (A=5, R=6, G=7, B=8).
/// </summary>
public class TextureMask
{
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>
    /// Raw DDS data including 128-byte header + ARGB pixel data.
    /// </summary>
    public byte[] DdsData { get; set; } = [];
}
