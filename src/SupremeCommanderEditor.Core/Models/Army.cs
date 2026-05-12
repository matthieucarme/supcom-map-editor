namespace SupremeCommanderEditor.Core.Models;

public class Army
{
    public string Name { get; set; } = string.Empty;
    public string Personality { get; set; } = string.Empty;
    public string Plans { get; set; } = string.Empty;
    public int Color { get; set; }
    public int Faction { get; set; }
    public float NoRushOffsetX { get; set; }
    public float NoRushOffsetY { get; set; }

    /// <summary>Pre-placed units belonging to this army (civilians, neutral defenses, wreckage…).</summary>
    public List<UnitSpawn> InitialUnits { get; set; } = [];
}
