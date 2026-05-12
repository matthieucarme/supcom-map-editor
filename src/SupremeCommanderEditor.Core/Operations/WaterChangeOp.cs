using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Operations;

/// <summary>
/// Snapshot of the four user-facing water fields so a Water palette popup edit reverts in one
/// Ctrl+Z. Covers the toggle and the three elevation tiers; the advanced color/refraction/sun
/// reflection fields are deliberately excluded (not exposed in the bottom palette).
/// </summary>
public readonly record struct WaterSnapshot(
    bool HasWater,
    float Elevation,
    float ElevationDeep,
    float ElevationAbyss)
{
    public static WaterSnapshot Of(WaterSettings w) => new(
        w.HasWater, w.Elevation, w.ElevationDeep, w.ElevationAbyss);

    public void ApplyTo(WaterSettings w)
    {
        w.HasWater = HasWater;
        w.Elevation = Elevation;
        w.ElevationDeep = ElevationDeep;
        w.ElevationAbyss = ElevationAbyss;
    }
}

public class WaterChangeOp : IMapOperation
{
    private readonly WaterSettings _target;
    private readonly WaterSnapshot _before;
    private readonly WaterSnapshot _after;

    public string Description { get; }

    public WaterChangeOp(WaterSettings target, WaterSnapshot before, WaterSnapshot after, string description)
    {
        _target = target;
        _before = before;
        _after = after;
        Description = description;
    }

    public void Execute() => _after.ApplyTo(_target);
    public void Undo()    => _before.ApplyTo(_target);
}
