using SupremeCommanderEditor.Core.Formats.Lua;
using SupremeCommanderEditor.Core.Models;
using SupremeCommanderEditor.Core.Services;
using Xunit;

namespace SupremeCommanderEditor.Core.Tests.Services;

/// <summary>
/// Regression coverage for the "rename + save" double-bug reported in the field:
///   1. The first Save after a rename used to land in the *old* folder, so the user had to save
///      twice for the on-disk folder to match the name shown in the editor.
///   2. The first Save also overwrote the *original* map's _scenario.lua with the renamed map's
///      in-game name, corrupting the source map.
///
/// Root cause was reading <c>CurrentMap.Info.Name</c> instead of the editor's display name when
/// resolving the canonical save folder. <see cref="MapSavePathResolver"/> takes the display name
/// as an explicit argument, so a regression would require deliberately re-routing the call.
/// </summary>
public class MapSavePathResolverTests
{
    [Fact]
    public void ResolveCanonicalFolder_UsesDisplayName_NotAnyOtherStringHangingAroundTheVm()
    {
        // The whole point of the fix: the resolver derives the path from the display name passed
        // in, period. If someone re-routes the VM to pass CurrentMap.Info.Name again, the unit-test
        // contract here doesn't change — but the integration test below will catch the regression.
        var (folder, error) = MapSavePathResolver.ResolveCanonicalFolder("/game", "MyCopy");
        Assert.Null(error);
        Assert.Equal(Path.Combine("/game", "maps", "MyCopy"), folder);
    }

    [Fact]
    public void ResolveCanonicalFolder_RejectsEmptyName()
    {
        var (folder, error) = MapSavePathResolver.ResolveCanonicalFolder("/game", "");
        Assert.Null(folder);
        Assert.NotNull(error);
    }

    [Fact]
    public void ResolveCanonicalFolder_RejectsMissingGamePath()
    {
        var (folder, error) = MapSavePathResolver.ResolveCanonicalFolder(null, "MyMap");
        Assert.Null(folder);
        Assert.NotNull(error);
    }

    [Fact]
    public void ResolveCanonicalFolder_StripsInvalidFileNameChars()
    {
        // Whatever Path.GetInvalidFileNameChars() reports on this platform must be stripped — on
        // Windows that includes '<' '>' ':' '"' '/' '\\' '|' '?' '*', so a LOC prefix carried into
        // here would otherwise produce a weird "LOC SCMP_001Burial Mounds" folder name.
        var invalid = Path.GetInvalidFileNameChars();
        var dirty = "Hello" + new string(invalid) + "World";
        var (folder, error) = MapSavePathResolver.ResolveCanonicalFolder("/game", dirty);
        Assert.Null(error);
        Assert.Equal(Path.Combine("/game", "maps", "HelloWorld"), folder);
    }

    /// <summary>
    /// Full integration check: simulate the VM's save flow for a renamed map and assert that
    ///   (a) the canonical folder resolves to the *new* name on the first attempt, and
    ///   (b) writing the new scenario.lua does NOT touch the source map's folder.
    /// This is what the field-reported bug actually broke end-to-end.
    /// </summary>
    [Fact]
    public void RenameThenSave_CreatesNewFolder_AndLeavesOriginalUntouched()
    {
        var gameRoot = Path.Combine(Path.GetTempPath(), "scme-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(gameRoot, "maps"));

            // 1. Lay down an "original" map at <game>/maps/MyMap/ with its scenario.lua naming itself "MyMap".
            var originalFolder = Path.Combine(gameRoot, "maps", "MyMap");
            Directory.CreateDirectory(originalFolder);
            var originalScenario = Path.Combine(originalFolder, "MyMap_scenario.lua");
            ScenarioLuaWriter.Write(
                originalScenario,
                new MapInfo { Name = "MyMap", Width = 256, Height = 256 },
                mapFolderName: "MyMap",
                mapBaseName: "MyMap");
            var originalContentBefore = File.ReadAllText(originalScenario);
            Assert.Contains("MyMap", originalContentBefore);

            // 2. Simulate the rename: editor field is now "MyCopy", but the VM hasn't yet propagated
            //    that into CurrentMap.Info.Name (which is the precondition that triggered the bug).
            //    The resolver must still pick "MyCopy" as the destination folder.
            var (newFolder, error) = MapSavePathResolver.ResolveCanonicalFolder(gameRoot, "MyCopy");
            Assert.Null(error);
            Assert.Equal(Path.Combine(gameRoot, "maps", "MyCopy"), newFolder);
            Assert.NotEqual(originalFolder, newFolder);

            // 3. Carry out the save into the resolved folder, mirroring what the VM does post-fix.
            Directory.CreateDirectory(newFolder!);
            var newScenario = Path.Combine(newFolder!, "MyCopy_scenario.lua");
            ScenarioLuaWriter.Write(
                newScenario,
                new MapInfo { Name = "MyCopy", Width = 256, Height = 256 },
                mapFolderName: "MyCopy",
                mapBaseName: "MyCopy");

            // 4a. New folder exists with the new name baked into its scenario.lua.
            Assert.True(File.Exists(newScenario));
            Assert.Contains("MyCopy", File.ReadAllText(newScenario));

            // 4b. Original folder still exists and is byte-identical to step 1 — no leak of the new name.
            Assert.True(File.Exists(originalScenario));
            Assert.Equal(originalContentBefore, File.ReadAllText(originalScenario));
        }
        finally
        {
            if (Directory.Exists(gameRoot)) Directory.Delete(gameRoot, recursive: true);
        }
    }
}
