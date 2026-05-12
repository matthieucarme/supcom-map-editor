using Avalonia.Media.Imaging;
using SkiaSharp;

namespace SupremeCommanderEditor.App.Services;

/// <summary>What kind of brush a 3D-palette tool activates on left-click in the 3D viewport.</summary>
public enum Viewport3DToolKind
{
    /// <summary>Heightmap brush (raise/lower/smooth/flatten/plateau). Payload = BrushModeIndex (0-4).</summary>
    TerrainBrush,
    /// <summary>Splatmap brush — paints one of the 8 terrain strata. Payload = strata index (1-8).</summary>
    TextureBrush,
    /// <summary>Opens a popup to edit a single lighting setting. Payload = <see cref="LightingSetting"/>.</summary>
    LightingSetting,
    /// <summary>Opens a popup to edit a water setting (or directly toggles Enable). Payload = <see cref="WaterSetting"/>.</summary>
    WaterSetting,
}

/// <summary>Which lighting property a Lighting tool edits when clicked.</summary>
public enum LightingSetting
{
    SunColor,
    Ambience,
    Multiplier,
    SunDirection,
    Bloom,
    FogStart,
    FogEnd,
}

/// <summary>Which water property a Water tool edits when clicked.</summary>
public enum WaterSetting
{
    Enable,
    Surface,
    Deep,
    Abyss,
}

/// <summary>One sub-menu entry in the 3D bottom palette. Payload type depends on Kind:
///   TerrainBrush → int (BrushModeIndex 0-4)
///   TextureBrush → string (albedo path, e.g. "/env/evergreen2/layers/grass001_albedo.dds")
/// </summary>
public sealed record Viewport3DTool(
    Viewport3DToolKind Kind,
    string Label,
    Bitmap Icon,
    object? Payload);

/// <summary>One top-level category in the 3D bottom palette (e.g. "Terrain", "Textures").</summary>
public sealed record Viewport3DCategory(string Name, IReadOnlyList<Viewport3DTool> Tools);

/// <summary>
/// Static portion of the 3D palette — the Terrain category with the 5 heightmap brush modes. The
/// texture categories are built dynamically per-game-install by <see cref="TextureLibraryService"/>
/// and merged into the displayed list by <see cref="MainWindowViewModel.Viewport3DCategories"/>.
/// </summary>
public static class Viewport3DCatalog
{
    public static Viewport3DCategory TerrainCategory { get; } = BuildTerrain();

    private static Viewport3DCategory BuildTerrain()
    {
        var tools = TerrainBrushCatalog.All
            .Select(e => (Viewport3DTool)new Viewport3DTool(
                Viewport3DToolKind.TerrainBrush, e.Label, e.Icon, (int)e.Mode))
            .ToList();
        return new Viewport3DCategory("Terrain", tools);
    }
}
