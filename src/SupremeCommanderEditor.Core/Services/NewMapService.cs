using SupremeCommanderEditor.Core.Formats.Lua;
using SupremeCommanderEditor.Core.Formats.Scmap;
using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Services;

public static class NewMapService
{
    public static ScMap CreateBlankMap(int size, int armyCount, string name = "Untitled Map",
        int versionMinor = 56)
    {
        if (!MapSize.IsValid(size))
            throw new ArgumentException($"Map size must be a power of 2, got {size}");
        if (armyCount < 2)
            throw new ArgumentException("Map must have at least 2 armies");

        var map = new ScMap
        {
            VersionMajor = 2,
            VersionMinor = versionMinor,
            MapWidth = size,
            MapHeight = size,
            HeaderUnknown1 = unchecked((int)0xBEEFFEED),
            HeaderUnknown2 = 2,
        };

        // Preview: blank 256x256 ARGB
        var previewPixels = new byte[256 * 256 * 4];
        Array.Fill(previewPixels, (byte)0x40); // Dark gray
        map.PreviewImageDds = DdsHelper.CreateArgbDds(256, 256, previewPixels);

        // Heightmap: flat at mid-height
        map.Heightmap = new Heightmap
        {
            Width = size,
            Height = size,
            HeightScale = 1f / 128f,
            Data = new ushort[(size + 1) * (size + 1)]
        };
        Array.Fill(map.Heightmap.Data, (ushort)(20 * 128)); // ~20m elevation

        // Terrain shader
        map.TerrainShader = "TTerrain";
        map.BackgroundTexturePath = "/textures/environment/defaultbackground.dds";
        map.SkyCubemapPath = "/textures/environment/defaultskycube.dds";
        map.EnvironmentCubeMaps =
        [
            new EnvironmentCubeMap
            {
                Name = "<default>",
                FilePath = "/textures/environment/defaultenvcube.dds"
            }
        ];

        // Lighting defaults
        map.Lighting = new LightingSettings();

        // Water disabled by default
        map.Water = new WaterSettings { HasWater = false };
        map.WaveGenerators = [];

        // Terrain textures — both v53 and v56+ allocate 10 slots so the splatmap channel layout
        // (mask0 + mask1 → strata 1..8, plus base/macro) is fully usable by the procedural map
        // generator regardless of the target version. v53's variable-length layer count means
        // writing 10 layers is still legal even if vanilla maps usually have fewer.
        if (versionMinor <= 53)
        {
            map.TilesetName = "No Tileset";
        }
        else
        {
            map.CartographicRawBytes = new byte[24];
        }
        map.TerrainTextures = new TerrainTexture[10];
        for (int i = 0; i < 10; i++)
            map.TerrainTextures[i] = new TerrainTexture();

        // No decals or props
        map.Decals = [];
        map.DecalGroups = [];
        map.Props = [];

        // Normal map: blank DXT5
        map.NormalMapWidth = size;
        map.NormalMapHeight = size;
        int dxt5BlockSize = Math.Max(1, (size + 3) / 4) * Math.Max(1, (size + 3) / 4) * 16;
        var normalData = new byte[dxt5BlockSize];
        // DXT5 default: normal pointing up (0.5, 0.5, 1.0 in normalized coords)
        map.NormalMapDds = DdsHelper.CreateDxt5Dds(size, size, normalData);

        // Texture masks: blank ARGB at half-size
        int maskSize = size / 2;
        var maskPixels = new byte[maskSize * maskSize * 4];
        map.TextureMaskLow = new TextureMask
        {
            Width = maskSize,
            Height = maskSize,
            DdsData = DdsHelper.CreateArgbDds(maskSize, maskSize, maskPixels)
        };
        map.TextureMaskHigh = new TextureMask
        {
            Width = maskSize,
            Height = maskSize,
            DdsData = DdsHelper.CreateArgbDds(maskSize, maskSize, maskPixels)
        };

        // Water map (v56+)
        if (versionMinor > 53)
        {
            int waterDxt5Size = Math.Max(1, (maskSize + 3) / 4) * Math.Max(1, (maskSize + 3) / 4) * 16;
            map.WaterMapDds = DdsHelper.CreateDxt5Dds(maskSize, maskSize, new byte[waterDxt5Size]);
        }

        // Water aux maps
        int halfSize = (size / 2) * (size / 2);
        map.WaterFoamMask = new byte[halfSize];
        map.WaterFlatness = new byte[halfSize];
        Array.Fill(map.WaterFlatness, (byte)0xFF);
        map.WaterDepthBias = new byte[halfSize];
        Array.Fill(map.WaterDepthBias, (byte)0x7F);

        // Terrain type
        map.TerrainTypeData = new byte[size * size];

        // Skybox for v60
        if (versionMinor >= 60)
        {
            map.SkyBox = new SkyBox();
        }

        // Map info
        var armies = new List<Army>();
        for (int i = 1; i <= armyCount; i++)
        {
            armies.Add(new Army { Name = $"ARMY_{i}" });
        }

        map.Info = new MapInfo
        {
            Name = name,
            Type = "skirmish",
            Width = size,
            Height = size,
            Armies = armies
        };

        // Create spawn markers
        map.Markers = CreateSpawnMarkers(size, armyCount);

        return map;
    }

    /// <summary>
    /// Save a complete map to a folder with all 4 required files.
    /// </summary>
    public static void SaveMapToFolder(ScMap map, string folderPath)
    {
        Directory.CreateDirectory(folderPath);
        var folderName = Path.GetFileName(folderPath);

        ScmapWriter.Write(Path.Combine(folderPath, $"{folderName}.scmap"), map);
        ScenarioLuaWriter.Write(
            Path.Combine(folderPath, $"{folderName}_scenario.lua"),
            map.Info, folderName, folderName);
        SaveLuaWriter.Write(
            Path.Combine(folderPath, $"{folderName}_save.lua"),
            map.Markers, map.Info.Armies);
        ScriptLuaWriter.Write(
            Path.Combine(folderPath, $"{folderName}_script.lua"));
    }

    private static List<Marker> CreateSpawnMarkers(int mapSize, int armyCount)
    {
        var markers = new List<Marker>();
        float center = mapSize / 2f;
        float radius = mapSize * 0.35f;

        for (int i = 0; i < armyCount; i++)
        {
            double angle = 2.0 * Math.PI * i / armyCount;
            float x = center + radius * (float)Math.Cos(angle);
            float z = center + radius * (float)Math.Sin(angle);

            markers.Add(new Marker
            {
                Name = $"ARMY_{i + 1}",
                Type = MarkerType.BlankMarker,
                Position = new System.Numerics.Vector3(x, 20f, z),
                Color = "ff800080"
            });
        }

        return markers;
    }
}
