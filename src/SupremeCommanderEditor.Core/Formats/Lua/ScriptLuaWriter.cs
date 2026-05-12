namespace SupremeCommanderEditor.Core.Formats.Lua;

public static class ScriptLuaWriter
{
    private const string Template = """
        local ScenarioUtils = import('/lua/sim/ScenarioUtilities.lua')

        function OnPopulate()
        	ScenarioUtils.InitializeArmies()
        end

        function OnStart(self)
        end
        """;

    public static void Write(string filePath)
    {
        File.WriteAllText(filePath, Template);
    }
}
