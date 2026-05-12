using System.Numerics;
using SupremeCommanderEditor.Core.Formats.Scmap;
using SupremeCommanderEditor.Core.Models;
using SupremeCommanderEditor.Core.Operations;

namespace SupremeCommanderEditor.Core.Services;

public class MapGenerationOptions
{
    public long Seed { get; set; } = 42;
    public int Size { get; set; } = 512;
    public bool HasWater { get; set; } = true;
    public string MapName { get; set; } = "Generated Map";

    /// <summary>Player count for each team. Length = number of teams. Total players capped at 8 by
    /// the engine. Examples: {1,1} = 1v1, {2,6} = 2v6, {2,2,2,2} = four 2-player teams, {1,1,1,1,1,1,1,1} = 8-FFA.</summary>
    public List<int> TeamPlayerCounts { get; set; } = new() { 4, 4 };

    /// <summary>Total Mass markers for each team. Must have the same length as <see cref="TeamPlayerCounts"/>.
    /// Each team's count is distributed evenly across its spawns (any remainder spread to the first
    /// few spawns).</summary>
    public List<int> TeamMassCounts { get; set; } = new() { 16, 16 };

    /// <summary>null = no symmetry applied; otherwise the heightmap+splatmap+markers are mirrored
    /// after generation using <see cref="SymmetryService"/>.</summary>
    public SymmetryPattern? Symmetry { get; set; }

    /// <summary>Texture paths to assign per smart category. Strata 1..7 are populated in this
    /// fixed order: Grass / Rock / Dirt / Beach / Snow / Plateau / SeaFloor — and the splatmap
    /// channel is painted based on per-pixel classification.</summary>
    public Dictionary<SmartBrushTool.TerrainCategory, string> TexturesByCategory { get; set; } = new();
    public Dictionary<SmartBrushTool.TerrainCategory, string?> NormalsByCategory { get; set; } = new();

    /// <summary>Optional macro/overlay texture (assigned to strata 9). The shader blends this on
    /// top of everything via the texture's own alpha — if it's left empty the magenta fallback
    /// would tint the whole map pink. Caller should pick a real "macrotexture*" entry from the
    /// library if available; null is replaced with the Grass texture as a safe default.</summary>
    public string? MacroTexturePath { get; set; }
}

/// <summary>
/// Procedural map generator. Given a <see cref="MapGenerationOptions"/> (including a seed), produces
/// a deterministic <see cref="ScMap"/> with heightmap, splatmap painted via smart-category logic,
/// spawn markers, and Mass markers. Optionally applies a symmetry pattern at the end.
/// </summary>
public static class MapGenerator
{
    // SC1 vanilla v53 layout: 6 strata total = base(0) + 4 splatmap-blended (1..4) + macro(5).
    // We don't write a 10-slot v53 because SC1's engine crashes loading those. So we pick the
    // 4 most useful smart categories for slots 1..4 — Beach and SeaFloor are skipped, which is
    // fine for most maps (SeaFloor is replaced visually by the water plane, Beach is rare).
    private const int VanillaMacroSlot = 5;
    private static readonly (SmartBrushTool.TerrainCategory cat, int strata)[] StrataOrder =
    {
        (SmartBrushTool.TerrainCategory.Rock,     1),
        (SmartBrushTool.TerrainCategory.Dirt,     2),
        (SmartBrushTool.TerrainCategory.Snow,     3),
        (SmartBrushTool.TerrainCategory.Plateau,  4),
    };

