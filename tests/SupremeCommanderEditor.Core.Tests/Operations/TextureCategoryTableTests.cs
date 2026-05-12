using SupremeCommanderEditor.Core.Operations;
using static SupremeCommanderEditor.Core.Operations.SmartBrushTool;
using Xunit;

namespace SupremeCommanderEditor.Core.Tests.Operations;

public class TextureCategoryTableTests
{
    // SCMP_001 (Burial Mounds) actual paths from the .scmap
    [Theory]
    [InlineData("/env/evergreen2/layers/evgrass010a_albedo.dds", TerrainCategory.Grass)]
    [InlineData("/env/evergreen2/layers/evrock006_albedo.dds",   TerrainCategory.Rock)]
    [InlineData("/env/evergreen2/layers/eg_gravel01_albedo.dds", TerrainCategory.Dirt)]
    [InlineData("/env/evergreen2/layers/evgrass005a_albedo.dds", TerrainCategory.Grass)]
    [InlineData("/env/evergreen2/layers/evrock007_albedo.dds",   TerrainCategory.Rock)]
    // SCMP_009 paths
    [InlineData("/env/evergreen2/layers/evrock008_albedo.dds",   TerrainCategory.Rock)]
    [InlineData("/env/evergreen2/layers/evgrass005_albedo.dds",  TerrainCategory.Grass)]
    [InlineData("/env/evergreen2/layers/eg_dirt003_albedo.dds",  TerrainCategory.Dirt)]
    [InlineData("/env/evergreen2/layers/eg_gravel005_albedo.dds",TerrainCategory.Dirt)]
    // SCCA_A01 (tundra) paths
    [InlineData("/env/tundra/layers/tund_ice001_albedo.dds",     TerrainCategory.Snow)]
    [InlineData("/env/tundra/layers/tund_ice004a_albedo.dds",    TerrainCategory.Snow)]
    [InlineData("/env/tundra/layers/tund_rock03_albedo.dds",     TerrainCategory.Rock)]
    [InlineData("/env/tundra/layers/tund_snow_albedo.dds",       TerrainCategory.Snow)]
    public void Classify_VanillaPaths_ReturnsExpectedCategory(string path, TerrainCategory expected)
    {
        var actual = TextureCategoryTable.Classify(path);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("/env/evergreen/layers/macrotexture000_albedo.dds")] // macro upper layer
    [InlineData("/env/tundra/layers/macroice_albedo.dds")]            // macro
    [InlineData("/env/madeup/layers/something_albedo.dds")]
    public void Classify_UnknownOrMacro_ReturnsNull(string path)
    {
        Assert.Null(TextureCategoryTable.Classify(path));
    }
}
