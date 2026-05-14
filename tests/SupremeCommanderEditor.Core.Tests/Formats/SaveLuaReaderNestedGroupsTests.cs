using System.Numerics;
using SupremeCommanderEditor.Core.Formats.Lua;
using SupremeCommanderEditor.Core.Models;
using Xunit;

namespace SupremeCommanderEditor.Core.Tests.Formats;

/// <summary>
/// Regression coverage for nested GROUP wrappers inside <c>INITIAL.Units</c>. Several vanilla maps
/// (notably SCMP_022 / Arctic Refuge) store their civilians inside a sub-table like
/// <c>['GROUP_1'] = GROUP { Units = { ... } }</c> rather than flat under INITIAL.Units. The
/// original parser stopped at the first level and silently dropped everything underneath —
/// Arctic Refuge ended up with 30/214 units, the missing 184 being all the NEUTRAL_CIVILIAN
/// buildings. The reader now walks the tree recursively.
/// </summary>
public class SaveLuaReaderNestedGroupsTests
{
    [Fact]
    public void ReadInitialUnitsByArmy_RecursesIntoGroupWrappers()
    {
        // Mimic Arctic Refuge's structure: civilians under GROUP_1 wrapper, plus a plain unit at
        // the top level to confirm both shapes coexist.
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, """
                function STRING(s) return s end
                function FLOAT(n) return n end
                function BOOLEAN(b) return b end
                function GROUP(t) return t end
                Scenario = {
                    Armies = {
                        ['NEUTRAL_CIVILIAN'] = {
                            Units = GROUP {
                                Units = {
                                    ['INITIAL'] = GROUP {
                                        Units = {
                                            -- A nested group, like SCMP_022's GROUP_1
                                            ['GROUP_1'] = GROUP {
                                                Units = {
                                                    ['UNIT_1'] = { type = 'uec0001',
                                                        Position = { 10, 20, 30 },
                                                        Orientation = { 0, 0, 0 } },
                                                    ['UNIT_2'] = { type = 'uec1101',
                                                        Position = { 11, 21, 31 },
                                                        Orientation = { 0, 0, 0 } },
                                                },
                                            },
                                            -- A flat unit at the same level, like SCMP_025's INITIAL
                                            ['UNIT_3'] = { type = 'uec1201',
                                                Position = { 12, 22, 32 },
                                                Orientation = { 0, 0, 0 } },
                                        },
                                    },
                                },
                            },
                        },
                    },
                }
                """);

            var result = SaveLuaReader.ReadInitialUnitsByArmy(tmp);
            Assert.True(result.ContainsKey("NEUTRAL_CIVILIAN"));
            var units = result["NEUTRAL_CIVILIAN"];
            // 2 nested + 1 flat = 3 leaf units total
            Assert.Equal(3, units.Count);

            // Verify the nested ones came through with correct blueprint ids (regression on the
            // silent-drop bug where they'd be missing entirely).
            var names = units.Select(u => u.Name).ToHashSet();
            Assert.Contains("UNIT_1", names);
            Assert.Contains("UNIT_2", names);
            Assert.Contains("UNIT_3", names);

            var u1 = units.First(u => u.Name == "UNIT_1");
            Assert.Equal("uec0001", u1.BlueprintId);
            Assert.Equal(10f, u1.Position.X);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void ReadInitialUnitsByArmy_CollectsFromSiblingCategories()
    {
        // Several vanilla maps put units under sibling categories of INITIAL — 'WRECKAGE' for
        // pre-destroyed entities (SCMP_005, _009, _011, _021, _031…), or arbitrary 'GROUP_N'
        // wrappers (SCMP_022's ARMY_2 has 2 buildings under GROUP_4 alongside an empty INITIAL).
        // The parser must walk every sibling, not just INITIAL.
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, """
                function STRING(s) return s end
                function GROUP(t) return t end
                Scenario = {
                    Armies = {
                        ['ARMY_2'] = {
                            Units = GROUP {
                                Units = {
                                    ['INITIAL'] = GROUP { Units = { } },
                                    ['WRECKAGE'] = GROUP {
                                        Units = {
                                            ['UNIT_99'] = { type = 'ueb1301',
                                                Position = { 1, 2, 3 },
                                                Orientation = { 0, 0, 0 } },
                                        },
                                    },
                                    ['GROUP_4'] = GROUP {
                                        Units = {
                                            ['UNIT_100'] = { type = 'ueb2101',
                                                Position = { 4, 5, 6 },
                                                Orientation = { 0, 0, 0 } },
                                        },
                                    },
                                },
                            },
                        },
                    },
                }
                """);

