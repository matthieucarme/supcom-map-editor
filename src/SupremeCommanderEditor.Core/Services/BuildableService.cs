using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Services;

/// <summary>Computes which build-grid cells are flat enough for a "big building" (T3 factory /
/// shield / experimental). The check matches the in-game placement rule: across the cell's 4
/// corners we look at the max pairwise slope (rise/run in world units) and reject the cell when
/// it exceeds the threshold. Cells whose terrain sits below water level are also rejected, since
/// nothing land-based can build there.
///
/// One cell = one heightmap unit (1×1). Output is row-major <c>byte[Width * Height]</c> with
/// <c>0</c> = buildable, <c>1</c> = unbuildable. The user knows that if they intend to place a
/// 4×4 building, all 16 cells of its footprint must be green.</summary>
public static class BuildableService
{
    /// <summary>Slope threshold for "big building". Vanilla SC1 blueprints (UEB1301 T3 factory,
    /// UEB2302 T2 shield, …) don't declare an explicit <c>Physics.MaxSlope</c>; the engine falls
    /// back to a hardcoded default in the 0.06–0.10 range. Settled on 0.075 after in-game checks
    /// on rough maps (Saltrock Colony): 0.06 ruled out spots the engine actually accepts, 0.10
    /// would mark unbuildable shield/T3 slots green. If a big building fits, smaller buildings
    /// fit too, so a single strict threshold serves the "where can I drop a factory?" use case.</summary>
    public const float BigBuildingSlopeThreshold = 0.075f;

    public static byte[] ComputeMask(ScMap map)
    {
        int w = map.Heightmap.Width;
        int h = map.Heightmap.Height;
        var mask = new byte[w * h];

        float waterLevel = map.Water.HasWater ? map.Water.Elevation : float.MinValue;
        float threshold = BigBuildingSlopeThreshold;
        float invSqrt2 = 0.70710677f;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float h00 = map.Heightmap.GetWorldHeight(x, y);
                float h10 = map.Heightmap.GetWorldHeight(x + 1, y);
                float h01 = map.Heightmap.GetWorldHeight(x, y + 1);
                float h11 = map.Heightmap.GetWorldHeight(x + 1, y + 1);

                float maxCorner = MathF.Max(MathF.Max(h00, h10), MathF.Max(h01, h11));
                bool underwater = maxCorner < waterLevel;

                // Slope = height diff / horizontal distance. Adjacent corners are 1 unit apart,
                // diagonal corners sqrt(2) — so we scale the diagonal differences accordingly to
                // get a comparable slope value.
                float adj = MathF.Max(
                    MathF.Max(MathF.Abs(h00 - h10), MathF.Abs(h01 - h11)),
                    MathF.Max(MathF.Abs(h00 - h01), MathF.Abs(h10 - h11)));
                float diag = MathF.Max(MathF.Abs(h00 - h11), MathF.Abs(h10 - h01)) * invSqrt2;
                float slope = MathF.Max(adj, diag);

                mask[y * w + x] = (underwater || slope > threshold) ? (byte)1 : (byte)0;
            }
        }

        return mask;
    }
}
