using TC = SupremeCommanderEditor.Core.Operations.SmartBrushTool.TerrainCategory;

namespace SupremeCommanderEditor.Core.Operations;

/// <summary>
/// Vanilla Supreme Commander / Forged Alliance terrain texture classification. The map is
/// deterministic: an Exact-basename override table for ambiguous compound names, followed by a
/// Prefix table that covers the numbered/lettered variant series (e.g. evgrass001..020 with a/b
/// suffixes). A texture not matched by either is reported as unclassified (smart will skip it).
/// No substring/keyword guessing in the middle of names.
/// </summary>
public static class TextureCategoryTable
{
    /// <summary>Pull the lowercase basename out of a full albedo path. Strips path prefix and the
    /// trailing "_albedo.dds" suffix. Handles forward + backward slashes.</summary>
    public static string ExtractBasename(string albedoPath)
    {
        if (string.IsNullOrEmpty(albedoPath)) return "";
        var normalized = albedoPath.Replace('\\', '/');
        int slash = normalized.LastIndexOf('/');
        var fileName = slash >= 0 ? normalized.Substring(slash + 1) : normalized;
        const string suffix = "_albedo.dds";
        if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            fileName = fileName.Substring(0, fileName.Length - suffix.Length);
        return fileName.ToLowerInvariant();
    }

    public static TC? Classify(string? albedoPath)
    {
        if (string.IsNullOrEmpty(albedoPath)) return null;
        var basename = ExtractBasename(albedoPath);
        if (string.IsNullOrEmpty(basename)) return null;
        if (Exact.TryGetValue(basename, out var c)) return c;
        foreach (var (prefix, cat) in Prefixes)
            if (basename.StartsWith(prefix, StringComparison.Ordinal)) return cat;
        return null;
    }

    /// <summary>Exact-basename overrides for ambiguous compound names where prefix matching alone
    /// would mislabel. Keys are lowercase basenames (no path, no extension).</summary>
    public static readonly Dictionary<string, TC> Exact = new(StringComparer.OrdinalIgnoreCase)
    {
        // Transition / compound textures — explicitly resolved
        ["eg_cliff_grass"] = TC.Grass,    // cliff with grass on top → grass dominant
        ["cr_glasssand"]   = TC.Beach,    // crystal-glass sand → beach
        ["tund_cliff_snow"]= TC.Snow,     // snowy cliff face → snow on visible faces
        ["sw_cliff_wet"]   = TC.Rock,     // wet cliff → still rock
        ["pp_grassyrocks"] = TC.Grass,    // mostly grass with rocks
        ["sw_grassymud"]   = TC.Dirt,     // mostly mud
        ["tund_grassydirt"]= TC.Grass,    // mostly grass
        ["pp_grassydirt"]  = TC.Grass,    // ditto
        // Old Evergreen variants
        ["sandrock"]       = TC.Rock,
    };

