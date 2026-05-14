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

            // Navigate down to the army's outer Units table. Children of that table represent
            // spawn categories: 'INITIAL' is the common one, but vanilla maps also use 'WRECKAGE'
            // (pre-destroyed entities) and free-form 'GROUP_N' wrappers — sometimes mixed at the
            // same level, sometimes alongside an empty INITIAL. Recursing through all of them is
            // the only way to recover everything; specifically targeting INITIAL silently dropped
            // units placed in any other category (e.g. SCMP_022 has 2 buildings under
            // ARMY_2's 'GROUP_4', sibling of an empty INITIAL).
            var outerUnits = LuaRuntime.GetTable(army, "Units");
            if (outerUnits == null) continue;
            var innerUnits = LuaRuntime.GetTable(outerUnits, "Units");
            if (innerUnits == null) continue;

            var list = new List<UnitSpawn>();
            // Each top-level child here is a spawn category (INITIAL, WRECKAGE, GROUP_4, …). We
            // pass it down so every leaf unit inherits the category of its containing block — the
            // writer needs that label intact, otherwise wrecks turn into live units on save.
            foreach (var cat in innerUnits.Pairs)
            {
                if (cat.Key.Type != DataType.String || cat.Value.Type != DataType.Table) continue;
                var subUnits = LuaRuntime.GetTable(cat.Value.Table, "Units");
                if (subUnits == null) continue;
                CollectUnitsRecursively(subUnits, list, cat.Key.String);
            }
            if (list.Count > 0)
                result[armyName] = list;
        }
        return result;
    }

    /// <summary>
    /// Walk a Units table and pull every leaf unit (table with a <c>type</c> field) into the list.
    /// Vanilla maps nest civilians inside arbitrary GROUP wrappers — e.g. SCMP_022's NEUTRAL_CIVILIAN
    /// has its 184 units under <c>['GROUP_1'] = GROUP { Units = { ... } }</c>. Without recursion the
    /// parser would treat GROUP_1 as a single (typeless) unit and silently drop everything else.
    /// The <paramref name="category"/> argument is propagated from the topmost container name so
    /// the writer can later emit each unit back under its original INITIAL/WRECKAGE/etc. block.
    /// </summary>
    private static void CollectUnitsRecursively(Table container, List<UnitSpawn> sink, string category)
    {
        foreach (var u in container.Pairs)
        {
            if (u.Key.Type != DataType.String || u.Value.Type != DataType.Table) continue;
            var t = u.Value.Table;

            // Heuristic: a leaf unit has a 'type' string. A GROUP wrapper has a nested Units table
            // instead and no type field. Some campaign maps mix both at the same level.
            var typeVal = t.Get("type");
            if (typeVal.Type == DataType.String && !string.IsNullOrEmpty(typeVal.String))
            {
                var spawn = new UnitSpawn
                {
                    Name = u.Key.String,
                    BlueprintId = typeVal.String,
                    Position = LuaRuntime.GetVector3(t, "Position"),
                    Orientation = LuaRuntime.GetVector3(t, "Orientation"),
                    Category = category,
                };
                var orders = t.Get("orders");
                if (orders.Type == DataType.String) spawn.Orders = orders.String;
                var platoon = t.Get("platoon");
                if (platoon.Type == DataType.String) spawn.Platoon = platoon.String;
                sink.Add(spawn);
                continue;
            }

            var nested = LuaRuntime.GetTable(t, "Units");
            if (nested != null) CollectUnitsRecursively(nested, sink, category);
        }
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

        // Read the prop blueprint reference if present; let an empty value flow through so the
        // writer's DefaultPropFor() fallback fires for user-created markers.
        var propPath = LuaRuntime.GetString(t, "prop");
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
            Prop = string.IsNullOrEmpty(propPath) ? null : propPath,
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