    public static ScMap Generate(MapGenerationOptions opts)
    {
        int size = opts.Size;
        int totalPlayers = Math.Clamp(opts.TeamPlayerCounts.Sum(), 1, 16);
        DebugLog.Write($"[MapGen] Seed={opts.Seed} Size={size} Water={opts.HasWater} Teams=[{string.Join(",", opts.TeamPlayerCounts)}] Mass=[{string.Join(",", opts.TeamMassCounts)}] Symmetry={opts.Symmetry?.ToString() ?? "None"}");
        DebugLog.Write($"[MapGen] TexturesByCategory: {opts.TexturesByCategory.Count} entries");
        foreach (var kv in opts.TexturesByCategory) DebugLog.Write($"[MapGen]   {kv.Key} → {kv.Value}");
        // Target vanilla SupCom (v53) so generated maps open in the original 2007 game, not just
        // Forged Alliance. v53 still supports a multi-layer texture array + splatmap, so the
        // generator's smart-classified strata layout works fine.
        var map = NewMapService.CreateBlankMap(size, Math.Min(8, totalPlayers), opts.MapName, versionMinor: 53);

        map.Water.HasWater = opts.HasWater;
        map.Water.Elevation       = opts.HasWater ? 25f : 0f;
        map.Water.ElevationDeep   = opts.HasWater ? 20f : 0f;
        map.Water.ElevationAbyss  = opts.HasWater ? 10f : 0f;

        // === Pre-compute spawn target positions (geometric, no heightmap dependency).
        // This is what lets us carve flat plateaus into the heightmap at exactly those positions
        // before any noise is laid down — so spawns end up on real flat ground every time, not
        // wherever a spiral search happens to find land. ===
        var placeRng = new Random(unchecked((int)(opts.Seed * 31 + 1337)));
        var spawnTargets = ComputeSpawnTargets(map.Heightmap.Width, map.Heightmap.Height, opts.TeamPlayerCounts, placeRng);

        // === Heightmap with plateau carving at the spawn targets ===
        var heightRng = new PerlinNoise(opts.Seed);
        GenerateHeightmap(map, heightRng, opts, spawnTargets);

        // === Textures + splatmap (smart-classified per height/slope) ===
        AssignStrataTextures(map, opts);
        PaintSplatmaps(map, opts);

        // === Symmetry on the TERRAIN only, applied before markers so plateaus survive the mirror.
        // We never mirror markers here : the team config (e.g. 2v6) is the source of truth for who
        // plays where, and a full marker mirror would wipe the larger team and replicate the
        // smaller one. The terrain mirror also requires team layouts that match the symmetry
        // (2 teams of equal size for 2-region patterns, 4 of equal size for 4-region patterns) —
        // otherwise mirroring R0 over R1+ would erase the larger team's plateaus. ===
        if (opts.Symmetry is SymmetryPattern pattern && TeamsCompatibleWith(pattern, opts.TeamPlayerCounts))
        {
            SymmetryService.ApplyTerrainOnly(map, pattern, SymmetryRegion.R0);
        }
        else if (opts.Symmetry is not null)
        {
            DebugLog.Write($"[MapGen] Symmetry {opts.Symmetry} skipped — team config {string.Join(",", opts.TeamPlayerCounts)} is not compatible (need equal-size teams matching the pattern's region count).");
        }

        // === Place spawn markers at the pre-computed targets and drop masses in tight rings around
        // each spawn, all inside the plateau zone. ===
        PlaceMarkersOnPlateaus(map, opts, spawnTargets);

        return map;
    }

    /// <summary>Plateau geometry — kept as constants so both the heightmap carver and the marker
    /// placer agree on how big the flat zone is.</summary>
    private const float PlateauFlatRadius  = 38f;   // perfectly flat disc around each spawn
    private const float PlateauBlendRadius = 90f;   // smoothstep transition out to full noise

    /// <summary>True iff the chosen symmetry pattern can be applied without destroying the team
    /// layout : same player count on every team, and exactly as many teams as the pattern has
    /// regions (2 for mirror patterns, 4 for quad patterns).</summary>
    private static bool TeamsCompatibleWith(SymmetryPattern pattern, List<int> teamSizes)
    {
        if (teamSizes.Count == 0) return false;
        int regions = SymmetryService.RegionCount(pattern);
        if (teamSizes.Count != regions) return false;
        int first = teamSizes[0];
        for (int i = 1; i < teamSizes.Count; i++)
            if (teamSizes[i] != first) return false;
        return true;
    }

