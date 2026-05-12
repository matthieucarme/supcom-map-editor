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
}
