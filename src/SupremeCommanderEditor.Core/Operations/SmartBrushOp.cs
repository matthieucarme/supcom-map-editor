using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Operations;

/// <summary>Snapshot of one <see cref="TerrainTexture"/> slot (4 user-facing fields).</summary>
public readonly record struct TerrainTextureSnapshot(
    string AlbedoPath, string NormalPath, float AlbedoScale, float NormalScale)
{
    public static TerrainTextureSnapshot Of(TerrainTexture t) => new(
        t.AlbedoPath ?? "", t.NormalPath ?? "", t.AlbedoScale, t.NormalScale);

    public void ApplyTo(TerrainTexture t)
    {
        t.AlbedoPath = AlbedoPath;
        t.NormalPath = NormalPath;
        t.AlbedoScale = AlbedoScale;
        t.NormalScale = NormalScale;
    }
}

/// <summary>
/// Single undoable record of a Smart-toggled brush stroke. Captures heightmap, both splatmaps, AND
/// the 10 TerrainTexture slot fields — Smart may have auto-assigned new textures to free slots when
/// the stroke started, and Undo has to revert those too.
/// </summary>
public class SmartBrushOp : IMapOperation
{
    private readonly ScMap _map;
    private readonly ushort[] _beforeHm, _afterHm;
    private readonly byte[] _beforeLow, _afterLow;
    private readonly byte[] _beforeHigh, _afterHigh;
    private readonly TerrainTextureSnapshot[] _beforeTextures;
    private readonly TerrainTextureSnapshot[] _afterTextures;

    public string Description => "Smart brush stroke";

    public SmartBrushOp(ScMap map,
        ushort[] beforeHm, byte[] beforeLow, byte[] beforeHigh, TerrainTextureSnapshot[] beforeTextures,
        ushort[] afterHm, byte[] afterLow, byte[] afterHigh, TerrainTextureSnapshot[] afterTextures)
    {
        _map = map;
        _beforeHm = beforeHm; _afterHm = afterHm;
        _beforeLow = beforeLow; _afterLow = afterLow;
        _beforeHigh = beforeHigh; _afterHigh = afterHigh;
        _beforeTextures = beforeTextures; _afterTextures = afterTextures;
    }

    public void Execute()
    {
        _map.Heightmap.Data = _afterHm;
        _map.TextureMaskLow.DdsData = _afterLow;
        _map.TextureMaskHigh.DdsData = _afterHigh;
        ApplyTextureSnapshots(_afterTextures);
    }

    public void Undo()
    {
        _map.Heightmap.Data = _beforeHm;
        _map.TextureMaskLow.DdsData = _beforeLow;
        _map.TextureMaskHigh.DdsData = _beforeHigh;
        ApplyTextureSnapshots(_beforeTextures);
    }

    private void ApplyTextureSnapshots(TerrainTextureSnapshot[] snapshots)
    {
        int n = Math.Min(snapshots.Length, _map.TerrainTextures.Length);
        for (int i = 0; i < n; i++) snapshots[i].ApplyTo(_map.TerrainTextures[i]);
    }
}

/// <summary>
/// Helpers for the Smart-texturing toggle: classify a heightmap pixel by altitude/slope/water
/// proximity, paint the corresponding strata, and auto-assign missing-category textures from a
/// library when the map has free strata slots. The orchestration (snapshots, push op) lives in the
/// ViewModel because it depends on services like the texture library.
/// </summary>
public static class SmartBrushTool
{
    private const int DdsHeader = 128;

    public enum TerrainCategory
    {
        SeaFloor,
        Beach,
        Grass,
        Plateau,
        Dirt,
        Rock,
        Snow,
    }

    /// <summary>
    /// Texture classification is fully deterministic via <see cref="TextureCategoryTable"/> — every
    /// known vanilla SC texture has an explicit category, and unknown textures return null (Smart
    /// leaves them alone). No keyword guessing.
    /// </summary>
    public static TerrainCategory? Classify(string? albedoPath) => TextureCategoryTable.Classify(albedoPath);

