using System.Globalization;
using System.Text;
using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Formats.Lua;

public static class SaveLuaWriter
{
    public static void Write(string filePath, List<Marker> markers, List<Army> armies)
    {
        File.WriteAllText(filePath, Generate(markers, armies));
    }

    public static string Generate(List<Marker> markers, List<Army> armies)
    {
        var sb = new StringBuilder();

        sb.AppendLine("--[[                                                                           ]]--");
        sb.AppendLine("--[[  Automatically generated code (do not edit)                               ]]--");
        sb.AppendLine("--[[                                                                           ]]--");
        sb.AppendLine("Scenario = {");
        sb.AppendLine("    next_area_id = '1',");

        // Props (empty)
        sb.AppendLine("    Props = {");
        sb.AppendLine("    },");

        // Areas (empty)
        sb.AppendLine("    Areas = {");
        sb.AppendLine("    },");

        // Markers
        sb.AppendLine("    MasterChain = {");
        sb.AppendLine("        ['_MASTERCHAIN_'] = {");
        sb.AppendLine("            Markers = {");

        foreach (var marker in markers)
        {
            WriteMarker(sb, marker, "                ");
        }

        sb.AppendLine("            },");
        sb.AppendLine("        },");
        sb.AppendLine("    },");

        // Chains (empty)
        sb.AppendLine("    Chains = {");
        sb.AppendLine("    },");

        // Orders / Platoons section — vanilla SC1 expects these even when empty. Skipping them
        // makes the engine crash on map load while resolving Scenario.Orders / Scenario.Platoons.
        sb.AppendLine("    next_queue_id = '1',");
        sb.AppendLine("    Orders = {");
        sb.AppendLine("    },");
        sb.AppendLine("    next_platoon_id = '1',");
        sb.AppendLine("    Platoons = {");
        sb.AppendLine("    },");

        // Armies — plus the `next_group_id` / `next_unit_id` counters vanilla maps include
        // alongside next_army_id (used by the in-game editor when adding new entities).
        sb.AppendLine("    next_army_id = '" + (armies.Count + 1) + "',");
        sb.AppendLine("    next_group_id = '1',");
        sb.AppendLine("    next_unit_id = '1',");
        sb.AppendLine("    Armies = {");

        foreach (var army in armies)
        {
            WriteArmy(sb, army, "        ");
        }

        sb.AppendLine("    },");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void WriteMarker(StringBuilder sb, Marker marker, string indent)
    {
        sb.AppendLine($"{indent}['{marker.Name}'] = {{");

        var typeStr = MarkerTypeToString(marker.Type);

        if (marker.Type == MarkerType.Mass || marker.Type == MarkerType.Hydrocarbon)
        {
            sb.AppendLine(FormattableString.Invariant(
                $"{indent}    ['size'] = FLOAT( 1.000000 ),"));
            sb.AppendLine(FormattableString.Invariant(
                $"{indent}    ['resource'] = BOOLEAN( true ),"));
            sb.AppendLine(FormattableString.Invariant(
                $"{indent}    ['amount'] = FLOAT( {marker.Amount:F6} ),"));
        }

        // `hint = BOOLEAN(true)` marks the marker for the AI's strategic decision logic. Vanilla
        // maps put it on every Combat Zone / Defensive Point / Rally Point / Naval Area /
        // Expansion Area / Protected Experimental Construction marker. Missing it makes the AI
        // module crash on map load. Auto-default per type if the marker wasn't loaded with one.
        bool shouldHint = marker.Hint == "true" || DefaultHintFor(marker.Type);
        if (shouldHint)
            sb.AppendLine($"{indent}    ['hint'] = BOOLEAN( true ),");

        if (marker.Color != null)
            sb.AppendLine($"{indent}    ['color'] = STRING( '{marker.Color}' ),");

        sb.AppendLine($"{indent}    ['type'] = STRING( '{typeStr}' ),");

        // SC1 spawns the visible 3D model for the marker from this blueprint. Without it the
        // engine crashes when it tries to instantiate the marker entity. Prefer the value read
        // from the original save.lua; fall back to the standard vanilla prop for the type.
        var prop = !string.IsNullOrEmpty(marker.Prop) ? marker.Prop : DefaultPropFor(marker.Type);
        if (!string.IsNullOrEmpty(prop))
            sb.AppendLine($"{indent}    ['prop'] = STRING( '{prop}' ),");

        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"{indent}    ['orientation'] = VECTOR3( {marker.Orientation.X:G}, {marker.Orientation.Y:G}, {marker.Orientation.Z:G} ),"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"{indent}    ['position'] = VECTOR3( {marker.Position.X:G}, {marker.Position.Y:G}, {marker.Position.Z:G} ),"));