    /// <summary>Prefix table — basenames starting with these strings are mapped to the category.
    /// Order matters: longer / more specific prefixes first so they win over shorter ones.</summary>
    private static readonly (string prefix, TC cat)[] Prefixes =
    {
        // === Evergreen2 (modern evergreen — most FA maps including Burial Mounds) ===
        ("evgrass",     TC.Grass),     // evgrass001..020, with a/b/c suffixes
        ("evrock",      TC.Rock),      // evrock001..020, with a/b/c suffixes
        ("evsand",      TC.Beach),
        ("evsnow",      TC.Snow),
        ("evdirt",      TC.Dirt),
        ("evgravel",    TC.Dirt),
        ("evforest",    TC.Grass),
        ("evcliff",     TC.Rock),
        ("evstone",     TC.Rock),
        ("evpath",      TC.Dirt),
        ("evpebbles",   TC.Dirt),

        // === Evergreen Style (Eg_*) — vanilla SupCom + some FA ===
        ("eg_camo",     TC.Grass),
        ("eg_forest",   TC.Grass),
        ("eg_grassrock",TC.Grass),
        ("eg_grass",    TC.Grass),
        ("eg_cliff",    TC.Rock),      // NB: eg_cliff_grass exact override above
        ("eg_stone",    TC.Rock),
        ("eg_rockbase", TC.Rock),
        ("eg_rock",     TC.Rock),
        ("eg_dirt",     TC.Dirt),
        ("eg_gravel",   TC.Dirt),
        ("eg_path",     TC.Dirt),
        ("eg_pebbles",  TC.Dirt),
        ("eg_basemud",  TC.Dirt),
        ("eg_sand",     TC.Beach),
        ("eg_coast",    TC.Beach),
        ("eg_snow",     TC.Snow),

        // === Tundra (Tund_*) ===
        ("tund_grass",      TC.Grass),   // tund_grass001..010
        ("tund_forest",     TC.Grass),
        ("tund_pine",       TC.Grass),
        ("tund_cliff",      TC.Rock),    // NB: tund_cliff_snow exact override
        ("tund_rock",       TC.Rock),    // tund_rock01..10, tund_rockmed
        ("tund_stone",      TC.Rock),    // tund_stonecliff, tund_stonydirt
        ("tund_ice",        TC.Snow),    // tund_ice001..010
        ("tund_snow",       TC.Snow),
        ("tund_frost",      TC.Snow),
        ("tund_path",       TC.Dirt),
        ("tund_gravel",     TC.Dirt),
        ("tund_pebbles",    TC.Dirt),

        // === Redrocks (Rr_*) ===
        ("rr_drygrass", TC.Plateau),
        ("rr_grasspatch",TC.Grass),
        ("rr_grassrock",TC.Grass),
        ("rr_grass",    TC.Grass),
        ("rr_redrock",  TC.Rock),
        ("rr_sandstone",TC.Rock),
        ("rr_rock",     TC.Rock),
        ("rr_cliff",    TC.Rock),
        ("rr_stone",    TC.Rock),
        ("rr_bluff",    TC.Rock),
        ("rr_clay",     TC.Dirt),
        ("rr_path",     TC.Dirt),
        ("rr_pebbles",  TC.Dirt),
        ("rr_gravel",   TC.Dirt),
        ("rr_wash",     TC.Dirt),
        ("rr_sand",     TC.Beach),
        ("rr_snow",     TC.Snow),

        // === Desert (Des_*) ===
        ("des_drygrass",TC.Plateau),
        ("des_driedmud",TC.Plateau),
        ("des_dunes",   TC.Beach),
        ("des_basesand",TC.Beach),
        ("des_sand",    TC.Beach),
        ("des_cliff",   TC.Rock),
        ("des_rockmud", TC.Rock),
        ("des_rock",    TC.Rock),
        ("des_stone",   TC.Rock),
        ("des_pebbles", TC.Dirt),

        // === Swamp (Sw_*) ===
        ("sw_drygrass", TC.Plateau),
        ("sw_grassrock",TC.Grass),
        ("sw_grass",    TC.Grass),
        ("sw_roots",    TC.Grass),
        ("sw_rootdry",  TC.Plateau),
        ("sw_cliff",    TC.Rock),       // NB: sw_cliff_wet exact override
        ("sw_rocks",    TC.Rock),
        ("sw_rock",     TC.Rock),
        ("sw_stones",   TC.Rock),
        ("sw_stone",    TC.Rock),
        ("sw_mud",      TC.Dirt),
        ("sw_dirt",     TC.Dirt),
        ("sw_path",     TC.Dirt),
        ("sw_pebbles",  TC.Dirt),
        ("sw_sand",     TC.Beach),
        ("sw_water",    TC.SeaFloor),

        // === Lava (Lav_*) — molten / volcanic ===
        ("lav_magma",   TC.Rock),
        ("lav_lava",    TC.Rock),
        ("lav_lavarock",TC.Rock),
        ("lav_ashyrock",TC.Rock),
        ("lav_crackle", TC.Rock),
        ("lav_stone",   TC.Rock),
        ("lav_cliff",   TC.Rock),
        ("lav_rock",    TC.Rock),
        ("lav_ash",     TC.Dirt),
        ("lav_pebbles", TC.Dirt),
        ("lav_sand",    TC.Beach),

        // === Paradise (Pp_*) — tropical ===
        ("pp_grass",    TC.Grass),
        ("pp_lawn",     TC.Grass),
        ("pp_beach",    TC.Beach),
        ("pp_sand",     TC.Beach),
        ("pp_cliff",    TC.Rock),
        ("pp_rocks",    TC.Rock),
        ("pp_rock",     TC.Rock),
        ("pp_lightdirt",TC.Dirt),
        ("pp_pebbles",  TC.Dirt),
        ("pp_snow",     TC.Snow),

        // === Crystal (Cr_*) — alien / FA ===
        ("cr_crystaldirt",TC.Dirt),
        ("cr_crystal",  TC.Rock),
        ("cr_basedirt", TC.Dirt),
        ("cr_dirt",     TC.Dirt),
        ("cr_cliff",    TC.Rock),
        ("cr_rock",     TC.Rock),
        ("cr_glass",    TC.Rock),       // NB: cr_glasssand exact override
        ("cr_sand",     TC.Beach),
        ("cr_pebbles",  TC.Dirt),

        // === Legacy SupCom-1 evergreen prefixes (rare) ===
        ("grass00",     TC.Grass),
        // Macro layers (macroice / macrotexture000 etc.) intentionally NOT listed — those are
        // upper/overlay textures, not paintable strata; leave unclassified so smart skips them.
    };
}
