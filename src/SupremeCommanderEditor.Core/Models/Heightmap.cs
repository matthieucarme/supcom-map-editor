namespace SupremeCommanderEditor.Core.Models;

public class Heightmap
{
    public int Width { get; set; }
    public int Height { get; set; }
    public float HeightScale { get; set; } = 1f / 128f;

    /// <summary>
    /// Raw 16-bit height values. Array size is (Width+1) * (Height+1).
    /// World elevation = value * HeightScale.
    /// </summary>
    public ushort[] Data { get; set; } = [];

    public float GetWorldHeight(int x, int y)
    {
        return Data[y * (Width + 1) + x] * HeightScale;
    }

    public void SetWorldHeight(int x, int y, float worldHeight)
    {
        var value = (ushort)Math.Clamp(worldHeight / HeightScale, ushort.MinValue, ushort.MaxValue);
        Data[y * (Width + 1) + x] = value;
    }

    public void SetRawHeight(int x, int y, ushort value)
    {
        Data[y * (Width + 1) + x] = value;
    }

    public ushort GetRawHeight(int x, int y)
    {
        return Data[y * (Width + 1) + x];
    }
}