        if (marker.AdjacentMarkers.Count > 0)
        {
            sb.AppendLine($"{indent}    ['adjacentTo'] = STRING( '{string.Join(" ", marker.AdjacentMarkers)}' ),");
        }

        sb.AppendLine($"{indent}}},");
    }

    private static void WriteArmy(StringBuilder sb, Army army, string indent)
    {
        sb.AppendLine($"{indent}['{army.Name}'] = {{");
        sb.AppendLine($"{indent}    personality = '',");
        sb.AppendLine($"{indent}    plans = '',");
        sb.AppendLine($"{indent}    color = {army.Color},");
        sb.AppendLine($"{indent}    faction = {army.Faction},");
        sb.AppendLine($"{indent}    Economy = {{");
        sb.AppendLine($"{indent}        mass = 0,");
        sb.AppendLine($"{indent}        energy = 0,");
        sb.AppendLine($"{indent}    }},");
        sb.AppendLine($"{indent}    Alliances = {{");
        sb.AppendLine($"{indent}    }},");
        sb.AppendLine($"{indent}    ['Units'] = GROUP {{");
        sb.AppendLine($"{indent}        orders = '',");
        sb.AppendLine($"{indent}        platoon = '',");
        sb.AppendLine($"{indent}        Units = {{");

        // Emit one GROUP block per distinct Category. SC1 treats the label as a load instruction
        // ('INITIAL' → live unit, 'WRECKAGE' → pre-destroyed husk), so silently merging everything
        // into INITIAL would turn vanilla carcasses into functional units on save. We preserve
        // whatever the reader saw. Empty armies still emit an empty INITIAL block so the engine's
        // schema check is satisfied.
        var byCategory = army.InitialUnits
            .GroupBy(u => string.IsNullOrEmpty(u.Category) ? "INITIAL" : u.Category)
            .ToList();
        if (byCategory.Count == 0)
        {
            sb.AppendLine($"{indent}            ['INITIAL'] = GROUP {{");
            sb.AppendLine($"{indent}                orders = '',");
            sb.AppendLine($"{indent}                platoon = '',");
            sb.AppendLine($"{indent}                Units = {{");
            sb.AppendLine($"{indent}                }},");
            sb.AppendLine($"{indent}            }},");
        }
        else
        {
            foreach (var group in byCategory)
            {
                sb.AppendLine($"{indent}            ['{group.Key}'] = GROUP {{");
                sb.AppendLine($"{indent}                orders = '',");
                sb.AppendLine($"{indent}                platoon = '',");
                sb.AppendLine($"{indent}                Units = {{");
                foreach (var unit in group)
                    WriteUnit(sb, unit, indent + "                    ");
                sb.AppendLine($"{indent}                }},");
                sb.AppendLine($"{indent}            }},");
            }
        }
        sb.AppendLine($"{indent}        }},");
        sb.AppendLine($"{indent}    }},");
        sb.AppendLine($"{indent}    PlatoonBuilders = {{");
        sb.AppendLine($"{indent}        next_platoon_builder_id = '0',");
        sb.AppendLine($"{indent}        Builders = {{");
        sb.AppendLine($"{indent}        }},");
        sb.AppendLine($"{indent}    }},");
        sb.AppendLine($"{indent}}},");
    }

    private static void WriteUnit(StringBuilder sb, UnitSpawn u, string indent)
    {
        sb.AppendLine($"{indent}['{u.Name}'] = {{");
        sb.AppendLine($"{indent}    type = '{u.BlueprintId}',");
        sb.AppendLine($"{indent}    orders = '{u.Orders ?? ""}',");
        sb.AppendLine($"{indent}    platoon = '{u.Platoon ?? ""}',");
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"{indent}    Position = VECTOR3( {u.Position.X:G}, {u.Position.Y:G}, {u.Position.Z:G} ),"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"{indent}    Orientation = VECTOR3( {u.Orientation.X:G}, {u.Orientation.Y:G}, {u.Orientation.Z:G} ),"));
        sb.AppendLine($"{indent}}},");
    }

    /// <summary>Strategic marker types vanilla always tags with <c>hint = BOOLEAN(true)</c>.
    /// SC1's AI module crashes during map load if these strategy markers don't carry the flag.</summary>
    private static bool DefaultHintFor(MarkerType type) => type switch
    {
        MarkerType.CombatZone or
        MarkerType.DefensePoint or
        MarkerType.RallyPoint or
        MarkerType.NavalRallyPoint or
        MarkerType.NavalArea or
        MarkerType.ExpansionArea or
        MarkerType.LargeExpansionArea or
        MarkerType.ProtectedExperimentalConstruction => true,
        _ => false,
    };

    /// <summary>Default prop blueprint path used by vanilla SC1 for the well-known marker types.
    /// Mirrors what GPG's official maps (SCMP_001..040) ship with. Path nodes / camera / weather /
    /// effect markers don't have a prop in vanilla — returning null skips the field.</summary>
    private static string? DefaultPropFor(MarkerType type) => type switch
    {
        MarkerType.Mass        => "/env/common/props/markers/M_Mass_prop.bp",
        MarkerType.Hydrocarbon => "/env/common/props/markers/M_Hydrocarbon_prop.bp",
        MarkerType.BlankMarker => "/env/common/props/markers/M_Blank_prop.bp",
        MarkerType.CombatZone  => "/env/common/props/markers/M_CombatZone_prop.bp",
        MarkerType.DefensePoint        or
        MarkerType.RallyPoint          or
        MarkerType.NavalRallyPoint     => "/env/common/props/markers/M_Defensive_prop.bp",
        MarkerType.ExpansionArea       or
        MarkerType.LargeExpansionArea  or
        MarkerType.NavalArea           or
        MarkerType.ProtectedExperimentalConstruction => "/env/common/props/markers/M_Expansion_prop.bp",
        _ => null,
    };

    public static string MarkerTypeToString(MarkerType type) => type switch
    {
        MarkerType.Mass => "Mass",
        MarkerType.Hydrocarbon => "Hydrocarbon",
        MarkerType.BlankMarker => "Blank Marker",
        MarkerType.LandPathNode => "Land Path Node",
        MarkerType.AirPathNode => "Air Path Node",
        MarkerType.WaterPathNode => "Water Path Node",
        MarkerType.AmphibiousPathNode => "Amphibious Path Node",
        MarkerType.RallyPoint => "Rally Point",
        MarkerType.NavalRallyPoint => "Naval Rally Point",
        MarkerType.ExpansionArea => "Expansion Area",
        MarkerType.LargeExpansionArea => "Large Expansion Area",
        MarkerType.NavalArea => "Naval Area",
        MarkerType.CombatZone => "Combat Zone",
        MarkerType.DefensePoint => "Defensive Point",
        MarkerType.ProtectedExperimentalConstruction => "Protected Experimental Construction",
        MarkerType.CameraInfo => "Camera Info",
        MarkerType.WeatherGenerator => "Weather Generator",
        MarkerType.WeatherDefinition => "Weather Definition",
        MarkerType.Effect => "Effect",
        _ => "Blank Marker"
    };
}
