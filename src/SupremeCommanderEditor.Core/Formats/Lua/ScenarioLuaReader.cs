using MoonSharp.Interpreter;
using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Formats.Lua;

public static class ScenarioLuaReader
{
    public static MapInfo Read(string filePath)
    {
        var script = LuaRuntime.CreateScript();
        script.DoFile(filePath);

        var scenarioInfo = script.Globals.Get("ScenarioInfo");
        if (scenarioInfo.Type != DataType.Table)
            throw new InvalidDataException("ScenarioInfo table not found in scenario file");

        return ParseScenarioInfo(scenarioInfo.Table);
    }

    private static MapInfo ParseScenarioInfo(Table t)
    {
        var info = new MapInfo
        {
            Name = LuaRuntime.GetString(t, "name", "Untitled"),
            Description = LuaRuntime.GetString(t, "description"),
            Type = LuaRuntime.GetString(t, "type", "skirmish"),
            Starts = LuaRuntime.GetBool(t, "starts", true),
            Preview = LuaRuntime.GetString(t, "preview"),
            NoRushRadius = (float)LuaRuntime.GetNumber(t, "norushradius", 70),
        };

        // Map size: {width, height}
        var sizeTable = LuaRuntime.GetTable(t, "size");
        if (sizeTable != null)
        {
            info.Width = (int)sizeTable.Get(1).Number;
            info.Height = (int)sizeTable.Get(2).Number;
        }

        // Map version from scenario
        info.MapVersion = (int)LuaRuntime.GetNumber(t, "map_version", 1);

        // Extract armies from Configurations
        var configs = LuaRuntime.GetTable(t, "Configurations");
        var standard = configs != null ? LuaRuntime.GetTable(configs, "standard") : null;
        var teams = standard != null ? LuaRuntime.GetTable(standard, "teams") : null;

        if (teams != null)
        {
            foreach (var teamEntry in teams.Values)
            {
                if (teamEntry.Type != DataType.Table) continue;
                var armies = LuaRuntime.GetTable(teamEntry.Table, "armies");
                if (armies == null) continue;

                foreach (var armyVal in armies.Values)
                {
                    if (armyVal.Type != DataType.String) continue;
                    var armyName = armyVal.String;
                    var army = new Army
                    {
                        Name = armyName,
                        NoRushOffsetX = (float)LuaRuntime.GetNumber(t, $"norushoffsetX_{armyName}"),
                        NoRushOffsetY = (float)LuaRuntime.GetNumber(t, $"norushoffsetY_{armyName}"),
                    };
                    info.Armies.Add(army);
                }
            }
        }

        // Customprops sit under Configurations.standard.customprops. Vanilla maps with neutrals
        // ship with ['ExtraArmies'] = STRING('ARMY_9 NEUTRAL_CIVILIAN') — we must round-trip the
        // whole table or the engine crashes when it can't resolve those armies on map load.
        var customProps = standard != null ? LuaRuntime.GetTable(standard, "customprops") : null;
        if (customProps != null)
        {
            foreach (var pair in customProps.Pairs)
            {
                if (pair.Key.Type != DataType.String) continue;
                var key = pair.Key.String;
                if (pair.Value.Type == DataType.String)
                    info.CustomProps[key] = pair.Value.String;
            }
        }

        return info;
    }
}
