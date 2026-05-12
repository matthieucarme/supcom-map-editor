using System.Globalization;
using System.Text;
using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Formats.Lua;

public static class ScenarioLuaWriter
{
    public static void Write(string filePath, MapInfo info, string mapFolderName, string mapBaseName)
    {
        File.WriteAllText(filePath, Generate(info, mapFolderName, mapBaseName));
    }

    public static string Generate(MapInfo info, string mapFolderName, string mapBaseName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("version = 3");
        sb.AppendLine("ScenarioInfo = {");
        sb.AppendLine($"    name = '{EscapeLua(info.Name)}',");
        sb.AppendLine($"    description = '{EscapeLua(info.Description)}',");
        sb.AppendLine($"    type = '{info.Type}',");
        sb.AppendLine($"    starts = true,");
        sb.AppendLine($"    preview = '',");
        sb.AppendLine($"    size = {{{info.Width}, {info.Height}}},");
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"    norushradius = {info.NoRushRadius:G},"));

        foreach (var army in info.Armies)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"    norushoffsetX_{army.Name} = {army.NoRushOffsetX:G},"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"    norushoffsetY_{army.Name} = {army.NoRushOffsetY:G},"));
        }

        sb.AppendLine($"    map = '/maps/{mapFolderName}/{mapBaseName}.scmap',");
        sb.AppendLine($"    save = '/maps/{mapFolderName}/{mapBaseName}_save.lua',");
        sb.AppendLine($"    script = '/maps/{mapFolderName}/{mapBaseName}_script.lua',");
        // NOTE: `map_version` is a FAF extension (used by Forged Alliance Forever to track map
        // revisions in the lobby). Vanilla SC1 doesn't recognise it: the lobby appends the value
        // in parentheses next to the map name AND selecting the entry crashes the game. Skip it
        // entirely so saved maps stay compatible with vanilla SC1.
        sb.AppendLine("    Configurations = {");
        sb.AppendLine("        ['standard'] = {");
        sb.AppendLine("            teams = {");
        sb.Append("                { name = 'FFA', armies = {");
        foreach (var army in info.Armies)
        {
            sb.Append($"'{army.Name}',");
        }
        sb.AppendLine("} },");
        sb.AppendLine("            },");
        sb.AppendLine("            customprops = {");
        // Preserve every customprops entry we read on load. Without this we'd strip
        // ['ExtraArmies'] = STRING('ARMY_9 NEUTRAL_CIVILIAN') and SC1 would crash on map load
        // because save.lua references those armies but they're no longer declared in scenario.
        foreach (var kv in info.CustomProps)
        {
            sb.AppendLine($"                ['{EscapeLua(kv.Key)}'] = STRING( '{EscapeLua(kv.Value)}' ),");
        }
        sb.AppendLine("            },");
        sb.AppendLine("        },");
        sb.AppendLine("    },");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string EscapeLua(string s)
    {
        return s.Replace("\\", "\\\\").Replace("'", "\\'");
    }
}
