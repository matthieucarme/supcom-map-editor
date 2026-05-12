using SupremeCommanderEditor.Core.Formats.Scmap;

namespace SupremeCommanderEditor.Core.Tests.Formats;

public class ScmapRoundTripTests
{
    private static string GetTestDataPath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "TestData", fileName);
    }

    [Theory]
    [InlineData("SCMP_001.scmap")]
    [InlineData("SCMP_009.scmap")]
    [InlineData("SCCA_A01.scmap")]
    public void RoundTrip_ProducesIdenticalBytes(string fileName)
    {
        var filePath = GetTestDataPath(fileName);
        var originalBytes = File.ReadAllBytes(filePath);

        // Read
        var map = ScmapReader.Read(filePath);

        // Write to memory
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        ScmapWriter.Write(writer, map);
        var writtenBytes = ms.ToArray();

        // Compare
        Assert.Equal(originalBytes.Length, writtenBytes.Length);

        int firstDiff = -1;
        for (int i = 0; i < originalBytes.Length; i++)
        {
            if (originalBytes[i] != writtenBytes[i])
            {
                firstDiff = i;
                break;
            }
        }

        Assert.True(firstDiff == -1,
            $"Files differ at byte offset 0x{firstDiff:X8} ({firstDiff}). " +
            $"Expected: 0x{(firstDiff >= 0 ? originalBytes[firstDiff] : 0):X2}, " +
            $"Got: 0x{(firstDiff >= 0 ? writtenBytes[firstDiff] : 0):X2}");
    }

    [Theory]
    [InlineData("SCMP_001.scmap")]
    [InlineData("SCMP_009.scmap")]
    public void Read_SkirmishMap_HasValidData(string fileName)
    {
        var filePath = GetTestDataPath(fileName);
        var map = ScmapReader.Read(filePath);

        Assert.Equal(2, map.VersionMajor);
        Assert.True(map.VersionMinor is 53 or 56 or 60,
            $"Unexpected version minor: {map.VersionMinor}");
        Assert.True(map.MapWidth > 0);
        Assert.True(map.MapHeight > 0);
        Assert.True(map.Heightmap.Width > 0);
        Assert.True(map.Heightmap.Height > 0);
        Assert.Equal((map.Heightmap.Width + 1) * (map.Heightmap.Height + 1), map.Heightmap.Data.Length);
        Assert.True(map.PreviewImageDds.Length > 0);
        Assert.NotEmpty(map.TerrainShader);
    }

    [Fact]
    public void Read_CampaignMap_HasValidData()
    {
        var filePath = GetTestDataPath("SCCA_A01.scmap");
        var map = ScmapReader.Read(filePath);

        Assert.Equal(2, map.VersionMajor);
        Assert.True(map.Heightmap.Data.Length > 0);
        Assert.True(map.PreviewImageDds.Length > 0);
    }
}