    /// <summary>Geometric spawn target placement — runs BEFORE the heightmap exists. Teams sit on
    /// a circle around the map centre (opposite for 2 teams, triangle/square/etc. for more), and
    /// each team's players sit on a smaller circle around the team centre. Returns one list per
    /// team in input order. Plateau carving and spawn-marker placement both consume this directly.</summary>
    private static List<List<(float x, float z)>> ComputeSpawnTargets(int mw, int mh, List<int> teamSizes, Random rng)
    {
        var result = new List<List<(float x, float z)>>();
        int teamCount = teamSizes.Count;
        if (teamCount == 0) return result;

        float cx = mw / 2f;
        float cz = mh / 2f;
        float baseAngle = (float)(rng.NextDouble() * Math.PI * 2);
        float teamDistance = MathF.Min(mw, mh) * (teamCount <= 2 ? 0.32f : (teamCount <= 4 ? 0.34f : 0.36f));

        for (int t = 0; t < teamCount; t++)
        {
            int size = teamSizes[t];
            var teamSpawns = new List<(float x, float z)>();
            if (size <= 0) { result.Add(teamSpawns); continue; }

            float a = baseAngle + t * (MathF.PI * 2f / teamCount);
            float tcx = cx + MathF.Cos(a) * teamDistance;
            float tcz = cz + MathF.Sin(a) * teamDistance;

            if (size == 1)
            {
                teamSpawns.Add((tcx, tcz));
            }
            else
            {
                // Each teammate gets an angular slot around the team centre. Radius scales with
                // team size so 6-player teams aren't on top of each other.
                float spawnR = 22f + (size - 1) * 5f;
                for (int i = 0; i < size; i++)
                {
                    float spawnAngle = a + (i + 0.5f) / size * MathF.PI * 2f;
                    float sx = tcx + MathF.Cos(spawnAngle) * spawnR;
                    float sz = tcz + MathF.Sin(spawnAngle) * spawnR;
                    teamSpawns.Add((sx, sz));
                }
            }
            result.Add(teamSpawns);
        }
        return result;
    }

