using System.Text.RegularExpressions;
using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Services;

/// <summary>
/// Keeps <see cref="ScMap.Info"/>'s <c>Armies</c> list in sync with the ARMY_N spawn markers
/// present in <see cref="ScMap.Markers"/>. The game lobby reads the army list from
/// <c>scenario.lua → Configurations.standard.teams[].armies</c>; if a spawn marker exists with
/// no matching Army entry, the slot is silently dropped from the lobby.
/// </summary>
public static class ArmyReconciler
{
    private static readonly Regex ArmyMarkerName = new(@"^ARMY_(\d+)$", RegexOptions.IgnoreCase);

    /// <summary>
    /// Rewrite <paramref name="map"/>.Info.Armies so it contains exactly one entry per ARMY_N
    /// marker, ordered by index. Existing Army records are preserved (color, faction, no-rush
    /// offsets) when their name matches; missing ones are appended with defaults.
    /// </summary>
    public static void Reconcile(ScMap map)
    {
        var existing = map.Info.Armies.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
        var seen = new SortedDictionary<int, string>();

        foreach (var m in map.Markers)
        {
            var match = ArmyMarkerName.Match(m.Name ?? "");
            if (!match.Success) continue;
            if (!int.TryParse(match.Groups[1].Value, out int idx)) continue;
            seen[idx] = $"ARMY_{idx}";
        }

        if (seen.Count == 0) return; // no spawn markers — leave existing armies alone

        var rebuilt = new List<Army>(seen.Count);
        foreach (var (_, name) in seen)
        {
            rebuilt.Add(existing.TryGetValue(name, out var prior) ? prior : new Army { Name = name });
        }
        map.Info.Armies = rebuilt;
    }
}
