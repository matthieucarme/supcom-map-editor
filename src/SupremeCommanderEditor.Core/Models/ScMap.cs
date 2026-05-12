using System.Numerics;

namespace SupremeCommanderEditor.Core.Models;

/// <summary>
/// Top-level aggregate representing a complete Supreme Commander map (.scmap + companion files).
/// </summary>
public class ScMap
{
    // === Header ===
    public int VersionMajor { get; set; } = 2;
    public int VersionMinor { get; set; } = 56;
    public float MapWidth { get; set; } = 256f;
    public float MapHeight { get; set; } = 256f;

    // Unknown header fields preserved for round-trip fidelity
    public int HeaderUnknown1 { get; set; } = unchecked((int)0xBEFEEDFE);
    public int HeaderUnknown2 { get; set; } = 2;
    public int HeaderUnknown3 { get; set; }
    public short HeaderUnknown4 { get; set; }

    // === Preview ===
    public byte[] PreviewImageDds { get; set; } = [];

    // === Terrain ===
    public Heightmap Heightmap { get; set; } = new();
    public string TerrainShader { get; set; } = "TTerrain";
    public string BackgroundTexturePath { get; set; } = string.Empty;
    public string SkyCubemapPath { get; set; } = string.Empty;
    public List<EnvironmentCubeMap> EnvironmentCubeMaps { get; set; } = [];

    // === Lighting ===
    public LightingSettings Lighting { get; set; } = new();

    // === Water ===
    public WaterSettings Water { get; set; } = new();
    public List<WaveGenerator> WaveGenerators { get; set; } = [];

    // === Cartographic / Minimap ===
    public CartographicSettings Cartographic { get; set; } = new();
    /// <summary>
    /// Raw unknown bytes for v56 cartographic section (preserved for round-trip).
    /// </summary>
    public byte[]? CartographicRawBytes { get; set; }

    // === Terrain Textures ===
    /// <summary>v53: tileset name (e.g. "No Tileset"). Not present in v56+.</summary>
    public string? TilesetName { get; set; }
    public TerrainTexture[] TerrainTextures { get; set; } = CreateDefaultTextures();

    // Additional unknown ints after texture section
    public int TextureUnknown1 { get; set; }
    public int TextureUnknown2 { get; set; }

    // === Decals ===
    public List<Decal> Decals { get; set; } = [];
    public List<DecalGroup> DecalGroups { get; set; } = [];

    // === Normal Map ===
    public int NormalMapWidth { get; set; }
    public int NormalMapHeight { get; set; }
    public byte[] NormalMapDds { get; set; } = [];

    // === Texture Masks (Splat Maps) ===
    public TextureMask TextureMaskLow { get; set; } = new();
    public TextureMask TextureMaskHigh { get; set; } = new();

    // === Water Maps ===
    public byte[] WaterMapDds { get; set; } = [];
    public byte[] WaterFoamMask { get; set; } = [];
    public byte[] WaterFlatness { get; set; } = [];
    public byte[] WaterDepthBias { get; set; } = [];

    // === Terrain Type ===
    public byte[] TerrainTypeData { get; set; } = [];

    // === SkyBox (v60 only) ===
    public SkyBox? SkyBox { get; set; }

    // === Props ===
    public List<Prop> Props { get; set; } = [];

    // === Companion file data (from Lua files) ===
    public MapInfo Info { get; set; } = new();
    public List<Marker> Markers { get; set; } = [];

    public bool IsForgedAlliance => VersionMinor >= 60;

    private static TerrainTexture[] CreateDefaultTextures()
    {
        var textures = new TerrainTexture[10];
        for (int i = 0; i < 10; i++)
            textures[i] = new TerrainTexture();
        return textures;
    }
}
