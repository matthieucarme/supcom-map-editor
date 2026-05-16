using System.Numerics;
using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Services;

/// <summary>How the map is divided for one-shot duplication.</summary>
public enum SymmetryPattern
{
    Vertical,        // left | right
    Horizontal,      // top  / bottom
    DiagonalTLBR,    // ↘ (top-left → bottom-right diagonal)
    DiagonalTRBL,    // ↙ (top-right → bottom-left diagonal)
    QuadCross,       // both vertical + horizontal → 4 quadrants
    QuadDiagonals    // both diagonals → 4 triangles (N/E/S/W)
}

/// <summary>How the source region is propagated into the others.</summary>
public enum SymmetryMode
{
    /// <summary>Reflect across the dividing axis. Both regions read as a mirror image — a ramp
    /// pointing east in the source half also points east in the mirrored half.</summary>
    Mirror,
    /// <summary>Rotate around the map's center. For 2-region patterns this is a 180° rotation
    /// (a ramp pointing east becomes a ramp pointing west on the other side). For 4-region
    /// patterns it's a 4-fold rotation (each region is the previous one turned 90°). This is the
    /// standard symmetry for competitive maps because both players get strictly identical layouts
    /// including asymmetric features.</summary>
    Rotational,
}

/// <summary>
/// Region of a map under a symmetry pattern. Interpretation depends on the pattern:
///   Vertical:        Left=0, Right=1
///   Horizontal:      Top=0,  Bottom=1
///   DiagonalTLBR:    TopRight=0, BottomLeft=1
///   DiagonalTRBL:    TopLeft=0,  BottomRight=1
///   QuadCross:       TL=0, TR=1, BL=2, BR=3
///   QuadDiagonals:   N=0,  E=1,  S=2,  W=3
/// </summary>
public enum SymmetryRegion { R0 = 0, R1 = 1, R2 = 2, R3 = 3 }

/// <summary>
/// Mirrors heightmap, splatmaps, and markers according to a chosen pattern.
/// The "source" region's content is replicated into every other region, replacing it.
/// </summary>
public static class SymmetryService
{
    public static int RegionCount(SymmetryPattern p) => p is SymmetryPattern.QuadCross or SymmetryPattern.QuadDiagonals ? 4 : 2;

    /// <summary>
    /// Identify which region a normalized point (u,v) in [0,1]² belongs to under a pattern.
    /// </summary>
    public static SymmetryRegion RegionOf(SymmetryPattern p, float u, float v)
    {
        return p switch
        {
            SymmetryPattern.Vertical     => u < 0.5f ? SymmetryRegion.R0 : SymmetryRegion.R1,
            SymmetryPattern.Horizontal   => v < 0.5f ? SymmetryRegion.R0 : SymmetryRegion.R1,
            SymmetryPattern.DiagonalTLBR => v < u    ? SymmetryRegion.R0 : SymmetryRegion.R1,
            SymmetryPattern.DiagonalTRBL => (u + v) < 1f ? SymmetryRegion.R0 : SymmetryRegion.R1,
            SymmetryPattern.QuadCross    => (SymmetryRegion)((u >= 0.5f ? 1 : 0) | (v >= 0.5f ? 2 : 0)),
            SymmetryPattern.QuadDiagonals => RegionOfDiagonals(u, v),
            _ => SymmetryRegion.R0,
        };
    }

    private static SymmetryRegion RegionOfDiagonals(float u, float v)
    {
        // 4 triangles defined by both diagonals (image coords, v=0 top):
        // N: v < u AND v < 1-u
        // E: v < u AND v >= 1-u
        // S: v >= u AND v >= 1-u
        // W: v >= u AND v < 1-u
        bool belowD1 = v < u;        // u=v is D1 (TL→BR)
        bool belowD2 = (u + v) < 1f; // u+v=1 is D2 (TR→BL)
        if (belowD1 && belowD2) return SymmetryRegion.R0; // N
        if (belowD1 && !belowD2) return SymmetryRegion.R1; // E
        if (!belowD1 && !belowD2) return SymmetryRegion.R2; // S
        return SymmetryRegion.R3; // W
    }

