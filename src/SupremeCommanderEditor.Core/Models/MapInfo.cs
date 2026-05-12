namespace SupremeCommanderEditor.Core.Models;

public class MapInfo
{
    public string Name { get; set; } = "Untitled Map";
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = "skirmish";
    public bool Starts { get; set; } = true;
    public int Width { get; set; } = 256;
    public int Height { get; set; } = 256;
    public int MapVersion { get; set; } = 1;
    public float NoRushRadius { get; set; } = 70f;
    public string Preview { get; set; } = string.Empty;
    public List<Army> Armies { get; set; } = [];

    /// <summary>Lobby-side custom props from <c>Configurations.standard.customprops</c>. Vanilla SC1
    /// maps with civilian/neutral armies (SCMP_018, SCMP_039…) ship with
    /// <c>['ExtraArmies'] = STRING('ARMY_9 NEUTRAL_CIVILIAN')</c> — without that field the engine
    /// crashes when it tries to spawn entities under those armies from <c>_save.lua</c>. We
    /// preserve everything we read here verbatim and write it back, so saved maps stay loadable.</summary>
    public Dictionary<string, string> CustomProps { get; set; } = [];
}
