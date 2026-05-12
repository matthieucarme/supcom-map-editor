using System.Reflection;

namespace SupremeCommanderEditor.Rendering;

/// <summary>
/// Loads pre-rendered prop icon PNGs embedded in this assembly. Icons are produced offline by
/// tools/IconGenerator from the vanilla `env.scd` (SC mesh + albedo → 96x96 PNG) and shipped
/// inside the Rendering DLL — no game-data dependency at runtime.
///
/// Key naming: `{biome}_{basename}` derived from the blueprint path. Example:
///   blueprint  "/env/evergreen/props/rocks/Rock01_prop.bp"
///   biome      = "evergreen" (first segment after /env/)
///   basename   = "rock01"    (lowercase, _prop suffix stripped)
///   resource   = "PropIcons/evergreen_rock01.png"
/// </summary>
public static class PropIconService
{
    private static readonly Assembly Asm = typeof(PropIconService).Assembly;
    private static readonly HashSet<string> AvailableResources = LoadResourceIndex();

    private static HashSet<string> LoadResourceIndex()
    {
        // Manifest resource names are case-sensitive; we keep them as-is and key the lookup directly.
        return new HashSet<string>(Asm.GetManifestResourceNames(), StringComparer.Ordinal);
    }

    /// <summary>Build the canonical resource name for a blueprint path.</summary>
    public static string? IconResourceName(string blueprintPath)
    {
        if (string.IsNullOrEmpty(blueprintPath)) return null;
        var parts = blueprintPath.Trim('/').Split('/');
        // Expected layout: env/<biome>/.../<name>_prop.bp
        if (parts.Length < 3 || !string.Equals(parts[0], "env", StringComparison.OrdinalIgnoreCase)) return null;
        var biome = parts[1].ToLowerInvariant();
        var fileName = parts[^1]; // last segment
        // Strip "_prop.bp" suffix (case-insensitive).
        const string suffix = "_prop.bp";
        if (!fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;
        var baseName = fileName.Substring(0, fileName.Length - suffix.Length).ToLowerInvariant();
        return $"PropIcons/{biome}_{baseName}.png";
    }

    /// <summary>Returns the icon bytes for a blueprint, or null if no icon was generated for it.</summary>
    public static byte[]? LoadIconBytes(string blueprintPath)
    {
        var name = IconResourceName(blueprintPath);
        if (name == null || !AvailableResources.Contains(name)) return null;
        using var s = Asm.GetManifestResourceStream(name);
        if (s == null) return null;
        var buf = new byte[s.Length];
        int read = 0;
        while (read < buf.Length)
        {
            int n = s.Read(buf, read, buf.Length - read);
            if (n <= 0) break;
            read += n;
        }
        return buf;
    }

    /// <summary>Returns true when a pre-rendered icon exists for this blueprint path.</summary>
    public static bool HasIcon(string blueprintPath)
    {
        var name = IconResourceName(blueprintPath);
        return name != null && AvailableResources.Contains(name);
    }

    /// <summary>Returns the icon bytes for a SC unit by its blueprint ID (e.g. "UEB1101"), or null
    /// if no icon was rendered for it.</summary>
    public static byte[]? LoadUnitIconBytes(string unitId)
    {
        if (string.IsNullOrEmpty(unitId)) return null;
        var name = $"UnitIcons/{unitId.ToLowerInvariant()}.png";
        if (!AvailableResources.Contains(name)) return null;
        using var s = Asm.GetManifestResourceStream(name);
        if (s == null) return null;
        var buf = new byte[s.Length];
        int read = 0;
        while (read < buf.Length)
        {
            int n = s.Read(buf, read, buf.Length - read);
            if (n <= 0) break;
            read += n;
        }
        return buf;
    }
}
