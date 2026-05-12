namespace SupremeCommanderEditor.Core.Services;

/// <summary>
/// Curated lists of SupCom prop blueprints grouped by visual category (biome + kind). The brush
/// picks a random blueprint from the active preset on each stamp. Every path here exists in
/// vanilla SC `gamedata/env.scd` (verified with `unzip -l`). A blueprint that doesn't exist is
/// silently dropped by the engine at map-load time with no error, so guessing paths is forbidden.
/// </summary>
public static class PropBrushPresets
{
    public record Preset(string Label, IReadOnlyList<string> Blueprints);

    public static readonly Preset RocksEvergreen = new("Rocks — Evergreen", new[]
    {
        "/env/evergreen/props/rocks/rock01_prop.bp",
        "/env/evergreen/props/rocks/rock02_prop.bp",
        "/env/evergreen/props/rocks/rock03_prop.bp",
        "/env/evergreen/props/rocks/rock04_prop.bp",
        "/env/evergreen/props/rocks/rock05_prop.bp",
        "/env/evergreen/props/rocks/rockpile01_prop.bp",
        "/env/evergreen/props/rocks/rockpile02_prop.bp",
        "/env/evergreen/props/rocks/fieldstone01_prop.bp",
        "/env/evergreen/props/rocks/fieldstone02_prop.bp",
        "/env/evergreen/props/rocks/fieldstone03_prop.bp",
        "/env/evergreen/props/rocks/fieldstone04_prop.bp",
        "/env/evergreen/props/rocks/searock01_prop.bp",
        "/env/evergreen/props/rocks/searock02_prop.bp",
    });

    public static readonly Preset RocksTropical = new("Rocks — Tropical", new[]
    {
        "/env/tropical/props/rocks/rock01_prop.bp",
        "/env/tropical/props/rocks/rock02_prop.bp",
        "/env/tropical/props/rocks/rock03_prop.bp",
        "/env/tropical/props/rocks/rock04_prop.bp",
        "/env/tropical/props/rocks/rock05_prop.bp",
        "/env/tropical/props/rocks/rockpile01_prop.bp",
        "/env/tropical/props/rocks/rockpile02_prop.bp",
        "/env/tropical/props/rocks/searock02_prop.bp",
    });

    public static readonly Preset RocksDesert = new("Rocks — Desert", new[]
    {
        "/env/desert/props/rocks/des_boulder01_prop.bp",
        "/env/desert/props/rocks/des_boulder02_prop.bp",
        "/env/desert/props/rocks/des_boulder03_prop.bp",
        "/env/desert/props/rocks/des_boulder04_prop.bp",
        "/env/desert/props/rocks/des_boulder05_prop.bp",
        "/env/desert/props/rocks/des_boulder06_prop.bp",
    });

    public static readonly Preset RocksTundra = new("Rocks — Tundra (ice)", new[]
    {
        "/env/tundra/props/rocks/ice_crystal_01_prop.bp",
        "/env/tundra/props/rocks/tund_rock02_prop.bp",
        "/env/tundra/props/rocks/tund_rock03_prop.bp",
        "/env/tundra/props/rocks/tund_rock04_prop.bp",
        "/env/tundra/props/rocks/tund_rock05_prop.bp",
        "/env/tundra/props/icerock01_prop.bp",
        "/env/tundra/props/icerock02_prop.bp",
        "/env/tundra/props/icerocksm01_prop.bp",
        "/env/tundra/props/icerocksm02_prop.bp",
        "/env/tundra/props/icerocksm03_prop.bp",
        "/env/tundra/props/icerocksm04_prop.bp",
        "/env/tundra/props/iceberg01_prop.bp",
        "/env/tundra/props/iceberg02_prop.bp",
        "/env/tundra/props/iceberg03_prop.bp",
        "/env/tundra/props/iceberg04_prop.bp",
        "/env/tundra/props/iceberg05_prop.bp",
        "/env/tundra/props/iceberg06_prop.bp",
    });

    public static readonly Preset RocksRedRocks = new("Rocks — Red Rocks", new[]
    {
        "/env/redrocks/props/boulder01_prop.bp",
        "/env/redrocks/props/boulder02_prop.bp",
        "/env/redrocks/props/boulder03_prop.bp",
        "/env/redrocks/props/boulder04_prop.bp",
        "/env/redrocks/props/boulder05_prop.bp",
        "/env/redrocks/props/boulder06_prop.bp",
        "/env/redrocks/props/rock01_prop.bp",
        "/env/redrocks/props/rock02_prop.bp",
        "/env/redrocks/props/rock03_prop.bp",
        "/env/redrocks/props/rock_sm01_prop.bp",
        "/env/redrocks/props/rock_sm02_prop.bp",
        "/env/redrocks/props/rock_sm03_prop.bp",
        "/env/redrocks/props/rock_sm04_prop.bp",
        "/env/redrocks/props/rock_sm05_prop.bp",
        "/env/redrocks/props/rock_sm06_prop.bp",
        "/env/redrocks/props/rock_sm07_prop.bp",
        "/env/redrocks/props/searock01_prop.bp",
        "/env/redrocks/props/searock02_prop.bp",
    });

