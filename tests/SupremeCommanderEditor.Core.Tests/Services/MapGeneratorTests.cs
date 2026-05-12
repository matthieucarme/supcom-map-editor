using SupremeCommanderEditor.Core.Operations;
using SupremeCommanderEditor.Core.Services;
using Xunit;

namespace SupremeCommanderEditor.Core.Tests.Services;

public class MapGeneratorTests
{
    [Fact]
    public void Generate_StrataPathsAssignedCorrectly()
    {
        var opts = new MapGenerationOptions
        {
            Seed = 42,
            Size = 256,
            HasWater = true,
            TeamPlayerCounts = new() { 2, 2 },
            TeamMassCounts = new() { 6, 6 },
            TexturesByCategory = new()
            {
                [SmartBrushTool.TerrainCategory.Grass]    = "/env/evergreen2/layers/evgrass005_albedo.dds",
                [SmartBrushTool.TerrainCategory.Rock]     = "/env/evergreen2/layers/evrock006_albedo.dds",
                [SmartBrushTool.TerrainCategory.Dirt]     = "/env/evergreen2/layers/eg_gravel01_albedo.dds",
                [SmartBrushTool.TerrainCategory.Beach]    = "/env/evergreen/layers/eg_sand_albedo.dds",
                [SmartBrushTool.TerrainCategory.Snow]     = "/env/tundra/layers/tund_snow_albedo.dds",
                [SmartBrushTool.TerrainCategory.Plateau]  = "/env/redrocks/layers/rr_drygrass_albedo.dds",
                [SmartBrushTool.TerrainCategory.SeaFloor] = "/env/swamp/layers/sw_water_albedo.dds",
            },
        };

        var map = MapGenerator.Generate(opts);

        // Vanilla v53 layout: 6 slots total (base + 4 splatmap-blended + macro).
        Assert.Equal(6, map.TerrainTextures.Length);
        // Strata 0 = Grass (base)
        Assert.Equal("/env/evergreen2/layers/evgrass005_albedo.dds", map.TerrainTextures[0].AlbedoPath);
        // Strata 1 = Rock
        Assert.Equal("/env/evergreen2/layers/evrock006_albedo.dds", map.TerrainTextures[1].AlbedoPath);
        // Strata 2 = Dirt
        Assert.Equal("/env/evergreen2/layers/eg_gravel01_albedo.dds", map.TerrainTextures[2].AlbedoPath);
        // Strata 3 = Snow (Beach was dropped — only 4 splatmap slots in vanilla v53)
        Assert.Equal("/env/tundra/layers/tund_snow_albedo.dds", map.TerrainTextures[3].AlbedoPath);
        // Strata 4 = Plateau
        Assert.Equal("/env/redrocks/layers/rr_drygrass_albedo.dds", map.TerrainTextures[4].AlbedoPath);
        // Strata 5 = macro overlay (alpha-blended on top by the shader)
        // The test data passes Beach/SeaFloor too but those are skipped in the 6-slot vanilla layout.
    }

    [Fact]
    public void Generate_SplatmapHasNonZeroData()
    {
        var opts = new MapGenerationOptions
        {
            Seed = 42,
            Size = 256,
            HasWater = true,
            TeamPlayerCounts = new() { 2, 2 },
            TexturesByCategory = new()
            {
                [SmartBrushTool.TerrainCategory.Grass]    = "/env/evergreen2/layers/evgrass005_albedo.dds",
                [SmartBrushTool.TerrainCategory.Rock]     = "/env/evergreen2/layers/evrock006_albedo.dds",
                [SmartBrushTool.TerrainCategory.Dirt]     = "/env/evergreen2/layers/eg_gravel01_albedo.dds",
                [SmartBrushTool.TerrainCategory.Snow]     = "/env/tundra/layers/tund_snow_albedo.dds",
            },
        };

        var map = MapGenerator.Generate(opts);

        // Splatmap should have some non-zero bytes (rock/dirt/snow painted somewhere)
        const int header = 128;
        bool foundNonZero = false;
        for (int i = header; i < map.TextureMaskLow.DdsData.Length; i++)
        {
            if (map.TextureMaskLow.DdsData[i] != 0) { foundNonZero = true; break; }
        }
        Assert.True(foundNonZero, "TextureMaskLow should have painted pixels");
    }

    [Fact]
    public void Generate_SameSeedProducesIdenticalHeightmap()
    {
        var opts = new MapGenerationOptions
        {
            Seed = 12345,
            Size = 256,
            HasWater = true,
            TeamPlayerCounts = new() { 2, 2 },
        };

        var m1 = MapGenerator.Generate(opts);
        var m2 = MapGenerator.Generate(opts);

        Assert.Equal(m1.Heightmap.Data, m2.Heightmap.Data);
    }
}
