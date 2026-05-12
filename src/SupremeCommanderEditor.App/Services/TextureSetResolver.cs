using SupremeCommanderEditor.Core.Operations;

namespace SupremeCommanderEditor.App.Services;

/// <summary>
/// Picks one library texture per smart category for a given biome. Used by the procedural
/// generator dialog (to preview the set) and the generator itself (to populate strata).
///
/// Strict biome matching: looks for "/&lt;biome&gt;/" in the path so "evergreen" doesn't accidentally
/// pick up "evergreen2" textures. If a category isn't covered by the biome, falls back to any
/// biome that does cover it — better than leaving the strata empty.
/// </summary>
public static class TextureSetResolver
{
    public static Dictionary<SmartBrushTool.TerrainCategory, TextureEntry?> Resolve(
        TextureLibraryService library, string biomeKey)
    {
        var result = new Dictionary<SmartBrushTool.TerrainCategory, TextureEntry?>();
        string fragment = "/" + biomeKey.ToLowerInvariant() + "/";
        foreach (var cat in Enum.GetValues<SmartBrushTool.TerrainCategory>())
        {
            // 1. Strict biome match — texture path contains "/<biomeKey>/" AND classifies to cat.
            var match = library.Entries.FirstOrDefault(e =>
                e.AlbedoPath.Contains(fragment, StringComparison.OrdinalIgnoreCase)
                && TextureCategoryTable.Classify(e.AlbedoPath) == cat);
            // 2. Fallback — any biome that has a texture for this category.
            match ??= library.Entries.FirstOrDefault(e =>
                TextureCategoryTable.Classify(e.AlbedoPath) == cat);
            result[cat] = match;
        }
        return result;
    }
}