            var result = SaveLuaReader.ReadInitialUnitsByArmy(tmp);
            Assert.Equal(2, result["ARMY_2"].Count);
            var names = result["ARMY_2"].Select(u => u.Name).ToHashSet();
            Assert.Contains("UNIT_99", names);   // from WRECKAGE
            Assert.Contains("UNIT_100", names);  // from GROUP_4
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void ReadInitialUnitsByArmy_AssignsCorrectCategoryPerUnit()
    {
        // Sanity-check the load side of the WRECKAGE/INITIAL/GROUP_N preservation: each unit's
        // Category must match its containing top-level block, even when nested under sub-groups.
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, """
                function STRING(s) return s end
                function GROUP(t) return t end
                Scenario = {
                    Armies = {
                        ['ARMY_9'] = {
                            Units = GROUP {
                                Units = {
                                    ['INITIAL'] = GROUP {
                                        Units = {
                                            ['UNIT_A'] = { type = 'ueb1101',
                                                Position = { 1, 2, 3 },
                                                Orientation = { 0, 0, 0 } },
                                            -- nested wrapper inside INITIAL should still inherit INITIAL
                                            ['GROUP_X'] = GROUP {
                                                Units = {
                                                    ['UNIT_B'] = { type = 'ueb1102',
                                                        Position = { 4, 5, 6 },
                                                        Orientation = { 0, 0, 0 } },
                                                },
                                            },
                                        },
                                    },
                                    ['WRECKAGE'] = GROUP {
                                        Units = {
                                            ['UNIT_C'] = { type = 'urs0201',
                                                Position = { 7, 8, 9 },
                                                Orientation = { 0, 0, 0 } },
                                        },
                                    },
                                },
                            },
                        },
                    },
                }
                """);

            var result = SaveLuaReader.ReadInitialUnitsByArmy(tmp);
            var units = result["ARMY_9"].ToDictionary(u => u.Name);
            Assert.Equal("INITIAL", units["UNIT_A"].Category);
            Assert.Equal("INITIAL", units["UNIT_B"].Category);   // nested under GROUP_X but inside INITIAL block
            Assert.Equal("WRECKAGE", units["UNIT_C"].Category);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Roundtrip_WreckagePreservesCategoryThroughWriter()
    {
        // The critical contract: a wreck (Category=WRECKAGE) must come back out of the writer as
        // WRECKAGE — otherwise SC1 spawns it as a live, controllable unit on the next load, which
        // would silently rewrite gameplay on every map that ships with carcasses (SCMP_005, _009,
        // _011, _021, etc.).
        var army = new Army
        {
            Name = "ARMY_9",
            InitialUnits = new List<UnitSpawn>
            {
                new() { Name = "UNIT_1", BlueprintId = "ueb1101",
                        Position = new Vector3(1, 2, 3), Category = "INITIAL" },
                new() { Name = "UNIT_2", BlueprintId = "urs0201",
                        Position = new Vector3(4, 5, 6), Category = "WRECKAGE" },
                new() { Name = "UNIT_3", BlueprintId = "ueb2101",
                        Position = new Vector3(7, 8, 9), Category = "INITIAL" },
            },
        };

        var tmp = Path.GetTempFileName();
        try
        {
            // Markers list can be empty for this test — we only care about the army payload.
            SaveLuaWriter.Write(tmp, new List<Marker>(), new List<Army> { army });

            // Sanity check: the file should contain both an INITIAL and a WRECKAGE block under
            // the same army, not a single merged INITIAL.
            var raw = File.ReadAllText(tmp);
            Assert.Contains("['INITIAL'] = GROUP", raw);
            Assert.Contains("['WRECKAGE'] = GROUP", raw);

            // Round-trip check: re-read and verify each unit kept its category.
            var result = SaveLuaReader.ReadInitialUnitsByArmy(tmp);
            var byName = result["ARMY_9"].ToDictionary(u => u.Name);
            Assert.Equal("INITIAL", byName["UNIT_1"].Category);
            Assert.Equal("WRECKAGE", byName["UNIT_2"].Category);
            Assert.Equal("INITIAL", byName["UNIT_3"].Category);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void ReadInitialUnitsByArmy_SkipsGroupEntriesWithoutType()
    {
        // Pre-fix bug: an entry without a `type` field (i.e. a GROUP wrapper) was counted as a unit
        // anyway, producing phantom UnitSpawns with empty BlueprintId. Verify we no longer leak
        // such ghosts even when a wrapper is empty.
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, """
                function STRING(s) return s end
                function GROUP(t) return t end
                Scenario = {
                    Armies = {
                        ['ARMY_9'] = {
                            Units = GROUP {
                                Units = {
                                    ['INITIAL'] = GROUP {
                                        Units = {
                                            ['EMPTY_GROUP'] = GROUP { Units = { } },
                                            ['UNIT_42'] = { type = 'uel0001',
                                                Position = { 1, 2, 3 },
                                                Orientation = { 0, 0, 0 } },
                                        },
                                    },
                                },
                            },
                        },
                    },
                }
                """);

            var result = SaveLuaReader.ReadInitialUnitsByArmy(tmp);
            Assert.Single(result["ARMY_9"]);
            Assert.Equal("UNIT_42", result["ARMY_9"][0].Name);
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
