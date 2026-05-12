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
    /// initialised empty.
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
        // Old last slot = macro overlay → goes to slot 9. The splatmap's mask1.* channels for the
        // intermediate slots were not used in the shader's "short v53" branch, so leaving the new
        // intermediate slots empty doesn't change rendering.
        if (oldLen > 0)
            expanded[MacroSlot] = map.TerrainTextures[oldLen - 1];
        else
            expanded[MacroSlot] = new TerrainTexture();
        // Fill the gap between (oldLen-1) and MacroSlot with empty entries.
        for (int i = oldLen - 1; i < MacroSlot; i++)
            if (i >= 0 && expanded[i] == null)
                expanded[i] = new TerrainTexture();
        // Defensive: fill any null left over.
        for (int i = 0; i < TargetSlotCount; i++)
            expanded[i] ??= new TerrainTexture();

        map.TerrainTextures = expanded;
    }
}