    /// <summary>
    /// Fill the heightmap with a low-amplitude Perlin profile calibrated against vanilla SC maps
    /// (SCMP_002 / Concord Lake: 2 → 38 m range, water at 17.5, ~78 % within ±5 m of water level),
    /// PLUS forced flat plateaus around each pre-computed spawn target. The plateau zone is a
    /// perfectly flat disc of radius <see cref="PlateauFlatRadius"/> at each spawn point, with a
    /// smooth transition (smoothstep) out to <see cref="PlateauBlendRadius"/> where it merges back
    /// into the noise terrain. This guarantees every spawn has buildable ground around it
    /// regardless of where the noise happens to be high or low.
    /// </summary>
    private static void GenerateHeightmap(ScMap map, PerlinNoise noise, MapGenerationOptions opts,
        List<List<(float x, float z)>> spawnTargets)
    {
        int w = map.Heightmap.Width;
        int h = map.Heightmap.Height;
        int stride = w + 1;

        var warpNoise = new PerlinNoise(opts.Seed + 1);
        var biomeNoise = new PerlinNoise(opts.Seed + 2);

        float waterLevel = opts.HasWater ? 25f : 0f;
        float landBase   = waterLevel + 3f;
        float reliefAmp  = 22f;
        // Bigger, smoother features (3 cycles vs 5) → larger calm zones, less "cabossé everywhere".
        float frequency  = 3f / w;
        float warpStrength = 0.5f;
        // No high-frequency plains ripple — vanilla SC plains are clean.

        // Flatten the spawn target lists. Plateau height for each spawn = the noise value at that
        // exact spot, clamped above water so we never spawn underwater. Plateaus blend smoothly
        // into the surrounding terrain (no "hovering disc" artifacts).
        var spawns = spawnTargets.SelectMany(t => t).ToList();
        var plateauHeights = new float[spawns.Count];
        for (int i = 0; i < spawns.Count; i++)
        {
            plateauHeights[i] = MathF.Max(waterLevel + 4f, SampleNoiseHeight(spawns[i].x, spawns[i].z,
                noise, warpNoise, biomeNoise, landBase, reliefAmp, frequency, warpStrength));
        }

        float flatR2  = PlateauFlatRadius * PlateauFlatRadius;
        float blendR2 = PlateauBlendRadius * PlateauBlendRadius;

        for (int y = 0; y <= h; y++)
        {
            for (int x = 0; x <= w; x++)
            {
                float noiseHeight = SampleNoiseHeight(x, y, noise, warpNoise, biomeNoise,
                    landBase, reliefAmp, frequency, warpStrength);

                // Plateau influence : find the nearest spawn and blend toward its plateau height.
                float worldHeight = noiseHeight;
                if (spawns.Count > 0)
                {
                    int nearest = 0;
                    float bestD2 = float.MaxValue;
                    for (int s = 0; s < spawns.Count; s++)
                    {
                        float dx = x - spawns[s].x, dz = y - spawns[s].z;
                        float d2 = dx * dx + dz * dz;
                        if (d2 < bestD2) { bestD2 = d2; nearest = s; }
                    }
                    if (bestD2 <= flatR2)
                    {
                        // Inside the plateau : fully flat.
                        worldHeight = plateauHeights[nearest];
                    }
                    else if (bestD2 <= blendR2)
                    {
                        // Smoothstep blend between plateau and noise.
                        float d = MathF.Sqrt(bestD2);
                        float t = (d - PlateauFlatRadius) / (PlateauBlendRadius - PlateauFlatRadius);
                        t = t * t * (3f - 2f * t);  // smoothstep
                        worldHeight = plateauHeights[nearest] * (1f - t) + noiseHeight * t;
                    }
                }

                int rawHeight = Math.Clamp((int)(worldHeight * 128f), 0, 65535);
                map.Heightmap.Data[y * stride + x] = (ushort)rawHeight;
            }
        }
    }

    /// <summary>Pure noise terrain height at (x, z) — no plateau influence. Shared by the spawn
    /// height pre-computation and the main heightmap loop so they always produce identical noise.</summary>
    private static float SampleNoiseHeight(float x, float z,
        PerlinNoise noise, PerlinNoise warpNoise, PerlinNoise biomeNoise,
        float landBase, float reliefAmp, float frequency, float warpStrength)
    {
        float wx = warpNoise.OctaveNoise(x * frequency * 0.5f, z * frequency * 0.5f, 2, 0.5f);
        float wy = warpNoise.OctaveNoise(x * frequency * 0.5f + 100f, z * frequency * 0.5f + 100f, 2, 0.5f);
        float sx = x * frequency + wx * warpStrength;
        float sy = z * frequency + wy * warpStrength;
        // 4 octaves (was 5) → less fine detail, bigger smoother features.
        float n = noise.OctaveNoise(sx, sy, 4, 0.55f);
        float bm = biomeNoise.OctaveNoise(x * frequency * 0.35f + 50f, z * frequency * 0.35f + 50f, 2, 0.5f);
        bm = (bm + 1f) * 0.5f;
        float reliefScale = 0.45f + bm * 0.85f;
        return landBase + n * reliefAmp * reliefScale;
    }

