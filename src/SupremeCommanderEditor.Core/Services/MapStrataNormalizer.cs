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
    /// <summary>SC1 vanilla v53 maps ship with 6 strata slots (base + 4 splatmap-blended + macro).
    /// We pad shorter v53 arrays up to this size, but never beyond — going to 10 (FA layout)
    /// makes vanilla SC1 crash on map load.</summary>
    public const int VanillaV53SlotCount = 6;
    /// <summary>v56+ canonical slot count (FA). Kept for the renderer's macro-slot reference.</summary>
    public const int FaSlotCount = 10;

    /// <summary>
    /// Expand the TerrainTextures array to <see cref="TargetSlotCount"/> slots. If the map
    /// already has that many (or more), this is a no-op. For short v53 maps, the original last
    /// slot is treated as the macro overlay and moved to slot 9; new intermediate slots are
    /// initialised empty AND the corresponding splatmap channels are zeroed so the shader
    /// doesn't render magenta where the old macro mask data still references those slots.
    /// </summary>
    /// <summary>
    /// Some maps (notably SCMP_021 v54) ship texture paths that aren't absolute — slot 0's
    /// albedo is just <c>"snow_albedo.dds"</c> instead of <c>"/env/tundra/layers/tund_snow_albedo.dds"</c>.
    /// SC1's engine resolves these by combining the tileset's layer directory with a tileset
    /// prefix; we infer both from the other absolute paths in the same texture array and rewrite
    /// the relative paths in-place. Without this fix the editor's texture cache falls back to a
    /// magenta texture for the unresolved slot, which then bleeds through wherever the splatmap
    /// channels are zero.
    /// </summary>
    public static void NormalizeRelativeTexturePaths(ScMap map)
    {
        if (map?.TerrainTextures == null) return;

        string? commonDir = null;
        string? commonPrefix = null;
        foreach (var tex in map.TerrainTextures)
        {
            foreach (var path in new[] { tex.AlbedoPath, tex.NormalPath })
            {
                if (string.IsNullOrEmpty(path) || !path.StartsWith("/")) continue;
                int lastSlash = path.LastIndexOf('/');
                if (lastSlash < 0) continue;
                commonDir ??= path.Substring(0, lastSlash + 1);
                if (commonPrefix == null)
                {
                    var basename = path.Substring(lastSlash + 1);
                    int u = basename.IndexOf('_');
                    if (u > 0) commonPrefix = basename.Substring(0, u + 1);
                }
            }
            if (commonDir != null && commonPrefix != null) break;
        }
        if (commonDir == null) return;

        foreach (var tex in map.TerrainTextures)
        {
            tex.AlbedoPath = Fix(tex.AlbedoPath, commonDir, commonPrefix);
            tex.NormalPath = Fix(tex.NormalPath, commonDir, commonPrefix);
        }

        static string Fix(string path, string dir, string? prefix)
        {
            if (string.IsNullOrEmpty(path) || path.StartsWith("/")) return path;
            // Path doesn't have a slash → it's a bare filename. Prepend dir + prefix.
            return dir + (prefix ?? "") + path;
        }
    }

    /// <summary>
    /// Pad the TerrainTextures array up to the SC1 vanilla v53 standard size of 6 slots when a
    /// map ships with fewer (e.g. a blank generated map starts with 1). The original last slot
    /// (the macro overlay) keeps its position as the final slot. New intermediate slots are
    /// empty and their splatmap channels are zeroed so the renderer doesn't bleed magenta.
    ///
    /// We deliberately do NOT expand v53 maps beyond 6 — SC1 vanilla's engine crashes when it
    /// encounters a 10-slot v53 map on load. v56+ maps already ship with 10 slots and pass
    /// through unchanged.
    /// </summary>
    public static void EnsureVanillaSlots(ScMap map)
    {
        if (map?.TerrainTextures == null) return;
        int oldLen = map.TerrainTextures.Length;

        // v53 maps stuck at 10 slots (saved by an earlier broken version of this editor) need to
        // be compacted back to the vanilla 6-slot layout — SC1 crashes loading 10-slot v53s.
        if (map.VersionMinor <= 53 && oldLen > VanillaV53SlotCount)
        {
            // Trust the writeback only when the "extra" slots are unused: slots 5..8 empty and
            // slot 9 carries the macro. That covers everything we've seen produced before the fix.
            bool middleEmpty = true;
            for (int i = 5; i <= 8 && i < oldLen; i++)
                if (!string.IsNullOrEmpty(map.TerrainTextures[i].AlbedoPath)) { middleEmpty = false; break; }
            if (middleEmpty)
            {
                var compacted = new TerrainTexture[VanillaV53SlotCount];
                for (int i = 0; i < 5; i++) compacted[i] = map.TerrainTextures[i];
                compacted[5] = oldLen > 9 ? map.TerrainTextures[9] : new TerrainTexture();
                for (int i = 0; i < VanillaV53SlotCount; i++) compacted[i] ??= new TerrainTexture();
                map.TerrainTextures = compacted;
                oldLen = VanillaV53SlotCount;
            }
        }

        // Pad short v53 maps up to the vanilla 6-slot layout. v56+ maps stay at whatever count
        // they have (typically 10).
        int target = oldLen <= 6 ? VanillaV53SlotCount : oldLen;
        if (oldLen < target)
        {
            int macroSlot = target - 1;
            var expanded = new TerrainTexture[target];
            for (int i = 0; i < oldLen - 1; i++)
                expanded[i] = map.TerrainTextures[i];
            // Old last slot = macro → goes to the new last slot.
            expanded[macroSlot] = oldLen > 0 ? map.TerrainTextures[oldLen - 1] : new TerrainTexture();
            for (int i = 0; i < target; i++)
                expanded[i] ??= new TerrainTexture();
            map.TerrainTextures = expanded;
        }

        // Zero the splatmap channel for every slot 1..8 that has no albedo, so empty slots
        // don't render as magenta where the splatmap happens to be non-zero.
        for (int slot = 1; slot <= 8; slot++)
        {
            if (slot >= map.TerrainTextures.Length) break;
            if (!string.IsNullOrEmpty(map.TerrainTextures[slot].AlbedoPath)) continue;
            ClearMaskChannelsForSlots(map, slot, slot);
        }
    }

    [System.Obsolete("Use EnsureVanillaSlots — expanding v53 maps to 10 slots crashes SC1 on load.")]
    public static void EnsureTenSlots(ScMap map) => EnsureVanillaSlots(map);

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
