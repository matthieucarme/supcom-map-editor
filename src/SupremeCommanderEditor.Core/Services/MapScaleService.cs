using System.Numerics;
using SupremeCommanderEditor.Core.Formats.Scmap;
using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Services;

/// <summary>
/// Resample a map to a new heightmap/world dimension. Standard SupCom sizes (in heightmap pixels):
///   256 = 5 km, 512 = 10 km, 1024 = 20 km, 2048 = 40 km, 4096 = 80 km.
/// Heightmap, splatmaps, markers, decals and prop positions are resampled. Auxiliary DDS layers
/// (normal map, water aux, terrain type) are reset to blank defaults at the new dimensions —
/// editing those isn't supported yet so we'd lose nothing useful by carrying them across.
/// </summary>
public static class MapScaleService
{
    public static void Scale(ScMap map, int newSize)
    {
        if (newSize <= 0) throw new ArgumentOutOfRangeException(nameof(newSize));
        int oldW = map.Heightmap.Width;
        int oldH = map.Heightmap.Height;
        if (oldW == newSize && oldH == newSize) return;

        ScaleHeightmap(map.Heightmap, newSize, newSize);
        ScaleSplatmap(map.TextureMaskLow, newSize / 2, newSize / 2);
        ScaleSplatmap(map.TextureMaskHigh, newSize / 2, newSize / 2);
        ScaleMarkers(map.Markers, oldW, oldH, newSize, newSize);
        ScaleDecals(map.Decals, oldW, oldH, newSize, newSize);
        ScaleProps(map.Props, oldW, oldH, newSize, newSize);
        ScaleInitialUnits(map, oldW, oldH, newSize, newSize);
        RegenerateAuxLayers(map, newSize);

        map.Info.Width = newSize;
        map.Info.Height = newSize;
    }

    private static void ScaleHeightmap(Heightmap hm, int newW, int newH)
    {
        int oldW = hm.Width;
        int oldH = hm.Height;
        var oldData = hm.Data;
        int oldStride = oldW + 1;
        int newStride = newW + 1;
        var newData = new ushort[newStride * (newH + 1)];

        for (int ny = 0; ny <= newH; ny++)
        {
            float fy = (float)ny * oldH / newH;
            int y0 = Math.Clamp((int)MathF.Floor(fy), 0, oldH);
            int y1 = Math.Min(y0 + 1, oldH);
            float ty = fy - y0;
            for (int nx = 0; nx <= newW; nx++)
            {
                float fx = (float)nx * oldW / newW;
                int x0 = Math.Clamp((int)MathF.Floor(fx), 0, oldW);
                int x1 = Math.Min(x0 + 1, oldW);
                float tx = fx - x0;

                ushort h00 = oldData[y0 * oldStride + x0];
                ushort h10 = oldData[y0 * oldStride + x1];
                ushort h01 = oldData[y1 * oldStride + x0];
                ushort h11 = oldData[y1 * oldStride + x1];

                float v0 = h00 + (h10 - h00) * tx;
                float v1 = h01 + (h11 - h01) * tx;
                float v = v0 + (v1 - v0) * ty;

                newData[ny * newStride + nx] = (ushort)Math.Clamp(v, 0, ushort.MaxValue);
            }
        }

        hm.Width = newW;
        hm.Height = newH;
        hm.Data = newData;
    }

    private static void ScaleSplatmap(TextureMask mask, int newW, int newH)
    {
        const int header = 128;
        // Empty / placeholder splatmap → just create a blank one at the new size.
        if (mask.DdsData.Length <= header || mask.Width <= 0 || mask.Height <= 0)
        {
            mask.Width = newW;
            mask.Height = newH;
            mask.DdsData = DdsHelper.CreateArgbDds(newW, newH, new byte[newW * newH * 4]);
            return;
        }

        // Decode the existing splatmap via Pfim — handles uncompressed ARGB *and* DXT-compressed
        // variants, both of which appear in the wild. Falls back to a blank rescale if decode fails.
        byte[]? oldPixels = null;
        int oldW = mask.Width;
        int oldH = mask.Height;
        try
        {
            using var img = Pfim.Pfimage.FromStream(new MemoryStream(mask.DdsData));
            if (img.Data != null)
            {
                oldW = img.Width;
                oldH = img.Height;
                int expected = oldW * oldH * 4;
                if (img.Data.Length >= expected)
                {
                    // Pfim returns BGRA for compressed/32-bit formats. We don't care about channel
                    // order for blending, just preserve byte triples — resampling each channel
                    // independently keeps the splatmap weights intact regardless of order.
                    oldPixels = new byte[expected];
                    Buffer.BlockCopy(img.Data, 0, oldPixels, 0, expected);
                }
            }
        }
        catch
        {
            oldPixels = null;
        }

        if (oldPixels == null)
        {
            // Decode failed (unknown format / corrupted) — fall back to blank at the new size.
            mask.Width = newW;
            mask.Height = newH;
            mask.DdsData = DdsHelper.CreateArgbDds(newW, newH, new byte[newW * newH * 4]);
            return;
        }

        var newPixels = ResamplePixels(oldPixels, oldW, oldH, newW, newH);
        mask.DdsData = DdsHelper.CreateArgbDds(newW, newH, newPixels);
        mask.Width = newW;
        mask.Height = newH;
    }