    /// <summary>
    /// Given a destination point (u,v) and the chosen source region, return the source point
    /// in [0,1]² whose value should be copied to (u,v). For points already in the source region
    /// this is the identity. Used by the per-pixel warp of heightmap and splatmaps.
    /// </summary>
    public static (float u, float v) SourceOf(SymmetryPattern p, SymmetryRegion source, float u, float v, SymmetryMode mode = SymmetryMode.Mirror)
    {
        if (mode == SymmetryMode.Rotational)
            return RotationalSourceOf(p, source, u, v);

        return p switch
        {
            SymmetryPattern.Vertical => source switch
            {
                SymmetryRegion.R0 => (Math.Min(u, 1f - u), v),
                _                 => (Math.Max(u, 1f - u), v),
            },
            SymmetryPattern.Horizontal => source switch
            {
                SymmetryRegion.R0 => (u, Math.Min(v, 1f - v)),
                _                 => (u, Math.Max(v, 1f - v)),
            },
            SymmetryPattern.DiagonalTLBR => DiagTLBR(u, v, source),
            SymmetryPattern.DiagonalTRBL => DiagTRBL(u, v, source),
            SymmetryPattern.QuadCross    => QuadCross(u, v, source),
            SymmetryPattern.QuadDiagonals => QuadDiag(u, v, source),
            _ => (u, v),
        };
    }

    /// <summary>
    /// Forward map: given a seed at (u,v) inside the source region, return where it lands in the
    /// destination region. For mirror symmetry the inverse is its own forward (involution), so we
    /// can reuse <see cref="SourceOf"/>. For rotational symmetry the 90° chain is NOT involutive,
    /// so we need the proper forward transform here.
    /// </summary>
    public static (float u, float v) DestOf(SymmetryPattern p, SymmetryRegion source, SymmetryRegion dest, float u, float v, SymmetryMode mode = SymmetryMode.Mirror)
    {
        if (mode == SymmetryMode.Mirror)
            return SourceOf(p, dest, u, v, SymmetryMode.Mirror);
        return RotationalDestOf(p, source, dest, u, v);
    }

    /// <summary>Source-of mapping under rotational mode (backward direction).</summary>
    private static (float u, float v) RotationalSourceOf(SymmetryPattern p, SymmetryRegion source, float u, float v)
    {
        var current = RegionOf(p, u, v);
        if (current == source) return (u, v);

        if (RegionCount(p) == 2)
        {
            // 180° rotation is its own inverse — same formula regardless of the dividing axis.
            return (1f - u, 1f - v);
        }

        // 4-fold rotation: the forward map takes (source→current) in N CW steps, so the inverse
        // takes (current→source) in N CCW steps. N = chain_order(current) - chain_order(source).
        int steps = (ChainOrder(p, current) - ChainOrder(p, source) + 4) % 4;
        return Rotate90CCW(u, v, steps);
    }

    /// <summary>Forward map under rotational mode (seed in source → corresponding point in dest).</summary>
    private static (float u, float v) RotationalDestOf(SymmetryPattern p, SymmetryRegion source, SymmetryRegion dest, float u, float v)
    {
        if (source == dest) return (u, v);

        if (RegionCount(p) == 2)
        {
            // 180° rotation is its own inverse — forward and backward are identical.
            return (1f - u, 1f - v);
        }

        // 4-fold rotation: CW steps from `source` to `dest`.
        int steps = (ChainOrder(p, dest) - ChainOrder(p, source) + 4) % 4;
        return Rotate90CW(u, v, steps);
    }

