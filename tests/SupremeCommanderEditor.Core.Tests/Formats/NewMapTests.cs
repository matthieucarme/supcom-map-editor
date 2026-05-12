using SupremeCommanderEditor.Core.Formats.Lua;
using SupremeCommanderEditor.Core.Formats.Scmap;
using SupremeCommanderEditor.Core.Services;

namespace SupremeCommanderEditor.Core.Tests.Formats;

public class NewMapTests
{
    [Fact]
    public void CreateBlankMap_ProducesValidScmap()
    {
        var map = NewMapService.CreateBlankMap(256, 2, "Test Map", 56);

        Assert.Equal(256f, map.MapWidth);
        Assert.Equal(256f, map.MapHeight);
        Assert.Equal(257 * 257, map.Heightmap.Data.Length);
        Assert.True(map.PreviewImageDds.Length > 0);
        Assert.Equal(2, map.Info.Armies.Count);
        Assert.Equal(2, map.Markers.Count);
    }

    [Fact]
    public void CreateBlankMap_ScmapRoundTrips()
    {
        var map = NewMapService.CreateBlankMap(256, 4, "Round Trip Test", 56);

        // Write to memory
        using var ms1 = new MemoryStream();
        using var writer1 = new BinaryWriter(ms1);
        ScmapWriter.Write(writer1, map);
        var bytes1 = ms1.ToArray();

        // Read back
        using var ms2 = new MemoryStream(bytes1);
        using var reader = new BinaryReader(ms2);
        var map2 = ScmapReader.Read(reader);

        // Write again
        using var ms3 = new MemoryStream();
        using var writer2 = new BinaryWriter(ms3);
        ScmapWriter.Write(writer2, map2);
        var bytes2 = ms3.ToArray();

        Assert.Equal(bytes1.Length, bytes2.Length);
        Assert.True(bytes1.SequenceEqual(bytes2), "Round-trip of new map should be byte-identical");
    }

    [Fact]
    public void SaveMapToFolder_CreatesAllFiles()
    {
        var map = NewMapService.CreateBlankMap(256, 2, "Folder Test");
        var tempDir = Path.Combine(Path.GetTempPath(), $"scmap_test_{Guid.NewGuid():N}");

        try
        {
            var mapDir = Path.Combine(tempDir, "TestMap.v0001");
            NewMapService.SaveMapToFolder(map, mapDir);

            Assert.True(File.Exists(Path.Combine(mapDir, "TestMap.v0001.scmap")));
            Assert.True(File.Exists(Path.Combine(mapDir, "TestMap.v0001_scenario.lua")));
            Assert.True(File.Exists(Path.Combine(mapDir, "TestMap.v0001_save.lua")));
            Assert.True(File.Exists(Path.Combine(mapDir, "TestMap.v0001_script.lua")));

            // Verify scenario is parseable
            var info = ScenarioLuaReader.Read(Path.Combine(mapDir, "TestMap.v0001_scenario.lua"));
            Assert.Equal("Folder Test", info.Name);
            Assert.Equal(2, info.Armies.Count);

            // Verify scmap round-trips
            var original = File.ReadAllBytes(Path.Combine(mapDir, "TestMap.v0001.scmap"));
            var reloaded = ScmapReader.Read(Path.Combine(mapDir, "TestMap.v0001.scmap"));

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            ScmapWriter.Write(writer, reloaded);
            Assert.True(original.SequenceEqual(ms.ToArray()), "Saved scmap should round-trip");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
