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

        // Armies
        sb.AppendLine("    next_army_id = '" + (armies.Count + 1) + "',");
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

        if (marker.Color != null)
            sb.AppendLine($"{indent}    ['color'] = STRING( '{marker.Color}' ),");

        sb.AppendLine($"{indent}    ['type'] = STRING( '{typeStr}' ),");

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
        sb.AppendLine($"{indent}            ['INITIAL'] = GROUP {{");
        sb.AppendLine($"{indent}                orders = '',");
        sb.AppendLine($"{indent}                platoon = '',");
        sb.AppendLine($"{indent}                Units = {{");
        foreach (var unit in army.InitialUnits)
            WriteUnit(sb, unit, indent + "                    ");
        sb.AppendLine($"{indent}                }},");
        sb.AppendLine($"{indent}            }},");
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