    /// <summary>Position of a region in the clockwise rotation chain used by 4-fold rotational
    /// symmetry. QuadDiagonals enum is already in CW order (N=0, E=1, S=2, W=3); QuadCross needs a
    /// re-map because its enum has TL=0, TR=1, BL=2, BR=3 but the CW chain is TL→TR→BR→BL.</summary>
    private static int ChainOrder(SymmetryPattern p, SymmetryRegion r)
    {
        if (p == SymmetryPattern.QuadCross)
        {
            return r switch
            {
                SymmetryRegion.R0 => 0, // TL
                SymmetryRegion.R1 => 1, // TR
                SymmetryRegion.R3 => 2, // BR
                SymmetryRegion.R2 => 3, // BL
                _ => 0,
            };
        }
        return (int)r;
    }

    private static (float u, float v) Rotate90CW(float u, float v, int steps)
    {
        // 90° CW around center (0.5, 0.5): (u, v) → (1 - v, u)
        for (int i = 0; i < steps; i++) (u, v) = (1f - v, u);
        return (u, v);
    }

    private static (float u, float v) Rotate90CCW(float u, float v, int steps)
    {
        // 90° CCW around center: (u, v) → (v, 1 - u)
        for (int i = 0; i < steps; i++) (u, v) = (v, 1f - u);
        return (u, v);
    }

    private static (float, float) DiagTLBR(float u, float v, SymmetryRegion src)
    {
        // Reflection across the line u=v.
        bool inR0 = v < u; // top-right triangle
        bool srcIsR0 = src == SymmetryRegion.R0;
        return inR0 == srcIsR0 ? (u, v) : (v, u);
    }

    private static (float, float) DiagTRBL(float u, float v, SymmetryRegion src)
    {
        // Reflection across the line u+v=1.
        bool inR0 = (u + v) < 1f; // top-left triangle
        bool srcIsR0 = src == SymmetryRegion.R0;
        return inR0 == srcIsR0 ? (u, v) : (1f - v, 1f - u);
    }

    private static (float, float) QuadCross(float u, float v, SymmetryRegion src)
    {
        // Fold the point into the source quadrant by reflecting axes as needed.
        int destQ = (u >= 0.5f ? 1 : 0) | (v >= 0.5f ? 2 : 0);
        int sxor = destQ ^ (int)src;
        float su = (sxor & 1) != 0 ? 1f - u : u;
        float sv = (sxor & 2) != 0 ? 1f - v : v;
        return (su, sv);
    }

    private static (float, float) QuadDiag(float u, float v, SymmetryRegion src)
    {
        // Reflections {id, D1, 180°, D2} form the Klein 4-group. Compose the transform that
        // brings the destination triangle onto the source triangle.
        var dest = RegionOfDiagonals(u, v);
        if (dest == src) return (u, v);
        // Table[d][s] gives the transform code: 0=id, 1=D1 swap, 2=D2 antiswap, 3=180° rotate
        // Built from the inverse mappings (each reflection is its own inverse).
        // N(0): id=N, D1=E, 180=S, D2=W → table row [0,1,3,2]
        // Wait — let me redo from the group action: from d to s, apply transform g such that g(d)=s.
        // Group ops in the Klein 4-group of reflections {id, D1, D2, rot180}:
        //   id   maps N→N, E→W, S→S, W→E (no, id is identity by definition)
        // Let me redefine using the symmetry of the regions:
        //   D1 (u↔v) maps: N↔W, E↔S (because reflecting across line u=v flips the triangles either side)
        //   D2 (u↔1-v, v↔1-u) maps: N↔E, S↔W
        //   rot180 (u→1-u, v→1-v) maps: N↔S, E↔W
        // Table[d,s] = op to apply on a destination point to land in source:
        //   d=N: s=N→id, s=E→D2, s=S→rot, s=W→D1
        //   d=E: s=N→D2, s=E→id, s=S→D1, s=W→rot
        //   d=S: s=N→rot, s=E→D1, s=S→id, s=W→D2
        //   d=W: s=N→D1, s=E→rot, s=S→D2, s=W→id
        int[,] op = {
            { 0, 2, 3, 1 },
            { 2, 0, 1, 3 },
            { 3, 1, 0, 2 },
            { 1, 3, 2, 0 },
        };
        int code = op[(int)dest, (int)src];
        return code switch
        {
            0 => (u, v),
            1 => (v, u),               // D1
            2 => (1f - v, 1f - u),     // D2
            3 => (1f - u, 1f - v),     // rot180
            _ => (u, v),
        };
    }