    public static readonly Preset RocksLava = new("Rocks — Lava", new[]
    {
        "/env/lava/props/rocks/lav_rock01_prop.bp",
        "/env/lava/props/rocks/lav_rock02_prop.bp",
        "/env/lava/props/rocks/lav_rock03_prop.bp",
        "/env/lava/props/rocks/lav_rock04_prop.bp",
        "/env/lava/props/rocks/lav_rock05_prop.bp",
        "/env/lava/props/rocks/lavarock02_prop.bp",
        "/env/lava/props/rocks/lavaberg02_prop.bp",
        "/env/lava/props/rocks/lavaberg03_prop.bp",
        "/env/lava/props/rocks/lavaberg04_prop.bp",
        "/env/lava/props/rocks/searock01_prop.bp",
        "/env/lava/props/rocks/searock02_prop.bp",
    });

    public static readonly Preset TreesEvergreen = new("Trees — Evergreen (pine/oak)", new[]
    {
        "/env/evergreen/props/trees/pine06_prop.bp",
        "/env/evergreen/props/trees/pine06_s2_prop.bp",
        "/env/evergreen/props/trees/pine06_s3_prop.bp",
        "/env/evergreen/props/trees/pine06_v1_prop.bp",
        "/env/evergreen/props/trees/pine06_v2_prop.bp",
        "/env/evergreen/props/trees/pine06_big_prop.bp",
        "/env/evergreen/props/trees/pine07_prop.bp",
        "/env/evergreen/props/trees/oak01_s1_prop.bp",
        "/env/evergreen/props/trees/oak01_s2_prop.bp",
        "/env/evergreen/props/trees/oak01_s3_prop.bp",
        "/env/evergreen/props/trees/brch01_prop.bp",
        "/env/evergreen/props/trees/brch01_s1_prop.bp",
        "/env/evergreen/props/trees/dc01_s1_prop.bp",
        "/env/evergreen/props/trees/dc01_s2_prop.bp",
        "/env/evergreen/props/trees/groups/pine06_groupa_prop.bp",
        "/env/evergreen/props/trees/groups/pine06_groupb_prop.bp",
        "/env/evergreen/props/trees/groups/pine07_groupa_prop.bp",
        "/env/evergreen/props/trees/groups/oak01_group1_prop.bp",
        "/env/evergreen/props/trees/groups/oak01_group2_prop.bp",
        "/env/evergreen/props/trees/groups/brch01_group01_prop.bp",
        "/env/evergreen/props/trees/groups/dc01_group1_prop.bp",
    });

    public static readonly Preset TreesTropical = new("Trees — Tropical (palm/ficus)", new[]
    {
        "/env/tropical/props/trees/ficus01_s1_prop.bp",
        "/env/tropical/props/trees/ficus01_s3_prop.bp",
        "/env/tropical/props/trees/ficus02_s2_prop.bp",
        "/env/tropical/props/trees/ficus02_s3_prop.bp",
        "/env/tropical/props/trees/palm01_s1_prop.bp",
        "/env/tropical/props/trees/palm01_s2_prop.bp",
        "/env/tropical/props/trees/palm01_s3_prop.bp",
        "/env/tropical/props/trees/palm02_s1_prop.bp",
        "/env/tropical/props/trees/palm02_s2_prop.bp",
        "/env/tropical/props/trees/palm02_s3_prop.bp",
        "/env/tropical/props/trees/groups/ficus01_group1_prop.bp",
        "/env/tropical/props/trees/groups/ficus01_group3_prop.bp",
        "/env/tropical/props/trees/groups/ficus02_group1_prop.bp",
        "/env/tropical/props/trees/groups/ficus02_group2_prop.bp",
        "/env/tropical/props/trees/groups/ficus02_group3_prop.bp",
        "/env/tropical/props/trees/groups/palm01_group1_prop.bp",
        "/env/tropical/props/trees/groups/palm01_group3_prop.bp",
        "/env/tropical/props/trees/groups/palm01_group4_prop.bp",
        "/env/tropical/props/trees/groups/palm02_group3_prop.bp",
        "/env/tropical/props/trees/groups/palm02_group4_prop.bp",
    });

