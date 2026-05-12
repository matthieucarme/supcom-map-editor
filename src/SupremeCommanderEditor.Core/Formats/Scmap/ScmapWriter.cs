using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Formats.Scmap;

/// <summary>
/// Writes an ScMap model to a Supreme Commander .scmap binary file.
/// Mirror of ScmapReader — field order must match exactly for round-trip fidelity.
/// </summary>
public static class ScmapWriter
{
    public static void Write(string filePath, ScMap map)
    {
        using var fs = File.Create(filePath);
        using var writer = new BinaryWriter(fs);
        Write(writer, map);
    }

    public static void Write(BinaryWriter writer, ScMap map)
    {
        WriteHeader(writer, map);
        WritePreview(writer, map);
        writer.Write(map.VersionMinor);
        WriteHeightmap(writer, map);
        WriteTerrainShaderAndEnvironment(writer, map);
        WriteLighting(writer, map);
        WriteWater(writer, map);
        WriteWaveGenerators(writer, map);

        if (map.VersionMinor <= 53)
        {
            WriteTerrainTexturesV53(writer, map);
        }
        else
        {
            WriteCartographic(writer, map);
            WriteTerrainTexturesV56(writer, map);
        }

        WriteDecals(writer, map);
        WriteDecalGroups(writer, map);
        WriteNormalMap(writer, map);
        WriteTextureMasks(writer, map);
        WriteWaterMaps(writer, map);

        if (map.VersionMinor >= 60)
        {
            WriteSkyBox(writer, map);
        }

        WriteProps(writer, map);
    }

    private static void WriteHeader(BinaryWriter writer, ScMap map)
    {
        writer.Write(ScmapReader.ScmapMagic);
        writer.Write(map.VersionMajor);
        writer.Write(map.HeaderUnknown1);
        writer.Write(map.HeaderUnknown2);
        writer.Write(map.MapWidth);
        writer.Write(map.MapHeight);
        writer.Write(map.HeaderUnknown3);
        writer.Write(map.HeaderUnknown4);
    }

    private static void WritePreview(BinaryWriter writer, ScMap map)
    {
        DdsHelper.WriteDdsBlob(writer, map.PreviewImageDds);
    }

    private static void WriteHeightmap(BinaryWriter writer, ScMap map)
    {
        writer.Write(map.Heightmap.Width);
        writer.Write(map.Heightmap.Height);
        writer.Write(map.Heightmap.HeightScale);

        foreach (var value in map.Heightmap.Data)
            writer.Write(value);

        // v56+ writes a null padding byte after the heightmap; v53 doesn't.
        if (map.VersionMinor > 53)
            writer.Write((byte)0);
    }

    private static void WriteTerrainShaderAndEnvironment(BinaryWriter writer, ScMap map)
    {
        writer.WriteNullTerminatedString(map.TerrainShader);
        writer.WriteNullTerminatedString(map.BackgroundTexturePath);
        writer.WriteNullTerminatedString(map.SkyCubemapPath);

        if (map.VersionMinor < 56)
        {
            var path = map.EnvironmentCubeMaps.Count > 0
                ? map.EnvironmentCubeMaps[0].FilePath
                : string.Empty;
            writer.WriteNullTerminatedString(path);
        }
        else
        {
            writer.Write(map.EnvironmentCubeMaps.Count);
            foreach (var cubeMap in map.EnvironmentCubeMaps)
            {
                writer.WriteNullTerminatedString(cubeMap.Name);
                writer.WriteNullTerminatedString(cubeMap.FilePath);
            }
        }
    }

    private static void WriteLighting(BinaryWriter writer, ScMap map)
    {
        var l = map.Lighting;
        writer.Write(l.LightingMultiplier);
        writer.WriteVector3(l.SunDirection);
        writer.WriteVector3(l.SunAmbience);
        writer.WriteVector3(l.SunColor);
        writer.WriteVector3(l.ShadowFillColor);
        writer.WriteVector4(l.SpecularColor);
        writer.Write(l.Bloom);
        writer.WriteVector3(l.FogColor);
        writer.Write(l.FogStart);
        writer.Write(l.FogEnd);
    }