    public static TerrainCategory Classify(float height, float slope, float waterLevel)
    {
        if (height < waterLevel - 2f) return TerrainCategory.SeaFloor;
        if (height < waterLevel + 3f) return TerrainCategory.Beach;
        // Peaks: very high AND flat.
        if (height > 80f && slope < 0.5f) return TerrainCategory.Snow;
        // Very steep anywhere = rock (cliffs, sheer slopes).
        if (slope > 1.0f) return TerrainCategory.Rock;
        // High AND steep = rocky face of a mountain.
        if (height > 60f && slope > 0.5f) return TerrainCategory.Rock;
        // Flat plateau at altitude.
        if (height > 50f && slope < 0.3f) return TerrainCategory.Plateau;
        // Below ~30 = lowland: always grass unless extremely steep (caught above).
        // This is what makes a flattened/lowered area return to grass even if there's residual
        // slope during the transition.
        if (height < waterLevel + 12f) return TerrainCategory.Grass;
        // Mid altitude with moderate slope = dirt (transitional terrain).
        if (slope > 0.5f) return TerrainCategory.Dirt;
        return TerrainCategory.Grass;
    }

    /// <summary>Match the map's already-assigned strata to categories using the explicit
    /// <see cref="TextureCategoryTable"/>. The first strata whose <c>AlbedoPath</c> resolves to
    /// a category wins. Unknown textures are logged once so the table can be extended.</summary>
    public static Dictionary<TerrainCategory, int> ResolveCategoryStrata(ScMap map)
    {
        var result = new Dictionary<TerrainCategory, int>();
        int upper = Math.Min(8, map.TerrainTextures.Length - 1);
        for (int i = 1; i <= upper; i++)
        {
            var path = map.TerrainTextures[i].AlbedoPath;
            if (string.IsNullOrEmpty(path)) continue;
            var cat = Classify(path);
            if (cat == null)
            {
                Services.DebugLog.Write($"[Smart] Strata {i}: unclassified texture '{path}' — add to TextureCategoryTable.");
                continue;
            }
            if (!result.ContainsKey(cat.Value)) result[cat.Value] = i;
        }
        return result;
    }

    /// <summary>For each affected pixel in the brush radius, classify by altitude/slope and paint
    /// the matching strata into the splatmap. The target category's channel is INCREMENTED while
    /// every other resolved-category channel is DECREMENTED — so transitions actually swap
    /// textures instead of just stacking (e.g. flattening a mountain back to grass really shows
    /// grass instead of being shaded by leftover rock channel).</summary>
    public static void ApplyTexturePass(ScMap map, Dictionary<TerrainCategory, int> categoryStrata,
        float centerX, float centerZ, float radius, float strength)
    {
        var hm = map.Heightmap;
        int cx = (int)centerX;
        int cz = (int)centerZ;
        int r = (int)MathF.Ceiling(radius);
        int xMin = Math.Max(0, cx - r);
        int xMax = Math.Min(hm.Width, cx + r);
        int zMin = Math.Max(0, cz - r);
        int zMax = Math.Min(hm.Height, cz + r);
        float waterLevel = map.Water.HasWater ? map.Water.Elevation : 0f;
        // Smart aggressively claims ownership of the splatmap while it's painting: any strata
        // channel that isn't the target on this pixel gets pulled toward 0. This guarantees that
        // a "flank rock" silhouette actually disappears when the area is flattened, even if our
        // keyword heuristic failed to classify some strata (e.g. unusual albedo file name).
        // User caveat: don't keep Smart on if you want to preserve hand-painted texture layers.
        int maxStrata = Math.Min(8, map.TerrainTextures.Length - 1);

        for (int z = zMin; z <= zMax; z++)
        {
            for (int x = xMin; x <= xMax; x++)
            {
                float dx = x - centerX;
                float dz = z - centerZ;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                if (dist > radius) continue;
                float falloff = 1f - dist / radius;
                falloff *= falloff;

                float h = hm.GetWorldHeight(x, z);
                float slope = ComputeSlope(hm, x, z);
                var cat = Classify(h, slope, waterLevel);
                if (!categoryStrata.TryGetValue(cat, out int targetStrata) || targetStrata < 1) continue;

                float delta = strength * falloff * 0.025f * 255f;
                int addDelta = (int)delta;
                // Erase faster than we paint. Sequential mix() in the shader means rock at 255
                // covers fresh grass at 50; without an asymmetric rate, transitions stay invisible
                // for the first half of a stroke.
                int subDelta = (int)(delta * 2.5f);

                AdjustStrataPixel(map, targetStrata, x, z, +addDelta);
                for (int s = 1; s <= maxStrata; s++)
                {
                    if (s == targetStrata) continue;
                    AdjustStrataPixel(map, s, x, z, -subDelta);
                }
            }
        }
    }

