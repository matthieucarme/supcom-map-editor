using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Media.Imaging;
using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Rendering;

/// <summary>Discriminator for palette entries: decides what kind of scene element a click places.</summary>
public enum PaletteEntryKind
{
    /// <summary>Decoration blueprint (env.scd) → adds a Prop to ScMap.Props.</summary>
    Prop,
    /// <summary>SC unit blueprint (units.scd) → adds a UnitSpawn to an Army.InitialUnits list.</summary>
    Unit,
    /// <summary>Marker type → adds a Marker to ScMap.Markers, type given by MarkerKind.</summary>
    Marker,
}

/// <summary>
/// One placeable entry in the bottom 2D-view palette. EntryKind decides routing at placement:
/// Prop → ScMap.Props, Unit → Army.InitialUnits, Marker → ScMap.Markers. For markers the
/// concrete <see cref="MarkerKind"/> is also set.
/// </summary>
public sealed record PropEntry(
    string BlueprintPath,  // empty for markers (they don't have a blueprint path)
    string Biome,          // for props: biome dir; for units: faction id; for markers: a sort hint
    string Kind,           // top-level menu category name
    string DisplayName,
    Bitmap Icon,
    PaletteEntryKind EntryKind = PaletteEntryKind.Prop,
    MarkerType? MarkerKind = null)
{
    /// <summary>Convenience accessor preserved for older call sites (prefer EntryKind).</summary>
    public bool IsUnit => EntryKind == PaletteEntryKind.Unit;
    public bool IsMarker => EntryKind == PaletteEntryKind.Marker;
}

/// <summary>A top-level group displayed as the first row of the prop menu.</summary>
public sealed record PropCategory(string Name, IReadOnlyList<PropEntry> Items);

/// <summary>
/// Enumerates every prop icon embedded in this assembly and groups them into broad kinds
/// (Rocks / Trees / Bushes / Wreckage / Misc). The blueprint path is reconstructed from the
/// embedded resource name (PropIcons/{biome}_{basename}.png), which is enough to write the prop
/// into a .scmap (the engine resolves the path against env.scd).
/// </summary>
public static class PropCatalog
{
    private const string PropResourcePrefix = "PropIcons/";
    private const string UnitResourcePrefix = "UnitIcons/";

    /// <summary>Lazily built once; subsequent reads are cached.</summary>
    public static IReadOnlyList<PropCategory> All { get; } = Build();

