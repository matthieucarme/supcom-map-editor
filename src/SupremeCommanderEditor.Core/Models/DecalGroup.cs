namespace SupremeCommanderEditor.Core.Models;

public class DecalGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<int> DecalIds { get; set; } = [];
}