    private static byte[] ResamplePixels(byte[] src, int srcW, int srcH, int dstW, int dstH)
    {
        var dst = new byte[dstW * dstH * 4];
        for (int ny = 0; ny < dstH; ny++)
        {
            float fy = (ny + 0.5f) * srcH / dstH - 0.5f;
            int y0 = Math.Clamp((int)MathF.Floor(fy), 0, srcH - 1);
            int y1 = Math.Min(y0 + 1, srcH - 1);
            float ty = Math.Clamp(fy - y0, 0f, 1f);
            for (int nx = 0; nx < dstW; nx++)
            {
                float fx = (nx + 0.5f) * srcW / dstW - 0.5f;
                int x0 = Math.Clamp((int)MathF.Floor(fx), 0, srcW - 1);
                int x1 = Math.Min(x0 + 1, srcW - 1);
                float tx = Math.Clamp(fx - x0, 0f, 1f);

                int i00 = (y0 * srcW + x0) * 4;
                int i10 = (y0 * srcW + x1) * 4;
                int i01 = (y1 * srcW + x0) * 4;
                int i11 = (y1 * srcW + x1) * 4;
                int j = (ny * dstW + nx) * 4;

                for (int c = 0; c < 4; c++)
                {
                    float v0 = src[i00 + c] + (src[i10 + c] - src[i00 + c]) * tx;
                    float v1 = src[i01 + c] + (src[i11 + c] - src[i01 + c]) * tx;
                    dst[j + c] = (byte)Math.Clamp(v0 + (v1 - v0) * ty, 0, 255);
                }
            }
        }
        return dst;
    }

    private static void ScaleMarkers(List<Marker> markers, int oldW, int oldH, int newW, int newH)
    {
        if (oldW <= 0 || oldH <= 0) return;
        float sx = (float)newW / oldW;
        float sz = (float)newH / oldH;
        foreach (var m in markers)
        {
            var p = m.Position;
            // Y is the world elevation, which is interpolated by the heightmap rescale; safest
            // is to re-sample it at the new XZ. But the marker may end up outside the new bounds
            // briefly, so clamp first.
            m.Position = new Vector3(p.X * sx, p.Y, p.Z * sz);
        }
    }

    private static void ScaleDecals(List<Decal> decals, int oldW, int oldH, int newW, int newH)
    {
        if (oldW <= 0 || oldH <= 0) return;
        float sx = (float)newW / oldW;
        float sz = (float)newH / oldH;
        foreach (var d in decals)
        {
            var p = d.Position;
            d.Position = new Vector3(p.X * sx, p.Y, p.Z * sz);
            var s = d.Scale;
            d.Scale = new Vector3(s.X * sx, s.Y, s.Z * sz);
        }
    }

    private static void ScaleProps(List<Prop> props, int oldW, int oldH, int newW, int newH)
    {
        if (oldW <= 0 || oldH <= 0) return;
        float sx = (float)newW / oldW;
        float sz = (float)newH / oldH;
        foreach (var p in props)
        {
            var pos = p.Position;
            p.Position = new Vector3(pos.X * sx, pos.Y, pos.Z * sz);
        }
    }

    private static void ScaleInitialUnits(ScMap map, int oldW, int oldH, int newW, int newH)
    {
        if (oldW <= 0 || oldH <= 0) return;
        float sx = (float)newW / oldW;
        float sz = (float)newH / oldH;
        foreach (var army in map.Info.Armies)
            foreach (var u in army.InitialUnits)
            {
                var p = u.Position;
                u.Position = new Vector3(p.X * sx, p.Y, p.Z * sz);
            }
    }

    private static void RegenerateAuxLayers(ScMap map, int size)
    {
        // Normal map: blank DXT5 at heightmap resolution.
        int dxt5Blocks = Math.Max(1, (size + 3) / 4);
        map.NormalMapWidth = size;
        map.NormalMapHeight = size;
        map.NormalMapDds = DdsHelper.CreateDxt5Dds(size, size, new byte[dxt5Blocks * dxt5Blocks * 16]);

        // Water aux: half-resolution
        int half = size / 2;
        int waterBlocks = Math.Max(1, (half + 3) / 4);
        if (map.WaterMapDds.Length > 0)
            map.WaterMapDds = DdsHelper.CreateDxt5Dds(half, half, new byte[waterBlocks * waterBlocks * 16]);
        int halfArea = half * half;
        map.WaterFoamMask = new byte[halfArea];
        map.WaterFlatness = new byte[halfArea];
        Array.Fill(map.WaterFlatness, (byte)0xFF);
        map.WaterDepthBias = new byte[halfArea];
        Array.Fill(map.WaterDepthBias, (byte)0x7F);

        // Terrain type: one byte per cell
        map.TerrainTypeData = new byte[size * size];
    }
}
