namespace SupremeCommanderEditor.Core.Services;

/// <summary>
/// Pure helper that resolves the canonical save folder for a map: always
/// <c>&lt;gamePath&gt;/maps/&lt;sanitized display name&gt;/</c>.
///
/// Lives in Core (not App) so we can unit-test the bug it fixes — the ViewModel used to derive the
/// folder from <c>CurrentMap.Info.Name</c>, which is only refreshed inside SaveMap after the user-
/// facing name was already turned into a folder. Two consequences: the first Save dropped the new
/// files inside the *old* folder (so a second Save was needed to land in the right place), and the
/// original map's scenario.lua was overwritten with the renamed map's name. Switching to the editor's
/// display-name field fixed both.
/// </summary>
public static class MapSavePathResolver
{
    /// <summary>Resolve the canonical save folder, or return an explanatory error.</summary>
    /// <param name="gamePath">Detected Supreme Commander install root. Null/empty → error.</param>
    /// <param name="displayName">Name as shown to the user (without any &lt;LOC ...&gt; prefix).
    /// Sanitised against <see cref="Path.GetInvalidFileNameChars"/>.</param>
    public static (string? folder, string? error) ResolveCanonicalFolder(string? gamePath, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(gamePath))
            return (null, "Supreme Commander install not detected — cannot derive a save folder.");
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat((displayName ?? "").Where(c => Array.IndexOf(invalid, c) < 0)).Trim();
        if (string.IsNullOrEmpty(sanitized))
            return (null, "Map name is empty — set one in the Map Info tab first.");
        return (Path.Combine(gamePath!, "maps", sanitized), null);
    }
}