    /// <summary>
    /// Assign textures to strata. Strata 0 = Grass (base / always visible). Strata 1..6 = the
    /// other 6 smart categories. Empty paths are left in place if the caller didn't supply one.
    /// </summary>
    private static void AssignStrataTextures(ScMap map, MapGenerationOptions opts)
    {
        for (int i = 0; i < map.TerrainTextures.Length; i++)
            map.TerrainTextures[i] = new TerrainTexture();

        // Base = Grass. If grass isn't available in the map (shouldn't happen with fallback resolver),
        // try other broadly-flat textures as base.
        SmartBrushTool.TerrainCategory[] basePreference =
        {
            SmartBrushTool.TerrainCategory.Grass,
            SmartBrushTool.TerrainCategory.Plateau,
            SmartBrushTool.TerrainCategory.Dirt,
            SmartBrushTool.TerrainCategory.Rock,
        };
        foreach (var cat in basePreference)
        {
            if (opts.TexturesByCategory.TryGetValue(cat, out var p) && !string.IsNullOrEmpty(p))
            {
                map.TerrainTextures[0].AlbedoPath = p;
                if (opts.NormalsByCategory.TryGetValue(cat, out var n) && !string.IsNullOrEmpty(n))
                    map.TerrainTextures[0].NormalPath = n;
                map.TerrainTextures[0].AlbedoScale = 10f;
                map.TerrainTextures[0].NormalScale = 10f;
                Services.DebugLog.Write($"[MapGen] Base strata 0 ← {cat}: {p}");
                break;
            }
        }
        // Final fallback: if none of the preferred categories had a texture, grab ANY non-empty
        // path from the options. Better than leaving strata 0 empty (which would show as magenta
        // wherever no other strata is fully painted).
        if (string.IsNullOrEmpty(map.TerrainTextures[0].AlbedoPath))
        {
            var anyPath = opts.TexturesByCategory.Values.FirstOrDefault(p => !string.IsNullOrEmpty(p));
            if (!string.IsNullOrEmpty(anyPath))
            {
                map.TerrainTextures[0].AlbedoPath = anyPath!;
                map.TerrainTextures[0].AlbedoScale = 10f;
                map.TerrainTextures[0].NormalScale = 10f;
                Services.DebugLog.Write($"[MapGen] Base strata 0 ← fallback ANY: {anyPath}");
            }
            else
            {
                Services.DebugLog.Write("[MapGen] Base strata 0 has no texture — map will render magenta!");
            }
        }

        // Macro layer at slot 5 for vanilla v53 (always-on alpha overlay rendered by the shader's
        // upper-layer path). It MUST be a real vanilla macro texture or the fallback magenta would
        // flood the entire map. The caller is responsible for picking the right one per biome.
        if (!string.IsNullOrEmpty(opts.MacroTexturePath) && map.TerrainTextures.Length > VanillaMacroSlot)
        {
            map.TerrainTextures[VanillaMacroSlot].AlbedoPath = opts.MacroTexturePath!;
            map.TerrainTextures[VanillaMacroSlot].AlbedoScale = 256f; // macros tile across the whole map
            map.TerrainTextures[VanillaMacroSlot].NormalScale = 256f;
            Services.DebugLog.Write($"[MapGen] Strata {VanillaMacroSlot} (macro) ← {opts.MacroTexturePath}");
        }
        else
        {
            Services.DebugLog.Write($"[MapGen] Strata {VanillaMacroSlot} (macro) — NO PATH SUPPLIED — map will render magenta!");
        }

        foreach (var (cat, strata) in StrataOrder)
        {
            if (!opts.TexturesByCategory.TryGetValue(cat, out var path) || string.IsNullOrEmpty(path))
            {
                Services.DebugLog.Write($"[MapGen] Strata {strata} ({cat}): no texture available, leaving empty");
                continue;
            }
            if (strata >= map.TerrainTextures.Length) continue;
            map.TerrainTextures[strata].AlbedoPath = path;
            if (opts.NormalsByCategory.TryGetValue(cat, out var nrm) && !string.IsNullOrEmpty(nrm))
                map.TerrainTextures[strata].NormalPath = nrm;
            map.TerrainTextures[strata].AlbedoScale = 10f;
            map.TerrainTextures[strata].NormalScale = 10f;
            Services.DebugLog.Write($"[MapGen] Strata {strata} ({cat}) ← {path}");
        }
    }

