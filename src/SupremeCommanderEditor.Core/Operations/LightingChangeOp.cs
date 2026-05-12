using System.Numerics;
using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Operations;

/// <summary>
/// Snapshot of the lighting settings so a dialog edit (Sun Color, Ambience, Multiplier, Sun Dir,
/// Bloom, Fog) can be reverted in one Ctrl+Z. Captures everything the lighting popups can touch.
/// </summary>
public readonly record struct LightingSnapshot(
    float LightingMultiplier,
    Vector3 SunDirection,
    Vector3 SunAmbience,
    Vector3 SunColor,
    float Bloom,
    float FogStart,
    float FogEnd)
{
    public static LightingSnapshot Of(LightingSettings l) => new(
        l.LightingMultiplier, l.SunDirection, l.SunAmbience, l.SunColor,
        l.Bloom, l.FogStart, l.FogEnd);

    public void ApplyTo(LightingSettings l)
    {
        l.LightingMultiplier = LightingMultiplier;
        l.SunDirection = SunDirection;
        l.SunAmbience = SunAmbience;
        l.SunColor = SunColor;
        l.Bloom = Bloom;
        l.FogStart = FogStart;
        l.FogEnd = FogEnd;
    }
}

public class LightingChangeOp : IMapOperation
{
    private readonly LightingSettings _target;
    private readonly LightingSnapshot _before;
    private readonly LightingSnapshot _after;

    public string Description { get; }

    public LightingChangeOp(LightingSettings target, LightingSnapshot before, LightingSnapshot after, string description)
    {
        _target = target;
        _before = before;
        _after = after;
        Description = description;
    }

    public void Execute() => _after.ApplyTo(_target);
    public void Undo() => _before.ApplyTo(_target);
}
