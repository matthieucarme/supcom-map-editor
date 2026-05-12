using System.Numerics;
using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Formats.Scmap;

/// <summary>
/// Reads a Supreme Commander .scmap binary file into an ScMap model.
/// Supports versions 53 (vanilla SupCom), 56, and 60 (Forged Alliance).
/// </summary>
public static class ScmapReader
{

    public const int ScmapMagic = 0x1a70614d; // "Map\x1a"

    public static ScMap Read(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        using var reader = new BinaryReader(fs);
        return Read(reader);
    }

    public static ScMap Read(BinaryReader reader)
    {
        var map = new ScMap();

        ReadHeader(reader, map);
        ReadPreview(reader, map);
        map.VersionMinor = reader.ReadInt32();
        ReadHeightmap(reader, map);
        ReadTerrainShaderAndEnvironment(reader, map);
        ReadLighting(reader, map);
        ReadWater(reader, map);
        ReadWaveGenerators(reader, map);

        if (map.VersionMinor <= 53)
        {
            ReadTerrainTexturesV53(reader, map);
        }
        else
        {
            ReadCartographic(reader, map);
            ReadTerrainTexturesV56(reader, map);
        }

        ReadDecals(reader, map);
        ReadDecalGroups(reader, map);
        ReadNormalMap(reader, map);
        ReadTextureMasks(reader, map);
        ReadWaterMaps(reader, map);

        if (map.VersionMinor >= 60)
        {
            ReadSkyBox(reader, map);
        }

        ReadProps(reader, map);

        return map;
    }

    private static void ReadHeader(BinaryReader reader, ScMap map)
    {
        int magic = reader.ReadInt32();
        if (magic != ScmapMagic)
            throw new InvalidDataException($"Invalid .scmap magic: 0x{magic:X8}, expected 0x{ScmapMagic:X8}");

        map.VersionMajor = reader.ReadInt32();
        map.HeaderUnknown1 = reader.ReadInt32();
        map.HeaderUnknown2 = reader.ReadInt32();
        map.MapWidth = reader.ReadSingle();
        map.MapHeight = reader.ReadSingle();
        map.HeaderUnknown3 = reader.ReadInt32();
        map.HeaderUnknown4 = reader.ReadInt16();
    }

    private static void ReadPreview(BinaryReader reader, ScMap map)
    {
        map.PreviewImageDds = DdsHelper.ReadDdsBlob(reader);
    }

    private static void ReadHeightmap(BinaryReader reader, ScMap map)
    {
        map.Heightmap.Width = reader.ReadInt32();
        map.Heightmap.Height = reader.ReadInt32();
        map.Heightmap.HeightScale = reader.ReadSingle();

        int count = (map.Heightmap.Width + 1) * (map.Heightmap.Height + 1);
        map.Heightmap.Data = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            map.Heightmap.Data[i] = reader.ReadUInt16();
        }