    public static readonly Preset TreesDesert = new("Trees — Desert (cactus)", new[]
    {
        "/env/desert/props/trees/cactus01_s1_prop.bp",
        "/env/desert/props/trees/cactus01_s2_prop.bp",
        "/env/desert/props/trees/cactus01_s3_prop.bp",
        "/env/desert/props/trees/manzanita01_s1_prop.bp",
        "/env/desert/props/trees/manzanita01_s2_prop.bp",
        "/env/desert/props/trees/manzanita01_s3_prop.bp",
        "/env/desert/props/trees/groups/cactus01_group1_prop.bp",
        "/env/desert/props/trees/groups/cactus01_group2_prop.bp",
        "/env/desert/props/trees/groups/manzanita01_group1_prop.bp",
        "/env/desert/props/trees/groups/manzanita01_group2_prop.bp",
    });

    public static readonly Preset TreesTundra = new("Trees — Tundra (snowy pines)", new[]
    {
        "/env/tundra/props/trees/tund_pine01_s1_prop.bp",
        "/env/tundra/props/trees/tund_pine01_s2_prop.bp",
        "/env/tundra/props/trees/tund_pine01_s3_prop.bp",
        "/env/tundra/props/trees/groups/tund_pine01_group1_prop.bp",
        "/env/tundra/props/trees/groups/tund_pine01_group2_prop.bp",
    });

    public static readonly Preset TreesRedRocks = new("Trees — Red Rocks", new[]
    {
        "/env/redrocks/props/trees/redtree01_s1_prop.bp",
        "/env/redrocks/props/trees/redtree01_s2_prop.bp",
        "/env/redrocks/props/trees/redtree01_s3_prop.bp",
        "/env/redrocks/props/trees/carotenoid01_s1_prop.bp",
        "/env/redrocks/props/trees/carotenoid01_s2_prop.bp",
        "/env/redrocks/props/trees/carotenoid01_s3_prop.bp",
        "/env/redrocks/props/trees/groups/redtree01_group1_prop.bp",
        "/env/redrocks/props/trees/groups/redtree01_group2_prop.bp",
        "/env/redrocks/props/trees/groups/carotenoid01_group1_prop.bp",
        "/env/redrocks/props/trees/groups/carotenoid01_group2_prop.bp",
    });

    public static readonly Preset TreesSwamp = new("Trees — Swamp (cypress)", new[]
    {
        "/env/swamp/props/trees/cypress01_s1_prop.bp",
        "/env/swamp/props/trees/cypress01_s2_prop.bp",
        "/env/swamp/props/trees/cypress01_s3_prop.bp",
        "/env/swamp/props/trees/groups/cypress01_group1_prop.bp",
        "/env/swamp/props/trees/groups/cypress01_group2_prop.bp",
    });

    public static readonly Preset TreesLava = new("Trees — Lava (dead)", new[]
    {
        "/env/lava/props/trees/dead01_s1_prop.bp",
        "/env/lava/props/trees/dead01_s2_prop.bp",
        "/env/lava/props/trees/dead01_s3_prop.bp",
        "/env/lava/props/trees/groups/dead01_group1_prop.bp",
        "/env/lava/props/trees/groups/dead01_group2_prop.bp",
    });

    public static readonly Preset BushesEvergreen = new("Bushes — Evergreen", new[]
    {
        "/env/evergreen/props/bush/eg_bush01_prop.bp",
        "/env/evergreen/props/bush/eg_bush02_prop.bp",
        "/env/evergreen/props/bush/eg_bush03_prop.bp",
        "/env/evergreen/props/bush/eg_fern01_prop.bp",
        "/env/evergreen/props/bush/eg_fern02_prop.bp",
        "/env/evergreen/props/bush/eg_fern03_prop.bp",
        "/env/evergreen/props/bush/flowerbush01_prop.bp",
        "/env/evergreen/props/logs/log01_prop.bp",
        "/env/evergreen/props/logs/log02_prop.bp",
    });

    public static readonly Preset Wreckage = new("Wreckage", new[]
    {
        "/env/wreckage/props/generic/wreckage01_prop.bp",
        "/env/wreckage/props/generic/wreckage02_prop.bp",
        "/env/wreckage/props/generic/wreckage03_prop.bp",
        "/env/wreckage/props/generic/wreckage04_prop.bp",
        "/env/wreckage/props/generic/wreckage05_prop.bp",
        "/env/wreckage/props/generic/wreckage06_prop.bp",
        "/env/wreckage/props/generic/wreckage07_prop.bp",
        "/env/wreckage/props/generic/wreckage08_prop.bp",
    });

    public static readonly IReadOnlyList<Preset> All = new[]
    {
        RocksEvergreen, RocksTropical, RocksDesert, RocksTundra, RocksRedRocks, RocksLava,
        TreesEvergreen, TreesTropical, TreesDesert, TreesTundra, TreesRedRocks, TreesSwamp, TreesLava,
        BushesEvergreen, Wreckage,
    };
}