    private static void WriteWater(BinaryWriter writer, ScMap map)
    {
        var w = map.Water;
        writer.Write((byte)(w.HasWater ? 1 : 0));
        writer.Write(w.Elevation);
        writer.Write(w.ElevationDeep);
        writer.Write(w.ElevationAbyss);

        writer.WriteVector3(w.SurfaceColor);
        writer.WriteVector2(w.ColorLerp);
        writer.Write(w.RefractionScale);
        writer.Write(w.FresnelBias);
        writer.Write(w.FresnelPower);
        writer.Write(w.UnitReflection);
        writer.Write(w.SkyReflection);

        writer.Write(w.SunShininess);
        writer.Write(w.SunStrength);
        writer.WriteVector3(w.SunDirection);
        writer.WriteVector3(w.SunColor);
        writer.Write(w.SunReflection);
        writer.Write(w.SunGlow);

        writer.WriteNullTerminatedString(w.CubemapFile);
        writer.WriteNullTerminatedString(w.WaterRampFile);

        for (int i = 0; i < 4; i++)
            writer.Write(w.NormalRepeats[i]);

        for (int i = 0; i < 4; i++)
        {
            writer.WriteVector2(w.WaveTextures[i].Movement);
            writer.WriteNullTerminatedString(w.WaveTextures[i].TexturePath);
        }
    }

    private static void WriteWaveGenerators(BinaryWriter writer, ScMap map)
    {
        writer.Write(map.WaveGenerators.Count);

        foreach (var wg in map.WaveGenerators)
        {
            writer.WriteNullTerminatedString(wg.TextureName);
            writer.WriteNullTerminatedString(wg.RampName);
            writer.WriteVector3(wg.Position);
            writer.Write(wg.Rotation);
            writer.WriteVector3(wg.Velocity);
            writer.Write(wg.LifetimeFirst);
            writer.Write(wg.LifetimeSecond);
            writer.Write(wg.PeriodFirst);
            writer.Write(wg.PeriodSecond);
            writer.Write(wg.ScaleFirst);
            writer.Write(wg.ScaleSecond);
            writer.Write(wg.FrameCount);
            writer.Write(wg.FrameRateFirst);
            writer.Write(wg.FrameRateSecond);
            writer.Write(wg.StripCount);
        }
    }

    private static void WriteTerrainTexturesV53(BinaryWriter writer, ScMap map)
    {
        writer.WriteNullTerminatedString(map.TilesetName ?? "No Tileset");

        writer.Write(map.TerrainTextures.Length);
        foreach (var tex in map.TerrainTextures)
        {
            writer.WriteNullTerminatedString(tex.AlbedoPath);
            writer.WriteNullTerminatedString(tex.NormalPath);
            writer.Write(tex.AlbedoScale);
            writer.Write(tex.NormalScale);
        }

        writer.Write(map.TextureUnknown1);
        writer.Write(map.TextureUnknown2);
    }

    private static void WriteCartographic(BinaryWriter writer, ScMap map)
    {
        if (map.VersionMinor <= 56)
        {
            writer.Write(map.CartographicRawBytes ?? new byte[24]);
        }
        else
        {
            writer.Write(map.Cartographic.ContourInterval);
            writer.Write(map.Cartographic.DeepWaterColor);
            writer.Write(map.Cartographic.ContourColor);
            writer.Write(map.Cartographic.ShoreColor);
            writer.Write(map.Cartographic.LandStartColor);
            if (map.VersionMinor >= 60)
            {
                writer.Write(map.Cartographic.LandEndColor);
            }
        }
    }

    private static void WriteTerrainTexturesV56(BinaryWriter writer, ScMap map)
    {
        for (int i = 0; i < 10; i++)
        {
            writer.WriteNullTerminatedString(map.TerrainTextures[i].AlbedoPath);
            writer.Write(map.TerrainTextures[i].AlbedoScale);
        }

        for (int i = 0; i < 9; i++)
        {
            writer.WriteNullTerminatedString(map.TerrainTextures[i].NormalPath);
            writer.Write(map.TerrainTextures[i].NormalScale);
        }

        writer.Write(map.TextureUnknown1);
        writer.Write(map.TextureUnknown2);
    }

    private static void WriteDecals(BinaryWriter writer, ScMap map)
    {
        writer.Write(map.Decals.Count);

        foreach (var decal in map.Decals)
        {
            writer.Write(decal.Id);
            writer.Write((int)decal.Type);
            writer.Write(decal.Unknown);
            writer.WriteLengthPrefixedString(decal.TexturePath1);
            writer.WriteLengthPrefixedString(decal.TexturePath2);
            writer.WriteVector3(decal.Scale);
            writer.WriteVector3(decal.Position);
            writer.WriteVector3(decal.Rotation);
            writer.Write(decal.CutOffLod);
            writer.Write(decal.NearCutOffLod);
            writer.Write(decal.OwnerArmy);
        }
    }

    private static void WriteDecalGroups(BinaryWriter writer, ScMap map)
    {
        writer.Write(map.DecalGroups.Count);

        foreach (var group in map.DecalGroups)
        {
            writer.Write(group.Id);
            writer.WriteNullTerminatedString(group.Name);
            writer.Write(group.DecalIds.Count);
            foreach (var decalId in group.DecalIds)
                writer.Write(decalId);
        }
    }

