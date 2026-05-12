using SupremeCommanderEditor.Core.Formats.Lua;
using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Tests.Formats;

public class LuaReaderTests
{
    private static string GetTestDataPath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    [Fact]
    public void ScenarioReader_ParsesMapInfo()
    {
        var info = ScenarioLuaReader.Read(GetTestDataPath("SCMP_001_scenario.lua"));

        Assert.Equal("Burial Mounds", info.Name);
        Assert.Equal("skirmish", info.Type);
        Assert.Equal(1024, info.Width);
        Assert.Equal(1024, info.Height);
        Assert.Equal(90, info.NoRushRadius);
        Assert.Equal(8, info.Armies.Count);
        Assert.Equal("ARMY_1", info.Armies[0].Name);
        Assert.Equal("ARMY_8", info.Armies[7].Name);
    }

    [Fact]
    public void SaveReader_ParsesMarkers()
    {
        var markers = SaveLuaReader.ReadMarkers(GetTestDataPath("SCMP_001_save.lua"));

        Assert.True(markers.Count > 0, "Should have markers");

        var massMarkers = markers.Where(m => m.Type == MarkerType.Mass).ToList();
        Assert.True(massMarkers.Count > 0, "Should have mass markers");
        Assert.True(massMarkers.All(m => m.Resource));

        // Check spawn markers (ARMY_1 etc. are Blank Marker type)
        var spawnMarkers = markers.Where(m => m.Name.StartsWith("ARMY_")).ToList();
        Assert.True(spawnMarkers.Count > 0, "Should have army spawn markers");

        // Check various AI marker types exist
        var expansionMarkers = markers.Where(m => m.Type == MarkerType.ExpansionArea).ToList();
        Assert.True(expansionMarkers.Count > 0, "Should have expansion markers");
    }
}