    private static float ComputeSlope(Heightmap hm, int x, int z)
    {
        int xL = Math.Max(0, x - 1);
        int xR = Math.Min(hm.Width, x + 1);
        int zU = Math.Max(0, z - 1);
        int zD = Math.Min(hm.Height, z + 1);
        float gx = (hm.GetWorldHeight(xR, z) - hm.GetWorldHeight(xL, z)) * 0.5f;
        float gz = (hm.GetWorldHeight(x, zD) - hm.GetWorldHeight(x, zU)) * 0.5f;
        return MathF.Sqrt(gx * gx + gz * gz);
    }

    /// <summary>Add <paramref name="deltaUnits"/> to (or subtract from) a strata's channel at one
    /// heightmap pixel, clamped to 0..255. Negative delta = erase (used to remove a different
    /// category's contribution when painting a new one).</summary>
    private static void AdjustStrataPixel(ScMap map, int strata, int hx, int hz, int deltaUnits)
    {
        var mask = strata <= 4 ? map.TextureMaskLow : map.TextureMaskHigh;
        if (mask.Width <= 0 || mask.Height <= 0) return;
        if (mask.DdsData.Length < DdsHeader + mask.Width * mask.Height * 4) return;

        int withinMask = ((strata - 1) % 4) + 1;
        int channelOffset = withinMask switch { 1 => 2, 2 => 1, 3 => 0, 4 => 3, _ => 0 };

        float scaleX = (float)mask.Width / Math.Max(1, map.Heightmap.Width);
        float scaleZ = (float)mask.Height / Math.Max(1, map.Heightmap.Height);
        int sx = Math.Clamp((int)MathF.Floor(hx * scaleX), 0, mask.Width - 1);
        int sz = Math.Clamp((int)MathF.Floor(hz * scaleZ), 0, mask.Height - 1);

        int idx = DdsHeader + (sz * mask.Width + sx) * 4 + channelOffset;
        int newVal = Math.Clamp(mask.DdsData[idx] + deltaUnits, 0, 255);
        mask.DdsData[idx] = (byte)newVal;
    }

    /// <summary>Snapshot all <see cref="TerrainTexture"/> slots of a map.</summary>
    public static TerrainTextureSnapshot[] SnapshotTextures(ScMap map)
    {
        var arr = new TerrainTextureSnapshot[map.TerrainTextures.Length];
        for (int i = 0; i < arr.Length; i++)
            arr[i] = TerrainTextureSnapshot.Of(map.TerrainTextures[i]);
        return arr;
    }

    public static bool ArraysEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    public static bool ArraysEqual(ushort[] a, ushort[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    public static bool TexturesEqual(TerrainTextureSnapshot[] a, TerrainTextureSnapshot[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) return false;
        return true;
    }
}