    // ============================================================
    // Apply: mirror heightmap, splatmaps, and every scene entity.
    // ============================================================

    /// <summary>Mirror just the heightmap + splatmaps. Used by the procedural map generator where
    /// markers are already placed correctly per the team configuration — calling the full Apply
    /// would wipe one team's spawns and replace them with mirrors of the other (breaks 2v6 etc.).</summary>
    public static void ApplyTerrainOnly(ScMap map, SymmetryPattern pattern, SymmetryRegion source, SymmetryMode mode = SymmetryMode.Mirror)
    {
        MirrorHeightmap(map.Heightmap, pattern, source, mode);
        MirrorSplatmap(map.TextureMaskLow, pattern, source, mode);
        MirrorSplatmap(map.TextureMaskHigh, pattern, source, mode);
    }

    /// <summary>Mirror just the resource markers (Mass + Hydrocarbon). Heightmap, splatmaps,
    /// non-resource markers, props, and pre-placed units stay untouched. Useful when the user
    /// has a hand-tuned terrain and only wants to enforce balanced resource placement.</summary>
    public static void ApplyResourcesOnly(ScMap map, SymmetryPattern pattern, SymmetryRegion source, SymmetryMode mode = SymmetryMode.Mirror)
    {
        MirrorEntities(
            map, map.Markers, pattern, source, mode,
            getPos: m => m.Position,
            setPos: (m, p) => m.Position = p,
            clone: CloneMarker,
            renameForRegion: (m, _, list) => m.Name = MakeUniqueMarkerName(m.Name, list),
            includeFilter: IsResourceMarker);
    }

    private static bool IsResourceMarker(Marker m) =>
        m.Type == MarkerType.Mass || m.Type == MarkerType.Hydrocarbon;

    public static void Apply(ScMap map, SymmetryPattern pattern, SymmetryRegion source, SymmetryMode mode = SymmetryMode.Mirror)
    {
        MirrorHeightmap(map.Heightmap, pattern, source, mode);
        MirrorSplatmap(map.TextureMaskLow, pattern, source, mode);
        MirrorSplatmap(map.TextureMaskHigh, pattern, source, mode);
        MirrorMarkers(map, pattern, source, mode);
        MirrorProps(map, pattern, source, mode);
        MirrorUnitSpawns(map, pattern, source, mode);
    }

    private static void MirrorHeightmap(Heightmap hm, SymmetryPattern pattern, SymmetryRegion source, SymmetryMode mode)
    {
        int w = hm.Width;
        int h = hm.Height;
        int stride = w + 1;
        var snapshot = (ushort[])hm.Data.Clone();

        for (int y = 0; y <= h; y++)
        {
            for (int x = 0; x <= w; x++)
            {
                float u = (float)x / w;
                float v = (float)y / h;
                var (su, sv) = SourceOf(pattern, source, u, v, mode);
                int sx = (int)MathF.Round(su * w);
                int sy = (int)MathF.Round(sv * h);
                sx = Math.Clamp(sx, 0, w);
                sy = Math.Clamp(sy, 0, h);
                hm.Data[y * stride + x] = snapshot[sy * stride + sx];
            }
        }
    }