    private static void WriteNormalMap(BinaryWriter writer, ScMap map)
    {
        writer.Write(map.NormalMapWidth);
        writer.Write(map.NormalMapHeight);
        // Honour the original normal-map count (1 for most maps, 4 for 4096-sized vanilla maps).
        int count = 1 + map.ExtraNormalMapDds.Count;
        writer.Write(count);
        DdsHelper.WriteDdsBlob(writer, map.NormalMapDds);
        foreach (var extra in map.ExtraNormalMapDds)
            DdsHelper.WriteDdsBlob(writer, extra);
    }

    private static void WriteTextureMasks(BinaryWriter writer, ScMap map)
    {
        if (map.VersionMinor <= 53 && map.V53MasksHaveCountPrefix)
        {
            // v53 variant with count prefix before each DDS blob (e.g. SCMP_001).
            writer.Write(1);
            DdsHelper.WriteDdsBlob(writer, map.TextureMaskLow.DdsData);
            writer.Write(1);
            DdsHelper.WriteDdsBlob(writer, map.TextureMaskHigh.DdsData);
        }
        else
        {
            // v56+ and v53 maps without count prefix (e.g. SCMP_030, SCMP_040): two DDS blobs.
            DdsHelper.WriteDdsBlob(writer, map.TextureMaskLow.DdsData);
            DdsHelper.WriteDdsBlob(writer, map.TextureMaskHigh.DdsData);
        }
    }

    private static void WriteWaterMaps(BinaryWriter writer, ScMap map)
    {
        if (map.VersionMinor <= 53)
        {
            // v53: no water map DDS
        }
        else if (map.V56WaterMapHasCountPrefix)
        {
            // v56+ variant with count prefix (SCMP_018 style).
            writer.Write(map.WaterMapDds.Length > 0 ? 1 : 0);
            if (map.WaterMapDds.Length > 0)
                DdsHelper.WriteDdsBlob(writer, map.WaterMapDds);
        }
        else
        {
            // v56+ variant without count prefix (SCMP_029 style).
            DdsHelper.WriteDdsBlob(writer, map.WaterMapDds);
        }

        writer.Write(map.WaterFoamMask);
        writer.Write(map.WaterFlatness);
        writer.Write(map.WaterDepthBias);
        writer.Write(map.TerrainTypeData);
    }

    private static void WriteSkyBox(BinaryWriter writer, ScMap map)
    {
        var sky = map.SkyBox ?? new SkyBox();

        writer.WriteVector3(sky.Position);
        writer.Write(sky.HorizonHeight);
        writer.Write(sky.Scale);
        writer.Write(sky.SubHeight);
        writer.Write(sky.SubdivisionAxis);
        writer.Write(sky.SubdivisionHeight);
        writer.Write(sky.ZenithHeight);
        writer.WriteVector3(sky.HorizonColor);
        writer.WriteVector3(sky.ZenithColor);

        writer.Write(sky.DecalGlowMultiplier);

        writer.WriteNullTerminatedString(sky.AlbedoTexturePath);
        writer.WriteNullTerminatedString(sky.GlowTexturePath);

        writer.Write(sky.Planets.Count);
        foreach (var planet in sky.Planets)
        {
            writer.WriteVector3(planet.Position);
            writer.Write(planet.Rotation);
            writer.WriteVector2(planet.Scale);
            writer.WriteVector4(planet.Uv);
        }

        writer.Write(sky.MidColorRed);
        writer.Write(sky.MidColorBlue);
        writer.Write(sky.MidColorGreen);

        writer.Write(sky.CirrusMultiplier);
        writer.WriteVector3(sky.CirrusColor);
        writer.WriteNullTerminatedString(sky.CirrusTexturePath);

        writer.Write(sky.CirrusLayers.Count);
        foreach (var layer in sky.CirrusLayers)
        {
            writer.WriteVector2(layer.Frequency);
            writer.Write(layer.Speed);
            writer.WriteVector2(layer.Direction);
        }
    }

    private static void WriteProps(BinaryWriter writer, ScMap map)
    {
        // 4096-sized vanilla maps embed an undocumented blob between watermaps and props.
        // Reader captured those bytes verbatim — write them back unchanged for round-trip parity.
        if (map.PostWatermapsExtra is { Length: > 0 })
            writer.Write(map.PostWatermapsExtra);

        writer.Write(map.Props.Count);

        foreach (var prop in map.Props)
        {
            writer.WriteNullTerminatedString(prop.BlueprintPath);
            writer.WriteVector3(prop.Position);
            writer.WriteVector3(prop.RotationX);
            writer.WriteVector3(prop.RotationY);
            writer.WriteVector3(prop.RotationZ);
            writer.WriteVector3(prop.Scale);
        }
    }
}