    private static IReadOnlyList<PropCategory> Build()
    {
        var asm = typeof(PropCatalog).Assembly;
        var entries = new List<PropEntry>();

        // --- Decoration props (env.scd icons) ---
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(PropResourcePrefix)) continue;
            var slug = name.Substring(PropResourcePrefix.Length, name.Length - PropResourcePrefix.Length - ".png".Length);
            int us = slug.IndexOf('_');
            if (us <= 0 || us == slug.Length - 1) continue;
            var biome = slug.Substring(0, us);
            var basename = slug.Substring(us + 1);
            var icon = LoadBitmap(asm, name);
            if (icon == null) continue;
            entries.Add(new PropEntry(
                BlueprintPathFor(biome, basename),
                biome,
                GuessKind(basename),
                PrettyName(basename),
                icon,
                EntryKind: PaletteEntryKind.Prop));
        }

        // --- Units (units.scd icons) ---
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(UnitResourcePrefix)) continue;
            var unitId = name.Substring(UnitResourcePrefix.Length, name.Length - UnitResourcePrefix.Length - ".png".Length);
            var icon = LoadBitmap(asm, name);
            if (icon == null) continue;
            var (faction, kind) = ClassifyUnit(unitId);
            entries.Add(new PropEntry(
                $"/units/{unitId.ToUpperInvariant()}/{unitId.ToUpperInvariant()}_unit.bp",
                faction,
                kind,
                unitId.ToUpperInvariant(),
                icon,
                EntryKind: PaletteEntryKind.Unit));
        }

        // Preferred order: decoration first (most common usage), then units grouped by faction.
        var preferredOrder = new[]
        {
            "Rocks", "Trees", "Bushes", "Logs", "Wreckage", "Geothermal", "Misc",
            "UEF", "Cybran", "Aeon", "Seraphim", "Civilians", "Other Units",
        };
        return preferredOrder
            .Select(k => new PropCategory(k, entries.Where(e => e.Kind == k).OrderBy(e => e.Biome).ThenBy(e => e.DisplayName).ToList()))
            .Where(c => c.Items.Count > 0)
            .ToList();
    }

    /// <summary>
    /// SC unit IDs follow the pattern `[XO][type][build][digits]`:
    ///  - 1st letter: U = original, X = expansion (FA), D = experimental wreck/special, O = mission
    ///  - 2nd letter: faction. E=UEF, R=Cybran, A=Aeon, S=Seraphim (FA), CIV variants for civilians.
    ///  - 3rd letter: type. B=Building (structure), L=Land, A=Air, S=Sea.
    /// </summary>
    private static (string Faction, string Kind) ClassifyUnit(string id)
    {
        if (id.Length < 3) return ("uef", "Other Units");
        char factionLetter = char.ToUpperInvariant(id[1]);
        var (faction, kind) = factionLetter switch
        {
            'E' => ("uef", "UEF"),
            'R' => ("cybran", "Cybran"),
            'A' => ("aeon", "Aeon"),
            'S' => ("seraphim", "Seraphim"),
            _ => ("civilian", "Civilians"),
        };
        // Treat OPC* (operational/campaign actors) as civilians too.
        if (id.StartsWith("opc", StringComparison.OrdinalIgnoreCase)) return ("civilian", "Civilians");
        // Dxxxxx are typically wreck/special — bucket as "Other Units".
        if (id.StartsWith("d", StringComparison.OrdinalIgnoreCase)) return ("other", "Other Units");
        return (faction, kind);
    }

    private static Bitmap? LoadBitmap(Assembly asm, string resName)
    {
        using var s = asm.GetManifestResourceStream(resName);
        if (s == null) return null;
        return new Bitmap(s);
    }

    /// <summary>
    /// Reconstruct the engine path "/env/&lt;biome&gt;/props/&lt;subdir&gt;/&lt;basename&gt;_prop.bp".
    /// Since we don't keep the subdir in the icon name, we hand the engine the most common layouts
    /// (rocks, trees, bush, logs, wreckage) — the user's .scmap stores the path verbatim and the
    /// engine resolves it case-insensitively. The kind heuristic also drives this guess.
    /// </summary>
    private static string BlueprintPathFor(string biome, string basename)
    {
        // For Wreckage, biome IS the wreckage dir and the subdir is /props/Generic /props/Aeon /props/Walls.
        // We cheat: for wreckage we try "/env/wreckage/props/generic/" first since 8/10 props live there.
        if (string.Equals(biome, "wreckage", System.StringComparison.OrdinalIgnoreCase))
        {
            if (basename.Contains("ual", System.StringComparison.OrdinalIgnoreCase)) return $"/env/wreckage/props/aeon/{basename}_prop.bp";
            if (basename.Contains("ueb", System.StringComparison.OrdinalIgnoreCase)) return $"/env/wreckage/props/walls/{basename}_prop.bp";
            return $"/env/wreckage/props/generic/{basename}_prop.bp";
        }
        // For tundra, some props are directly under env/tundra/props/ (icerock, iceberg, icerocksm).
        // Both `env/tundra/props/X_prop.bp` and `env/tundra/props/rocks/X_prop.bp` exist depending
        // on the asset. Pick the right one based on basename markers.
        if (string.Equals(biome, "tundra", System.StringComparison.OrdinalIgnoreCase) &&
            (basename.StartsWith("iceberg", System.StringComparison.OrdinalIgnoreCase) ||
             basename.StartsWith("icerock", System.StringComparison.OrdinalIgnoreCase)))
            return $"/env/tundra/props/{basename}_prop.bp";
        // For redrocks, plain rocks/boulders/pods live directly under /env/redrocks/props/.
        if (string.Equals(biome, "redrocks", System.StringComparison.OrdinalIgnoreCase) &&
            (basename.StartsWith("rock", System.StringComparison.OrdinalIgnoreCase) ||
             basename.StartsWith("boulder", System.StringComparison.OrdinalIgnoreCase) ||
             basename.StartsWith("pod", System.StringComparison.OrdinalIgnoreCase) ||
             basename.StartsWith("searock", System.StringComparison.OrdinalIgnoreCase) ||
             basename.StartsWith("thetabridge", System.StringComparison.OrdinalIgnoreCase)))
            return $"/env/redrocks/props/{basename}_prop.bp";
        // Common: env/common/props/ or env/common/props/markers/
        if (string.Equals(biome, "common", System.StringComparison.OrdinalIgnoreCase))
            return $"/env/common/props/{basename}_prop.bp";
        // devtest props
        if (string.Equals(biome, "devtest", System.StringComparison.OrdinalIgnoreCase))
            return $"/env/devtest/props/{basename}_prop.bp";
        // Default: pick subdir from kind heuristic.
        var sub = GuessKind(basename) switch
        {
            "Rocks" => "rocks",
            "Trees" => basename.Contains("group", System.StringComparison.OrdinalIgnoreCase) ? "trees/groups" : "trees",
            "Bushes" => "bush",
            "Logs" => "logs",
            "Geothermal" => basename.Contains("geyser", System.StringComparison.OrdinalIgnoreCase) ? "geysers" : "mudpots",
            _ => "rocks",
        };
        return $"/env/{biome}/props/{sub}/{basename}_prop.bp";
    }

    /// <summary>Cheap heuristic by token. Vanilla SC asset names are very regular.</summary>
    private static string GuessKind(string basename)
    {
        var b = basename.ToLowerInvariant();
        if (b.Contains("wreckage") || b.StartsWith("ual") || b.StartsWith("ueb")) return "Wreckage";
        if (b.Contains("bush") || b.Contains("fern") || b.Contains("flower")) return "Bushes";
        if (b.StartsWith("log")) return "Logs";
        if (b.Contains("geyser") || b.Contains("mudpot")) return "Geothermal";
        if (b.Contains("rock") || b.Contains("boulder") || b.Contains("stone") ||
            b.Contains("berg") || b.Contains("crystal") || b.Contains("pile") ||
            b.Contains("outcrop") || b.Contains("pod") || b.Contains("thetabridge"))
            return "Rocks";
        if (b.Contains("pine") || b.Contains("oak") || b.Contains("palm") ||
            b.Contains("ficus") || b.Contains("cactus") || b.Contains("manzanita") ||
            b.Contains("cypress") || b.Contains("redtree") || b.Contains("carotenoid") ||
            b.Contains("dead") || b.StartsWith("brch") || b.StartsWith("dc") ||
            b.StartsWith("tund_pine") || b.Contains("tree"))
            return "Trees";
        return "Misc";
    }

    private static string PrettyName(string basename)
    {
        // Trim common biome/type prefixes: tund_, eg_, des_, sw_, lav_.
        var b = basename;
        foreach (var prefix in new[] { "tund_", "eg_", "des_", "sw_", "lav_" })
            if (b.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) { b = b.Substring(prefix.Length); break; }
        // Replace _ with space, capitalise.
        var parts = b.Split('_').Where(p => !string.IsNullOrEmpty(p)).Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1));
        return string.Join(' ', parts);
    }
}
