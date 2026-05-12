using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using SkiaSharp;
using SupremeCommanderEditor.Core.Services;

namespace SupremeCommanderEditor.App.Services;

/// <summary>One scanned game texture: its on-disk path inside the .scd, the matching normal map
/// (if it exists), a clean display name, and a pre-rendered 96×96 thumbnail.</summary>
public sealed record TextureEntry(
    string Biome,
    string Name,
    string AlbedoPath,
    string? NormalPath,
    Bitmap Thumbnail);

/// <summary>
/// Builds an in-memory catalog of every terrain-layer albedo found inside the game's .scd archives,
/// with a small thumbnail per texture. Used to populate the 3D bottom palette's per-biome
/// categories ("Tundra", "Evergreen", "Redrocks", …) with browsable real textures.
/// </summary>
public class TextureLibraryService
{
    private const int ThumbnailSize = 96;

    /// <summary>All scanned textures across every biome. Empty if game data isn't initialized.</summary>
    public IReadOnlyList<TextureEntry> Entries { get; }

    public TextureLibraryService(GameDataService gameData)
    {
        Entries = gameData.IsInitialized ? Build(gameData) : [];
    }

    private static IReadOnlyList<TextureEntry> Build(GameDataService gameData)
    {
        var entries = new List<TextureEntry>();
        // FindFiles filters against the raw `entry.FullName` BEFORE the backslash→forward-slash
        // normalization, so passing "/env/" or "/layers/" would miss archives stored with Windows
        // separators. Use the unique suffix "_albedo.dds" as the only FindFiles filter, then
        // re-check the segments on the yielded (already normalized) path.
        foreach (var rawPath in gameData.FindFiles("_albedo.dds"))
        {
            var normalized = rawPath.Replace('\\', '/').ToLowerInvariant();
            if (!normalized.Contains("/env/")) continue;
            if (!normalized.Contains("/layers/")) continue;
            var parsed = ParseEntry(rawPath, gameData);
            if (parsed != null) entries.Add(parsed);
        }
        DebugLog.Write($"[TextureLibrary] Scanned {entries.Count} terrain textures from game data.");
        return entries;
    }

    private static TextureEntry? ParseEntry(string albedoPath, GameDataService gameData)
    {
        // Normalize to forward slashes first — archive entries from Windows-stored ZIPs may use '\'.
        var normalized = albedoPath.Replace('\\', '/');
        // /env/evergreen2/layers/grass001_albedo.dds → biome = "evergreen2", base = "grass001"
        var parts = normalized.TrimStart('/').Split('/');
        if (parts.Length < 4) return null;
        if (!string.Equals(parts[0], "env", StringComparison.OrdinalIgnoreCase)) return null;

        var biome = parts[1].ToLowerInvariant();
        var fileName = parts[^1];
        const string suffix = "_albedo.dds";
        if (!fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;
        var baseName = fileName.Substring(0, fileName.Length - suffix.Length);

        // Sibling normal map (may or may not exist). Path format same dir + _normal.dds.
        var dir = normalized.Substring(0, normalized.LastIndexOf('/') + 1);
        var normalPath = dir + baseName + "_normal.dds";
        string? normalIfFound = gameData.LoadFile(normalPath) != null ? normalPath : null;

        // Generate thumbnail; if the texture fails to decode (rare DXT variant or corrupt entry),
        // skip silently — the user just won't see that texture in the palette.
        var dds = gameData.LoadTextureDds(normalized);
        if (dds == null) return null;
        var thumb = MakeThumbnail(dds.Value);

        return new TextureEntry(biome, baseName, normalized, normalIfFound, thumb);
    }

    /// <summary>Resize the loaded RGBA pixels to a 96×96 Avalonia bitmap.</summary>
    private static Bitmap MakeThumbnail((byte[] Pixels, int Width, int Height) src)
    {
        using var full = new SKBitmap(new SKImageInfo(src.Width, src.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        Marshal.Copy(src.Pixels, 0, full.GetPixels(), src.Pixels.Length);

        // SKBitmap.Resize can return null on failure; fall back to drawing into a canvas.
        using var resized = full.Resize(new SKImageInfo(ThumbnailSize, ThumbnailSize, SKColorType.Rgba8888, SKAlphaType.Premul), SKFilterQuality.High)
                          ?? RenderViaCanvas(full);
        using var data = resized.Encode(SKEncodedImageFormat.Png, 90);
        using var ms = new MemoryStream(data.ToArray());
        return new Bitmap(ms);
    }

    private static SKBitmap RenderViaCanvas(SKBitmap src)
    {
        var bmp = new SKBitmap(ThumbnailSize, ThumbnailSize, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
        canvas.DrawBitmap(src, new SKRect(0, 0, ThumbnailSize, ThumbnailSize), paint);
        return bmp;
    }

    /// <summary>Title-case a biome key for the palette category name. "evergreen2" → "Evergreen 2".</summary>
    public static string PrettyBiome(string biomeKey)
    {
        if (string.IsNullOrEmpty(biomeKey)) return biomeKey;
        // Insert a space before trailing digits so "evergreen2" becomes "evergreen 2", then Title-case.
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < biomeKey.Length; i++)
        {
            if (i > 0 && char.IsDigit(biomeKey[i]) && !char.IsDigit(biomeKey[i - 1])) sb.Append(' ');
            sb.Append(biomeKey[i]);
        }
        var spaced = sb.ToString();
        return char.ToUpperInvariant(spaced[0]) + spaced.Substring(1);
    }
}
