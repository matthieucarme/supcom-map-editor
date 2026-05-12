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
        sb.AppendLine($"    map_version = {info.MapVersion},");
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