        // v56+ writes a null padding byte after the heightmap; v53 doesn't.
        if (map.VersionMinor > 53)
            reader.ReadByte();
    }

    private static void ReadTerrainShaderAndEnvironment(BinaryReader reader, ScMap map)
    {
        map.TerrainShader = reader.ReadNullTerminatedString();
        map.BackgroundTexturePath = reader.ReadNullTerminatedString();
        map.SkyCubemapPath = reader.ReadNullTerminatedString();

        if (map.VersionMinor < 56)
        {
            // v53 and v54: single environment cubemap path, no count
            var path = reader.ReadNullTerminatedString();
            map.EnvironmentCubeMaps =
            [
                new EnvironmentCubeMap { Name = "<default>", FilePath = path }
            ];
        }
        else
        {
            // v56+: count followed by name/path pairs
            int cubemapCount = reader.ReadInt32();
            map.EnvironmentCubeMaps = new List<EnvironmentCubeMap>(cubemapCount);
            for (int i = 0; i < cubemapCount; i++)
            {
                map.EnvironmentCubeMaps.Add(new EnvironmentCubeMap
                {
                    Name = reader.ReadNullTerminatedString(),
                    FilePath = reader.ReadNullTerminatedString()
                });
            }
        }
    }

    private static void ReadLighting(BinaryReader reader, ScMap map)
    {
        var l = map.Lighting;
        l.LightingMultiplier = reader.ReadSingle();
        l.SunDirection = reader.ReadVector3();
        l.SunAmbience = reader.ReadVector3();
        l.SunColor = reader.ReadVector3();
        l.ShadowFillColor = reader.ReadVector3();
        l.SpecularColor = reader.ReadVector4();
        l.Bloom = reader.ReadSingle();
        l.FogColor = reader.ReadVector3();
        l.FogStart = reader.ReadSingle();
        l.FogEnd = reader.ReadSingle();
    }

    private static void ReadWater(BinaryReader reader, ScMap map)
    {
        var w = map.Water;
        w.HasWater = reader.ReadByte() != 0;
        w.Elevation = reader.ReadSingle();
        w.ElevationDeep = reader.ReadSingle();
        w.ElevationAbyss = reader.ReadSingle();

        w.SurfaceColor = reader.ReadVector3();
        w.ColorLerp = reader.ReadVector2();
        w.RefractionScale = reader.ReadSingle();
        w.FresnelBias = reader.ReadSingle();
        w.FresnelPower = reader.ReadSingle();
        w.UnitReflection = reader.ReadSingle();
        w.SkyReflection = reader.ReadSingle();

        w.SunShininess = reader.ReadSingle();
        w.SunStrength = reader.ReadSingle();
        w.SunDirection = reader.ReadVector3();
        w.SunColor = reader.ReadVector3();
        w.SunReflection = reader.ReadSingle();
        w.SunGlow = reader.ReadSingle();

        w.CubemapFile = reader.ReadNullTerminatedString();
        w.WaterRampFile = reader.ReadNullTerminatedString();

        for (int i = 0; i < 4; i++)
            w.NormalRepeats[i] = reader.ReadSingle();

        for (int i = 0; i < 4; i++)
        {
            w.WaveTextures[i] = new WaveTexture
            {
                Movement = reader.ReadVector2(),
                TexturePath = reader.ReadNullTerminatedString()
            };
        }
    }

    private static void ReadWaveGenerators(BinaryReader reader, ScMap map)
    {
        int count = reader.ReadInt32();
        map.WaveGenerators = new List<WaveGenerator>(count);

        for (int i = 0; i < count; i++)
        {
            map.WaveGenerators.Add(new WaveGenerator
            {
                TextureName = reader.ReadNullTerminatedString(),
                RampName = reader.ReadNullTerminatedString(),
                Position = reader.ReadVector3(),
                Rotation = reader.ReadSingle(),
                Velocity = reader.ReadVector3(),
                LifetimeFirst = reader.ReadSingle(),
                LifetimeSecond = reader.ReadSingle(),
                PeriodFirst = reader.ReadSingle(),
                PeriodSecond = reader.ReadSingle(),
                ScaleFirst = reader.ReadSingle(),
                ScaleSecond = reader.ReadSingle(),
                FrameCount = reader.ReadSingle(),
                FrameRateFirst = reader.ReadSingle(),
                FrameRateSecond = reader.ReadSingle(),
                StripCount = reader.ReadSingle()
            });
        }
    }

    /// <summary>
    /// v53: tileset name + layer count + per-layer (albedo_path, normal_path, albedo_scale, normal_scale)
    /// Replaces both the cartographic section and the v56 fixed-count texture section.
    /// </summary>
    private static void ReadTerrainTexturesV53(BinaryReader reader, ScMap map)
    {
        map.TilesetName = reader.ReadNullTerminatedString();

        int layerCount = reader.ReadInt32();
        map.TerrainTextures = new TerrainTexture[layerCount];
        for (int i = 0; i < layerCount; i++)
        {
            map.TerrainTextures[i] = new TerrainTexture
            {
                AlbedoPath = reader.ReadNullTerminatedString(),
                NormalPath = reader.ReadNullTerminatedString(),
                AlbedoScale = reader.ReadSingle(),
                NormalScale = reader.ReadSingle()
            };
        }

        // Two unknown ints (same as v56, but values differ)
        map.TextureUnknown1 = reader.ReadInt32();
        map.TextureUnknown2 = reader.ReadInt32();
    }

    private static void ReadCartographic(BinaryReader reader, ScMap map)
    {
        if (map.VersionMinor <= 56)
        {
            map.CartographicRawBytes = reader.ReadBytes(24);
        }
        else
        {
            map.Cartographic.ContourInterval = reader.ReadInt32();
            map.Cartographic.DeepWaterColor = reader.ReadInt32();
            map.Cartographic.ContourColor = reader.ReadInt32();
            map.Cartographic.ShoreColor = reader.ReadInt32();
            map.Cartographic.LandStartColor = reader.ReadInt32();
            if (map.VersionMinor >= 60)
            {
                map.Cartographic.LandEndColor = reader.ReadInt32();
            }
        }
    }

    /// <summary>
    /// v56+: fixed 10 albedo (path+scale), 9 normal (path+scale), 2 unknown ints.
    /// </summary>
    private static void ReadTerrainTexturesV56(BinaryReader reader, ScMap map)
    {
        for (int i = 0; i < 10; i++)
        {
            map.TerrainTextures[i].AlbedoPath = reader.ReadNullTerminatedString();
            map.TerrainTextures[i].AlbedoScale = reader.ReadSingle();
        }

        for (int i = 0; i < 9; i++)
        {
            map.TerrainTextures[i].NormalPath = reader.ReadNullTerminatedString();
            map.TerrainTextures[i].NormalScale = reader.ReadSingle();
        }

        map.TextureUnknown1 = reader.ReadInt32();
        map.TextureUnknown2 = reader.ReadInt32();
    }

    private static void ReadDecals(BinaryReader reader, ScMap map)
    {
        int count = reader.ReadInt32();
        map.Decals = new List<Decal>(count);

        for (int i = 0; i < count; i++)
        {
            map.Decals.Add(new Decal
            {
                Id = reader.ReadInt32(),
                Type = (DecalType)reader.ReadInt32(),
                Unknown = reader.ReadInt32(),
                TexturePath1 = reader.ReadLengthPrefixedString(),
                TexturePath2 = reader.ReadLengthPrefixedString(),
                Scale = reader.ReadVector3(),
                Position = reader.ReadVector3(),
                Rotation = reader.ReadVector3(),
                CutOffLod = reader.ReadSingle(),
                NearCutOffLod = reader.ReadSingle(),
                OwnerArmy = reader.ReadInt32()
            });
        }
    }

    private static void ReadDecalGroups(BinaryReader reader, ScMap map)
    {
        int count = reader.ReadInt32();
        map.DecalGroups = new List<DecalGroup>(count);

        for (int i = 0; i < count; i++)
        {
            var group = new DecalGroup
            {
                Id = reader.ReadInt32(),
                Name = reader.ReadNullTerminatedString()
            };

            int entryCount = reader.ReadInt32();
            group.DecalIds = new List<int>(entryCount);
            for (int j = 0; j < entryCount; j++)
                group.DecalIds.Add(reader.ReadInt32());

            map.DecalGroups.Add(group);
        }
    }

    private static void ReadNormalMap(BinaryReader reader, ScMap map)
    {
        map.NormalMapWidth = reader.ReadInt32();
        map.NormalMapHeight = reader.ReadInt32();

        // Most maps have normalCount=1; 4096-sized vanilla maps ship with normalCount=4 (3 extra
        // unused DDS blobs follow). Read all of them so we don't desync the stream.
        int normalCount = reader.ReadInt32();
        if (normalCount > 0)
            map.NormalMapDds = DdsHelper.ReadDdsBlob(reader);
        for (int i = 1; i < normalCount; i++)
            map.ExtraNormalMapDds.Add(DdsHelper.ReadDdsBlob(reader));
    }

    private static void ReadTextureMasks(BinaryReader reader, ScMap map)
    {
        if (map.VersionMinor <= 53)
        {
            // v53 is inconsistent across vanilla maps: some (SCMP_001, SCMP_009) prefix each mask
            // with a count int (= 1), others (SCMP_030, SCMP_040) skip it. Sniff the first int —
            // if it's 1 we're in count-prefix mode; otherwise rewind and treat the bytes as the
            // DDS blob length directly. Record which variant for the writer.
            int sniff = reader.ReadInt32();
            if (sniff == 1)
            {
                map.V53MasksHaveCountPrefix = true;
                map.TextureMaskLow.DdsData = DdsHelper.ReadDdsBlob(reader);
                int countHigh = reader.ReadInt32();
                if (countHigh > 0)
                    map.TextureMaskHigh.DdsData = DdsHelper.ReadDdsBlob(reader);
            }
            else
            {
                // Rewind and read both masks as plain length-prefixed DDS blobs.
                map.V53MasksHaveCountPrefix = false;
                reader.BaseStream.Seek(-4, SeekOrigin.Current);
                map.TextureMaskLow.DdsData = DdsHelper.ReadDdsBlob(reader);
                map.TextureMaskHigh.DdsData = DdsHelper.ReadDdsBlob(reader);
            }
        }
        else
        {
            // v56+: two DDS blobs directly, no count prefix
            map.TextureMaskLow.DdsData = DdsHelper.ReadDdsBlob(reader);
            map.TextureMaskHigh.DdsData = DdsHelper.ReadDdsBlob(reader);
        }

        // Extract dimensions from headers
        if (map.TextureMaskLow.DdsData.Length >= DdsHelper.DdsHeaderSize)
        {
            var (w, h) = DdsHelper.GetDdsDimensions(map.TextureMaskLow.DdsData);
            map.TextureMaskLow.Width = w;
            map.TextureMaskLow.Height = h;
        }
        if (map.TextureMaskHigh.DdsData.Length >= DdsHelper.DdsHeaderSize)
        {
            var (w, h) = DdsHelper.GetDdsDimensions(map.TextureMaskHigh.DdsData);
            map.TextureMaskHigh.Width = w;
            map.TextureMaskHigh.Height = h;
        }
    }

    private static void ReadWaterMaps(BinaryReader reader, ScMap map)
    {
        if (map.VersionMinor <= 53)
        {
            // v53: no water map DDS, aux maps start directly
        }
        else
        {
            // v56+: same SCMP_018/SCMP_029 inconsistency as the v53 texture masks. Sniff the
            // first int — = 0: no water map; = 1: count-prefix mode (read DDS); other: it's the
            // DDS length itself, rewind 4 bytes and read directly.
            int sniff = reader.ReadInt32();
            if (sniff == 0)
            {
                map.V56WaterMapHasCountPrefix = true;
            }
            else if (sniff == 1)
            {
                map.V56WaterMapHasCountPrefix = true;
                map.WaterMapDds = DdsHelper.ReadDdsBlob(reader);
            }
            else
            {
                map.V56WaterMapHasCountPrefix = false;
                reader.BaseStream.Seek(-4, SeekOrigin.Current);
                map.WaterMapDds = DdsHelper.ReadDdsBlob(reader);
            }
        }

        int halfWidth = map.Heightmap.Width / 2;
        int halfHeight = map.Heightmap.Height / 2;
        int halfSize = halfWidth * halfHeight;

        map.WaterFoamMask = reader.ReadBytes(halfSize);
        map.WaterFlatness = reader.ReadBytes(halfSize);
        map.WaterDepthBias = reader.ReadBytes(halfSize);

        int fullSize = map.Heightmap.Width * map.Heightmap.Height;
        map.TerrainTypeData = reader.ReadBytes(fullSize);
    }

    private static void ReadSkyBox(BinaryReader reader, ScMap map)
    {
        var sky = new SkyBox();

        sky.Position = reader.ReadVector3();
        sky.HorizonHeight = reader.ReadSingle();
        sky.Scale = reader.ReadSingle();
        sky.SubHeight = reader.ReadSingle();
        sky.SubdivisionAxis = reader.ReadInt32();
        sky.SubdivisionHeight = reader.ReadInt32();
        sky.ZenithHeight = reader.ReadSingle();
        sky.HorizonColor = reader.ReadVector3();
        sky.ZenithColor = reader.ReadVector3();

        sky.DecalGlowMultiplier = reader.ReadSingle();

        sky.AlbedoTexturePath = reader.ReadNullTerminatedString();
        sky.GlowTexturePath = reader.ReadNullTerminatedString();

        int planetCount = reader.ReadInt32();
        sky.Planets = new List<Planet>(planetCount);
        for (int i = 0; i < planetCount; i++)
        {
            sky.Planets.Add(new Planet
            {
                Position = reader.ReadVector3(),
                Rotation = reader.ReadSingle(),
                Scale = reader.ReadVector2(),
                Uv = reader.ReadVector4()
            });
        }

        sky.MidColorRed = reader.ReadByte();
        sky.MidColorBlue = reader.ReadByte();
        sky.MidColorGreen = reader.ReadByte();

        sky.CirrusMultiplier = reader.ReadSingle();
        sky.CirrusColor = reader.ReadVector3();
        sky.CirrusTexturePath = reader.ReadNullTerminatedString();

        int cirrusLayerCount = reader.ReadInt32();
        sky.CirrusLayers = new List<CirrusLayer>(cirrusLayerCount);
        for (int i = 0; i < cirrusLayerCount; i++)
        {
            sky.CirrusLayers.Add(new CirrusLayer
            {
                Frequency = reader.ReadVector2(),
                Speed = reader.ReadSingle(),
                Direction = reader.ReadVector2()
            });
        }

        map.SkyBox = sky;
    }

    private static void ReadProps(BinaryReader reader, ScMap map)
    {
        // 4096×4096 vanilla maps (SCMP_029, SCMP_030) embed an undocumented blob between the
        // watermaps section and the props section. Locate where the props count actually starts
        // by scanning for "int count + '/' starting a blueprint path", and preserve the bytes in
        // between as PostWatermapsExtra so the writer can round-trip them verbatim.
        long startPos = reader.BaseStream.Position;
        long propsCountPos = LocatePropsCount(reader, startPos);
        if (propsCountPos > startPos)
        {
            int blobLen = (int)(propsCountPos - startPos);
            reader.BaseStream.Seek(startPos, SeekOrigin.Begin);
            map.PostWatermapsExtra = reader.ReadBytes(blobLen);
        }

        int count = reader.ReadInt32();
        map.Props = new List<Prop>(count);

        for (int i = 0; i < count; i++)
        {
            map.Props.Add(new Prop
            {
                BlueprintPath = reader.ReadNullTerminatedString(),
                Position = reader.ReadVector3(),
                RotationX = reader.ReadVector3(),
                RotationY = reader.ReadVector3(),
                RotationZ = reader.ReadVector3(),
                Scale = reader.ReadVector3()
            });
        }
    }

    /// <summary>Find the offset where the props count int lives. For most maps it's exactly
    /// `startPos`; for 4096-sized maps an unknown 25–37 MB blob precedes it. We accept a
    /// candidate position when bytes [pos..pos+3] form a plausible count (0 ≤ N ≤ 10⁶) AND the
    /// next byte is '/' (the start of a typical BlueprintPath like "/env/..."). If we can't find
    /// such a pattern, fall back to startPos and let the existing logic read whatever's there
    /// (which historically gave count=0, harmless).</summary>
    private static long LocatePropsCount(BinaryReader reader, long startPos)
    {
        long len = reader.BaseStream.Length;
        // Try the obvious position first: most maps have props right after watermaps.
        if (CountThenSlash(reader, startPos)) return startPos;
        if (CountIsZeroAtEof(reader, startPos)) return startPos;

        // 4096-sized vanilla maps hide the props count behind a big opaque blob. Read everything
        // remaining into memory and scan for the count-then-slash pattern. Cheap on a 40 MB blob.
        int bufLen = (int)(len - startPos);
        reader.BaseStream.Seek(startPos, SeekOrigin.Begin);
        byte[] buf = reader.ReadBytes(bufLen);
        for (int i = 0; i + 5 <= bufLen; i++)
        {
            if (buf[i + 4] != 0x2F) continue; // require '/' at position +4
            int candidate = BitConverter.ToInt32(buf, i);
            if (candidate < 0 || candidate > 1_000_000) continue;
            return startPos + i;
        }
        // Nothing matched — restore position and signal caller (no blob).
        reader.BaseStream.Seek(startPos, SeekOrigin.Begin);
        return startPos;
    }

    private static bool CountThenSlash(BinaryReader reader, long pos)
    {
        long save = reader.BaseStream.Position;
        reader.BaseStream.Seek(pos, SeekOrigin.Begin);
        try
        {
            if (reader.BaseStream.Length - pos < 5) return false;
            int count = reader.ReadInt32();
            byte next = reader.ReadByte();
            return next == 0x2F && count >= 0 && count <= 1_000_000;
        }
        finally { reader.BaseStream.Seek(save, SeekOrigin.Begin); }
    }

    private static bool CountIsZeroAtEof(BinaryReader reader, long pos)
    {
        long save = reader.BaseStream.Position;
        reader.BaseStream.Seek(pos, SeekOrigin.Begin);
        try
        {
            if (reader.BaseStream.Length - pos < 4) return true; // not even room for a count → no props
            int count = reader.ReadInt32();
            return count == 0 && reader.BaseStream.Position >= reader.BaseStream.Length - 8;
        }
        finally { reader.BaseStream.Seek(save, SeekOrigin.Begin); }
    }
}