    /// <summary>
    /// Walk every splatmap pixel, classify the corresponding heightmap pixel via smart logic, and
    /// set the matching strata channel to 255 (others remain 0). Produces a sharp per-category
    /// painted map.
    /// </summary>
    private static void PaintSplatmaps(ScMap map, MapGenerationOptions opts)
    {
        var category2Strata = new Dictionary<SmartBrushTool.TerrainCategory, int>();
        foreach (var (cat, strata) in StrataOrder)
        {
            if (opts.TexturesByCategory.ContainsKey(cat))
                category2Strata[cat] = strata;
        }
        if (category2Strata.Count == 0) return;

        int mw = map.TextureMaskLow.Width;
        int mh = map.TextureMaskLow.Height;
        const int header = 128;
        int expectedPixels = mw * mh * 4;
        if (map.TextureMaskLow.DdsData.Length < header + expectedPixels) return;
        if (map.TextureMaskHigh.DdsData.Length < header + expectedPixels) return;

        // Wipe both masks (clean slate).
        Array.Clear(map.TextureMaskLow.DdsData, header, expectedPixels);
        Array.Clear(map.TextureMaskHigh.DdsData, header, expectedPixels);

        var hm = map.Heightmap;
        float waterLevel = map.Water.HasWater ? map.Water.Elevation : 0f;
        float scaleX = (float)hm.Width / mw;
        float scaleZ = (float)hm.Height / mh;

        for (int y = 0; y < mh; y++)
        {
            for (int x = 0; x < mw; x++)
            {
                int hx = (int)MathF.Floor(x * scaleX);
                int hz = (int)MathF.Floor(y * scaleZ);
                hx = Math.Clamp(hx, 0, hm.Width);
                hz = Math.Clamp(hz, 0, hm.Height);
                float height = hm.GetWorldHeight(hx, hz);
                float slope = ComputeSlope(hm, hx, hz);
                var cat = SmartBrushTool.Classify(height, slope, waterLevel);
                if (!category2Strata.TryGetValue(cat, out int strata)) continue;
                WriteStrataChannel(map, strata, x, y, 255);
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

    private static void WriteStrataChannel(ScMap map, int strata, int mx, int my, byte value)
    {
        var mask = strata <= 4 ? map.TextureMaskLow : map.TextureMaskHigh;
        if (mask.Width <= 0 || mask.Height <= 0) return;
        const int header = 128;
        int withinMask = ((strata - 1) % 4) + 1;
        int channelOffset = withinMask switch { 1 => 2, 2 => 1, 3 => 0, 4 => 3, _ => 0 };
        int idx = header + (my * mask.Width + mx) * 4 + channelOffset;
        if (idx < mask.DdsData.Length) mask.DdsData[idx] = value;
    }

    /// <summary>
    /// Add spawn markers (one per pre-computed target) and Mass markers in tight rings around
    /// each spawn. Because the heightmap was carved with flat plateaus at the spawn targets,
    /// every spawn is guaranteed to land on flat ground; masses placed within the plateau radius
    /// are likewise guaranteed flat and accessible.
    /// </summary>
    private static void PlaceMarkersOnPlateaus(ScMap map, MapGenerationOptions opts,
        List<List<(float x, float z)>> spawnTargets)
    {
        var sizes = opts.TeamPlayerCounts;
        int teamCount = sizes.Count;
        if (teamCount == 0 || sizes.Sum() <= 0) return;

        var masses = new List<int>(opts.TeamMassCounts);
        while (masses.Count < teamCount) masses.Add(sizes[masses.Count] * 4);

        map.Markers.Clear();
        int spawnNum = 1;
        int massNum = 1;
        var allMassPositions = new List<(float x, float z)>();

        for (int t = 0; t < teamCount && t < spawnTargets.Count; t++)
        {
            var teamSpawns = spawnTargets[t];
            if (teamSpawns.Count == 0) continue;

            // Place all team spawn markers and remember their map.Markers indices.
            var spawnIndices = new List<int>();
            foreach (var (sx, sz) in teamSpawns)
            {
                (float x, float z) = BuildGridSnap.Snap(MarkerType.BlankMarker, sx, sz);
                float y = GroundClampService.SampleHeight(map.Heightmap, x, z);
                map.Markers.Add(new Marker
                {
                    Name = $"ARMY_{spawnNum}",
                    Type = MarkerType.BlankMarker,
                    Position = new Vector3(x, y, z),
                    Color = "ff800080",
                });
                spawnIndices.Add(map.Markers.Count - 1);
                spawnNum++;
            }

            // Mass markers : split this team's total across its spawns, with the first R spawns
            // getting one extra each (R = remainder).
            int totalMass = masses[t];
            if (totalMass <= 0) continue;
            int perSpawn = totalMass / spawnIndices.Count;
            int remainder = totalMass % spawnIndices.Count;
            for (int i = 0; i < spawnIndices.Count; i++)
            {
                int count = perSpawn + (i < remainder ? 1 : 0);
                PlaceMassesAroundSpawn(map, spawnIndices[i], count, allMassPositions, ref massNum);
            }
        }
    }

    /// <summary>Drop <paramref name="count"/> Mass markers around a single spawn. Because the
    /// terrain has been pre-carved with a flat plateau of radius <see cref="PlateauFlatRadius"/>
    /// around every spawn, we can just distribute the masses on rings inside that plateau and skip
    /// any plateau/slope checks — they're guaranteed flat and accessible.</summary>
    private static void PlaceMassesAroundSpawn(ScMap map, int spawnIdx, int count,
        List<(float x, float z)> allPlacedMasses, ref int massNum)
    {
        if (count <= 0 || spawnIdx < 0 || spawnIdx >= map.Markers.Count) return;
        var spawn = map.Markers[spawnIdx].Position;
        const float minMassSpacing = 9f;

        // Distribute evenly around the spawn. Rings stay well inside PlateauFlatRadius (38) so
        // every mass falls on guaranteed flat ground.
        for (int m = 0; m < count; m++)
        {
            float baseAngle = (m + 0.5f) / count * MathF.PI * 2f;

            (float x, float z)? chosen = null;
            float[] rings = { 14f, 20f, 26f, 32f };
            foreach (float ring in rings)
            {
                if (chosen != null) break;
                // 6 angular jitter samples per ring — only relaxes outward if neighbours block.
                for (int j = 0; j < 6; j++)
                {
                    float angle = baseAngle + j * (MathF.PI / 18f) * (j % 2 == 0 ? 1 : -1);
                    float x = spawn.X + MathF.Cos(angle) * ring;
                    float z = spawn.Z + MathF.Sin(angle) * ring;
                    if (TooCloseToAny((x, z), allPlacedMasses, minMassSpacing)) continue;
                    chosen = (x, z);
                    break;
                }
            }
            if (chosen == null) continue;

            var (mx, mz) = chosen.Value;
            (mx, mz) = BuildGridSnap.Snap(MarkerType.Mass, mx, mz);
            float my = GroundClampService.SampleHeight(map.Heightmap, mx, mz);
            map.Markers.Add(new Marker
            {
                Name = $"Mass {massNum:D2}",
                Type = MarkerType.Mass,
                Position = new Vector3(mx, my, mz),
                Resource = true,
                Amount = 100f,
                Color = "ff808000",
            });
            allPlacedMasses.Add((mx, mz));
            massNum++;
        }
    }

    private static bool TooCloseToAny((float x, float z) pos, List<(float x, float z)> existing, float minDist)
    {
        float min2 = minDist * minDist;
        foreach (var p in existing)
        {
            float dx = pos.x - p.x, dz = pos.z - p.z;
            if (dx * dx + dz * dz < min2) return true;
        }
        return false;
    }
}
