using System.Numerics;
using MoonSharp.Interpreter;
using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Formats.Lua;

public static class SaveLuaReader
{
    /// <summary>
    /// Parse the Armies → Units → INITIAL Units block out of a _save.lua and return one
    /// <see cref="Army"/>-keyed dictionary of unit spawns. Used so we can preserve civilian / neutral
    /// preplaced units (shields, turrets, civilian buildings, wreckage) on round-trip.
    /// </summary>
    public static Dictionary<string, List<UnitSpawn>> ReadInitialUnitsByArmy(string filePath)
    {
        var result = new Dictionary<string, List<UnitSpawn>>(StringComparer.OrdinalIgnoreCase);
        var script = LuaRuntime.CreateScript();
        script.DoFile(filePath);

        var scenario = script.Globals.Get("Scenario");
        if (scenario.Type != DataType.Table) return result;

        var armiesTable = LuaRuntime.GetTable(scenario.Table, "Armies");
        if (armiesTable == null) return result;

        foreach (var pair in armiesTable.Pairs)
        {
            if (pair.Key.Type != DataType.String || pair.Value.Type != DataType.Table) continue;
            var armyName = pair.Key.String;
            var army = pair.Value.Table;

            // Navigate Units → Units → INITIAL → Units
            var outerUnits = LuaRuntime.GetTable(army, "Units");
            if (outerUnits == null) continue;
            var innerUnits = LuaRuntime.GetTable(outerUnits, "Units");
            if (innerUnits == null) continue;
            var initial = LuaRuntime.GetTable(innerUnits, "INITIAL");
            if (initial == null) continue;
            var units = LuaRuntime.GetTable(initial, "Units");
            if (units == null) continue;

            var list = new List<UnitSpawn>();
            foreach (var u in units.Pairs)
            {
                if (u.Key.Type != DataType.String || u.Value.Type != DataType.Table) continue;
                var t = u.Value.Table;
                var spawn = new UnitSpawn
                {
                    Name = u.Key.String,
                    BlueprintId = LuaRuntime.GetString(t, "type", ""),
                    Position = LuaRuntime.GetVector3(t, "Position"),
                    Orientation = LuaRuntime.GetVector3(t, "Orientation"),
                };
                var orders = t.Get("orders");
                if (orders.Type == DataType.String) spawn.Orders = orders.String;
                var platoon = t.Get("platoon");
                if (platoon.Type == DataType.String) spawn.Platoon = platoon.String;
                list.Add(spawn);
            }
            if (list.Count > 0)
                result[armyName] = list;
        }
        return result;
    }

    public static List<Marker> ReadMarkers(string filePath)
    {
        var script = LuaRuntime.CreateScript();
        script.DoFile(filePath);

        var scenario = script.Globals.Get("Scenario");
        if (scenario.Type != DataType.Table)
            throw new InvalidDataException("Scenario table not found in save file");

        var markers = new List<Marker>();

        var masterChain = LuaRuntime.GetTable(scenario.Table, "MasterChain");
        if (masterChain == null) return markers;

        var chain = LuaRuntime.GetTable(masterChain, "_MASTERCHAIN_");
        if (chain == null) return markers;

        var markersTable = LuaRuntime.GetTable(chain, "Markers");
        if (markersTable == null) return markers;

        foreach (var pair in markersTable.Pairs)
        {
            if (pair.Key.Type != DataType.String || pair.Value.Type != DataType.Table)
                continue;

            var marker = ParseMarker(pair.Key.String, pair.Value.Table);
            markers.Add(marker);
        }

        return markers;
    }

    private static Marker ParseMarker(string name, Table t)
    {
        var typeStr = LuaRuntime.GetString(t, "type", "Blank Marker");

        var marker = new Marker
        {
            Name = name,
            Type = ParseMarkerType(typeStr),
            Position = LuaRuntime.GetVector3(t, "position"),
            Orientation = LuaRuntime.GetVector3(t, "orientation"),
            Resource = LuaRuntime.GetBool(t, "resource"),
            Amount = (float)LuaRuntime.GetNumber(t, "amount", 100),
            Color = LuaRuntime.GetString(t, "color"),
            Hint = LuaRuntime.GetBool(t, "hint") ? "true" : null,
        };

        // AI path adjacency
        var adjTable = LuaRuntime.GetTable(t, "adjacentTo");
        if (adjTable != null)
        {
            foreach (var val in adjTable.Values)
            {
                if (val.Type == DataType.String)
                    marker.AdjacentMarkers.Add(val.String);
            }
        }

        // Graph
        var graph = t.Get("graph");
        if (graph.Type == DataType.String)
            marker.Graph = graph.String;

        // Camera
        var zoom = t.Get("zoom");
        if (zoom.Type == DataType.Number)
            marker.Zoom = (float)zoom.Number;
        var canSetCam = t.Get("canSetCamera");
        if (canSetCam.Type == DataType.Boolean)
            marker.CanSetCamera = canSetCam.Boolean;
        var canSyncCam = t.Get("canSyncCamera");
        if (canSyncCam.Type == DataType.Boolean)
            marker.CanSyncCamera = canSyncCam.Boolean;

        // Effect
        var effect = t.Get("EffectTemplate");
        if (effect.Type == DataType.String)
            marker.EffectTemplate = effect.String;

        // Weather
        var weatherType = t.Get("weatherType");
        if (weatherType.Type == DataType.String)
            marker.WeatherType = weatherType.String;

        return marker;
    }

    private static MarkerType ParseMarkerType(string typeStr) => typeStr switch
    {
        "Mass" => MarkerType.Mass,
        "Hydrocarbon" => MarkerType.Hydrocarbon,
        "Blank Marker" => MarkerType.BlankMarker,
        "Land Path Node" => MarkerType.LandPathNode,
        "Air Path Node" => MarkerType.AirPathNode,
        "Water Path Node" => MarkerType.WaterPathNode,
        "Amphibious Path Node" => MarkerType.AmphibiousPathNode,
        "Rally Point" => MarkerType.RallyPoint,
        "Naval Rally Point" => MarkerType.NavalRallyPoint,
        "Expansion Area" => MarkerType.ExpansionArea,
        "Large Expansion Area" => MarkerType.LargeExpansionArea,
        "Naval Area" => MarkerType.NavalArea,
        "Combat Zone" => MarkerType.CombatZone,
        "Defensive Point" => MarkerType.DefensePoint,
        "Protected Experimental Construction" => MarkerType.ProtectedExperimentalConstruction,
        "Camera Info" => MarkerType.CameraInfo,
        "Weather Generator" => MarkerType.WeatherGenerator,
        "Weather Definition" => MarkerType.WeatherDefinition,
        "Effect" => MarkerType.Effect,
        _ => MarkerType.BlankMarker
    };
}
