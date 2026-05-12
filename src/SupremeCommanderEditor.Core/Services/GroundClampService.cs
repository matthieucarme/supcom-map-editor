using System.Numerics;
using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Services;

/// <summary>
/// Snap entity Y coordinates to the heightmap surface. Used after terrain editing so props
/// (rocks, trees, …) and ground-bound markers don't end up floating or buried.
/// </summary>
public static class GroundClampService
{
    /// <summary>Returns the bilinearly-sampled world height at (x, z) in heightmap coords.</summary>
    public static float SampleHeight(Heightmap hm, float x, float z)
    {
        int w = hm.Width;
        int h = hm.Height;
        float fx = Math.Clamp(x, 0f, w);
        float fz = Math.Clamp(z, 0f, h);
        int x0 = (int)MathF.Floor(fx);
        int z0 = (int)MathF.Floor(fz);
        int x1 = Math.Min(x0 + 1, w);
        int z1 = Math.Min(z0 + 1, h);
        float tx = fx - x0;
        float tz = fz - z0;

        float h00 = hm.GetWorldHeight(x0, z0);
        float h10 = hm.GetWorldHeight(x1, z0);
        float h01 = hm.GetWorldHeight(x0, z1);
        float h11 = hm.GetWorldHeight(x1, z1);

        float v0 = h00 + (h10 - h00) * tx;
        float v1 = h01 + (h11 - h01) * tx;
        return v0 + (v1 - v0) * tz;
    }

    /// <summary>Snap every prop's Y to the terrain surface beneath it. Returns the count.</summary>
    public static int ClampPropsToGround(ScMap map)
    {
        int n = 0;
        foreach (var prop in map.Props)
        {
            var p = prop.Position;
            float y = SampleHeight(map.Heightmap, p.X, p.Z);
            if (MathF.Abs(p.Y - y) > 0.001f) n++;
            prop.Position = new Vector3(p.X, y, p.Z);
        }
        return n;
    }

    /// <summary>Snap every pre-placed unit's Y to the terrain — buildings/turrets/wrecks sit on ground.</summary>
    public static int ClampInitialUnits(ScMap map)
    {
        int n = 0;
        foreach (var army in map.Info.Armies)
            foreach (var u in army.InitialUnits)
            {
                var p = u.Position;
                float y = SampleHeight(map.Heightmap, p.X, p.Z);
                if (MathF.Abs(p.Y - y) > 0.001f) n++;
                u.Position = new System.Numerics.Vector3(p.X, y, p.Z);
            }
        return n;
    }

    /// <summary>
    /// Snap every ground-bound marker's Y to the terrain. Air path nodes and metadata markers
    /// (camera/weather/effect) keep their explicit Y.
    /// </summary>
    public static int ClampGroundMarkers(ScMap map)
    {
        int n = 0;
        foreach (var m in map.Markers)
        {
            if (!IsGroundBound(m.Type)) continue;
            var p = m.Position;
            float y = SampleHeight(map.Heightmap, p.X, p.Z);
            if (MathF.Abs(p.Y - y) > 0.001f) n++;
            m.Position = new Vector3(p.X, y, p.Z);
        }
        return n;
    }

    private static bool IsGroundBound(MarkerType t) => t switch
    {
        MarkerType.AirPathNode => false,
        MarkerType.CameraInfo => false,
        MarkerType.WeatherGenerator => false,
        MarkerType.WeatherDefinition => false,
        MarkerType.Effect => false,
        _ => true,
    };
}