    private static void MirrorSplatmap(TextureMask mask, SymmetryPattern pattern, SymmetryRegion source, SymmetryMode mode)
    {
        // Raw ARGB DDS: 128-byte header then W*H*4 pixel bytes
        if (mask.DdsData.Length <= 128 || mask.Width <= 0 || mask.Height <= 0) return;
        const int header = 128;
        int w = mask.Width;
        int h = mask.Height;
        int expected = header + w * h * 4;
        if (mask.DdsData.Length < expected) return;

        // Snapshot the pixel block so we can read source while writing destination
        var pixels = new byte[w * h * 4];
        Buffer.BlockCopy(mask.DdsData, header, pixels, 0, pixels.Length);

        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h;
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                var (su, sv) = SourceOf(pattern, source, u, v, mode);
                int sx = Math.Clamp((int)MathF.Floor(su * w), 0, w - 1);
                int sy = Math.Clamp((int)MathF.Floor(sv * h), 0, h - 1);
                int srcIdx = (sy * w + sx) * 4;
                int dstIdx = (y * w + x) * 4;
                mask.DdsData[header + dstIdx]     = pixels[srcIdx];
                mask.DdsData[header + dstIdx + 1] = pixels[srcIdx + 1];
                mask.DdsData[header + dstIdx + 2] = pixels[srcIdx + 2];
                mask.DdsData[header + dstIdx + 3] = pixels[srcIdx + 3];
            }
        }
    }

    private static void MirrorMarkers(ScMap map, SymmetryPattern pattern, SymmetryRegion source, SymmetryMode mode) =>
        MirrorEntities(
            map, map.Markers, pattern, source, mode,
            getPos: m => m.Position,
            setPos: (m, p) => m.Position = p,
            clone: CloneMarker,
            renameForRegion: (m, _, list) => m.Name = MakeUniqueMarkerName(m.Name, list));

    private static void MirrorProps(ScMap map, SymmetryPattern pattern, SymmetryRegion source, SymmetryMode mode) =>
        MirrorEntities(
            map, map.Props, pattern, source, mode,
            getPos: p => p.Position,
            setPos: (p, v) => p.Position = v,
            clone: CloneProp,
            renameForRegion: null);

    private static void MirrorUnitSpawns(ScMap map, SymmetryPattern pattern, SymmetryRegion source, SymmetryMode mode)
    {
        // Each army owns its own InitialUnits list; mirror within each list independently so
        // names stay unique per army (UNIT_N) and ownership is preserved.
        foreach (var army in map.Info.Armies)
        {
            var list = army.InitialUnits;
            MirrorEntities(
                map, list, pattern, source, mode,
                getPos: u => u.Position,
                setPos: (u, p) => u.Position = p,
                clone: CloneUnitSpawn,
                renameForRegion: (u, _, all) => u.Name = MakeUniqueUnitName(all));
        }
    }

    /// <summary>
    /// Wipe entities outside the source region, then duplicate every seed from the source region into
    /// each other region with its position reflected. Same algorithm for markers, props, and units.
    /// </summary>
    private static void MirrorEntities<T>(
        ScMap map, List<T> entities,
        SymmetryPattern pattern, SymmetryRegion source, SymmetryMode mode,
        Func<T, Vector3> getPos,
        Action<T, Vector3> setPos,
        Func<T, T> clone,
        Action<T, SymmetryRegion, List<T>>? renameForRegion,
        Func<T, bool>? includeFilter = null)
    {
        var hm = map.Heightmap;
        int w = hm.Width;
        int h = hm.Height;
        var originals = entities.ToList();
        entities.Clear();

        // When a filter is set, entities that don't match it are preserved verbatim (added back
        // first so they keep their original ordering). Only matching entities go through the
        // mirror routine. Without a filter, everything is fair game (legacy behaviour).
        if (includeFilter != null)
        {
            foreach (var e in originals)
                if (!includeFilter(e)) entities.Add(e);
        }

        // Identify which originals lie in the source region — these are the seeds. Filtered-out
        // entities are skipped here too so they don't get duplicated into other regions.
        var seeds = new List<T>();
        foreach (var e in originals)
        {
            if (includeFilter != null && !includeFilter(e)) continue;
            var p = getPos(e);
            float u = w > 0 ? Math.Clamp(p.X / w, 0f, 1f) : 0.5f;
            float v = h > 0 ? Math.Clamp(p.Z / h, 0f, 1f) : 0.5f;
            if (RegionOf(pattern, u, v) == source)
                seeds.Add(e);
        }

        // Step 1: re-add the source region first so rename-for-region sees them as taken slots
        // when handing out unique names in subsequent regions. Otherwise the first mirrored copy
        // would claim the seed's own name.
        foreach (var seed in seeds)
            entities.Add(clone(seed));

        // Step 2: forward-mirror into every other region. Mirror mode reuses SourceOf via DestOf's
        // involution shortcut; rotational mode goes through the proper 90°-chain forward map.
        int regions = RegionCount(pattern);
        for (int r = 0; r < regions; r++)
        {
            var region = (SymmetryRegion)r;
            if (region == source) continue;

            foreach (var seed in seeds)
            {
                var copy = clone(seed);
                var p = getPos(seed);
                float u = w > 0 ? p.X / w : 0.5f;
                float v = h > 0 ? p.Z / h : 0.5f;
                var (du, dv) = DestOf(pattern, source, region, u, v, mode);
                setPos(copy, new Vector3(du * w, p.Y, dv * h));
                renameForRegion?.Invoke(copy, region, entities);
                entities.Add(copy);
            }
        }
    }

    private static Marker CloneMarker(Marker src) => new()
    {
        Name = src.Name,
        Type = src.Type,
        Position = src.Position,
        Orientation = src.Orientation,
        Color = src.Color,
        Hint = src.Hint,
        AdjacentMarkers = [..src.AdjacentMarkers],
        Graph = src.Graph,
        Resource = src.Resource,
        Amount = src.Amount,
        Zoom = src.Zoom,
        CanSetCamera = src.CanSetCamera,
        CanSyncCamera = src.CanSyncCamera,
        EffectTemplate = src.EffectTemplate,
        Scale = src.Scale,
        WeatherType = src.WeatherType,
    };

    private static Prop CloneProp(Prop src) => new()
    {
        BlueprintPath = src.BlueprintPath,
        Position = src.Position,
        RotationX = src.RotationX,
        RotationY = src.RotationY,
        RotationZ = src.RotationZ,
        Scale = src.Scale,
    };

    private static UnitSpawn CloneUnitSpawn(UnitSpawn src) => new()
    {
        Name = src.Name,
        BlueprintId = src.BlueprintId,
        Position = src.Position,
        Orientation = src.Orientation,
        Platoon = src.Platoon,
        Orders = src.Orders,
    };

    private static string MakeUniqueMarkerName(string baseName, List<Marker> existing)
    {
        // For ARMY_N markers, increment N to the next free slot
        if (baseName.StartsWith("ARMY_", StringComparison.OrdinalIgnoreCase))
        {
            for (int n = 1; n < 32; n++)
            {
                string candidate = $"ARMY_{n}";
                if (!existing.Any(m => string.Equals(m.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                    return candidate;
            }
        }
        // For numbered names like "Mass 03", bump the number until free
        string trimmed = baseName.TrimEnd();
        int lastSpace = trimmed.LastIndexOf(' ');
        string prefix = lastSpace >= 0 ? trimmed[..lastSpace] : trimmed;
        for (int i = 1; i < 1000; i++)
        {
            string candidate = $"{prefix} {i:D2}";
            if (!existing.Any(m => string.Equals(m.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
        return baseName + "_copy";
    }

    /// <summary>UNIT_N convention used by SC for INITIAL units inside an army.</summary>
    private static string MakeUniqueUnitName(List<UnitSpawn> existing)
    {
        for (int n = 0; n < 10000; n++)
        {
            string candidate = $"UNIT_{n}";
            if (!existing.Any(u => string.Equals(u.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
        return $"UNIT_{existing.Count}";
    }
}
