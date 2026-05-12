using SupremeCommanderEditor.Core.Formats.Scmap;
using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Services;

/// <summary>
/// Normalises the in-memory TerrainTextures layout so every map opened in the editor exposes the
/// same 10-slot structure regardless of its on-disk version. Vanilla v53 maps often ship with
/// fewer slots (6 is the typical count) and place the macro overlay at the last index — that gives
/// only 4 splatmap-paintable strata. Expanding to 10 slots and moving the macro to slot 9 unlocks
/// the full 8-strata paint workflow without changing how the map renders.
/// </summary>
public static class MapStrataNormalizer
{
    public const int TargetSlotCount = 10;
    public const int MacroSlot = 9;

    /// <summary>
    /// Expand the TerrainTextures array to <see cref="TargetSlotCount"/> slots. If the map
    /// already has that many (or more), this is a no-op. For short v53 maps, the original last
    /// slot is treated as the macro overlay and moved to slot 9; new intermediate slots are
    /// initialised empty AND the corresponding splatmap channels are zeroed so the shader
    /// doesn't render magenta where the old macro mask data still references those slots.
    /// </summary>
    public static void EnsureTenSlots(ScMap map)
    {
        if (map?.TerrainTextures == null) return;
        int oldLen = map.TerrainTextures.Length;
        if (oldLen >= TargetSlotCount) return;

        var expanded = new TerrainTexture[TargetSlotCount];
        // Slot 0 (base) and slots 1..oldLen-2 carry over identically.
        for (int i = 0; i < oldLen - 1; i++)
            expanded[i] = map.TerrainTextures[i];
        // Old last slot = macro overlay → goes to slot 9.
        if (oldLen > 0)
            expanded[MacroSlot] = map.TerrainTextures[oldLen - 1];
        else
            expanded[MacroSlot] = new TerrainTexture();
        // Fill the gap between (oldLen-1) and MacroSlot with empty entries.
        for (int i = oldLen - 1; i < MacroSlot; i++)
            if (i >= 0 && expanded[i] == null)
                expanded[i] = new TerrainTexture();
        for (int i = 0; i < TargetSlotCount; i++)
            expanded[i] ??= new TerrainTexture();

        map.TerrainTextures = expanded;

        // Zero out splatmap channels for any slot that's now empty (oldLen-1 through 8). In the
        // original short-v53 layout the slot at index N-1 was the alpha-blended macro overlay;
        // its mask channel was rendered as junk by SC1's engine (alpha-only blend, not splatmap)
        // but our normalised renderer blends slot 5..8 via mask1 — so we must clear that data,
        // otherwise the now-empty slot renders as magenta wherever the channel was non-zero.
        ClearMaskChannelsForSlots(map, oldLen - 1, 8);

        // Short v53 maps ship a degenerate "high" splatmap (1 byte per pixel, never used as a real
        // splatmap by the SC1 engine). Once we promote them to the 10-strata layout the renderer
        // does sample mask1 — so we replace the high mask with a fresh, blank ARGB DDS at the
        // same dimensions. Zero everywhere = no spurious blend of empty slots 5..8.
        if (map.TextureMaskHigh != null && map.TextureMaskHigh.Width > 0 && map.TextureMaskHigh.Height > 0)
        {
            int w = map.TextureMaskHigh.Width;
            int h = map.TextureMaskHigh.Height;
            var expectedLen = 128 + w * h * 4;
            if (map.TextureMaskHigh.DdsData == null || map.TextureMaskHigh.DdsData.Length != expectedLen)
            {
                map.TextureMaskHigh.DdsData = DdsHelper.CreateArgbDds(w, h, new byte[w * h * 4]);
            }
        }
    }

    private static void ClearMaskChannelsForSlots(ScMap map, int fromSlot, int toSlot)
    {
        for (int slot = fromSlot; slot <= toSlot; slot++)
        {
            if (slot < 1 || slot > 8) continue;
            var mask = slot <= 4 ? map.TextureMaskLow : map.TextureMaskHigh;
            if (mask?.DdsData == null) continue;
            const int headerSize = 128;
            int pixels = mask.Width * mask.Height;
            if (mask.DdsData.Length < headerSize + pixels * 4) continue;
            int withinMask = ((slot - 1) % 4) + 1;
            // Match the channel-offset convention used by MapGenerator.WriteStrataChannel.
            int channelOffset = withinMask switch { 1 => 2, 2 => 1, 3 => 0, 4 => 3, _ => 0 };
            for (int p = 0; p < pixels; p++)
                mask.DdsData[headerSize + p * 4 + channelOffset] = 0;
        }
    }
}
