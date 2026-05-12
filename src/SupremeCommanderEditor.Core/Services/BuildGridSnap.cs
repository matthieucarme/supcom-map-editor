using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Services;

/// <summary>
/// Snap resource-marker positions to the SupCom build grid so the in-game extractor footprint
/// lines up with the visual deposit. Verified empirically by scanning 3239 mass markers and
/// 188 hydro markers across 75 vanilla maps: every resource marker stores its position at a
/// cell-center, i.e. `floor(x) + 0.5` (parity of the integer part is irrelevant). The engine
/// then plants the extractor footprint centered on that point.
/// </summary>
public static class BuildGridSnap
{
    /// <summary>Round (x, z) to the nearest half-cell (`N + 0.5`) on both axes.</summary>
    public static (float x, float z) Snap(MarkerType type, float x, float z) => type switch
    {
        MarkerType.Mass or MarkerType.Hydrocarbon =>
            (MathF.Round(x - 0.5f) + 0.5f, MathF.Round(z - 0.5f) + 0.5f),
        _ => (x, z),
    };

    /// <summary>In-place adjust every Mass/Hydro marker so they sit on the build grid.</summary>
    public static int SnapAll(ScMap map)
    {
        int n = 0;
        foreach (var m in map.Markers)
        {
            if (m.Type != MarkerType.Mass && m.Type != MarkerType.Hydrocarbon) continue;
            var p = m.Position;
            var (x, z) = Snap(m.Type, p.X, p.Z);
            if (x != p.X || z != p.Z) n++;
            m.Position = new System.Numerics.Vector3(x, p.Y, z);
        }
        return n;
    }
}
