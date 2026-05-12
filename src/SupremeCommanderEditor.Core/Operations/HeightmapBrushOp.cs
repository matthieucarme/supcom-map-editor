using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Operations;

public enum BrushMode
{
    Raise,
    Lower,
    Smooth,
    Flatten,
    Plateau,
}

/// <summary>
/// Stores a heightmap region snapshot for undo/redo of a complete brush stroke.
/// </summary>
public class HeightmapBrushOp : IMapOperation
{
    private readonly Heightmap _heightmap;
    private readonly int _x, _y, _w, _h;
    private readonly ushort[] _beforeData;
    private readonly ushort[] _afterData;

    public string Description { get; }

    public HeightmapBrushOp(Heightmap heightmap, int x, int y, int w, int h,
        ushort[] beforeData, ushort[] afterData, string description)
    {
        _heightmap = heightmap;
        _x = x;
        _y = y;
        _w = w;
        _h = h;
        _beforeData = beforeData;
        _afterData = afterData;
        Description = description;
    }

    public void Execute() => ApplyData(_afterData);
    public void Undo() => ApplyData(_beforeData);

    private void ApplyData(ushort[] data)
    {
        int hmW = _heightmap.Width + 1;
        int idx = 0;
        for (int dy = 0; dy < _h; dy++)
        {
            for (int dx = 0; dx < _w; dx++)
            {
                int hx = _x + dx;
                int hy = _y + dy;
                if (hx >= 0 && hx <= _heightmap.Width && hy >= 0 && hy <= _heightmap.Height)
                {
                    _heightmap.Data[hy * hmW + hx] = data[idx];
                }
                idx++;
            }
        }
    }

    /// <summary>
    /// Capture a rectangular region of heightmap data.
    /// </summary>
    public static ushort[] CaptureRegion(Heightmap heightmap, int x, int y, int w, int h)
    {
        int hmW = heightmap.Width + 1;
        var data = new ushort[w * h];
        int idx = 0;
        for (int dy = 0; dy < h; dy++)
        {
            for (int dx = 0; dx < w; dx++)
            {
                int hx = x + dx;
                int hy = y + dy;
                if (hx >= 0 && hx <= heightmap.Width && hy >= 0 && hy <= heightmap.Height)
                    data[idx] = heightmap.Data[hy * hmW + hx];
                idx++;
            }
        }
        return data;
    }
}

/// <summary>
/// Applies brush strokes to a heightmap. Call BeginStroke, ApplyBrush (per mouse move), EndStroke.
/// </summary>
public class HeightmapBrushTool
{
    private Heightmap? _heightmap;
    private int _regionX, _regionY, _regionW, _regionH;
    private ushort[]? _beforeData;
    private bool _strokeActive;

    public BrushMode Mode { get; set; } = BrushMode.Raise;
    public float Radius { get; set; } = 15f;
    public float Strength { get; set; } = 10f;

    public void BeginStroke(Heightmap heightmap, float centerX, float centerZ)
    {
        _heightmap = heightmap;

        // Capture the entire heightmap as the "before" region (simplest approach)
        _regionX = 0;
        _regionY = 0;
        _regionW = heightmap.Width + 1;
        _regionH = heightmap.Height + 1;
        _beforeData = HeightmapBrushOp.CaptureRegion(heightmap, _regionX, _regionY, _regionW, _regionH);
        _strokeActive = true;
    }

    public void ApplyBrush(float centerX, float centerZ)
    {
        if (!_strokeActive || _heightmap == null) return;

        int cx = (int)centerX;
        int cz = (int)centerZ;
        int r = (int)MathF.Ceiling(Radius);

        int xMin = Math.Max(0, cx - r);
        int xMax = Math.Min(_heightmap.Width, cx + r);
        int zMin = Math.Max(0, cz - r);
        int zMax = Math.Min(_heightmap.Height, cz + r);

        float flattenTarget = 0;
        if (Mode == BrushMode.Flatten || Mode == BrushMode.Plateau)
        {
            flattenTarget = _heightmap.GetWorldHeight(
                Math.Clamp(cx, 0, _heightmap.Width),
                Math.Clamp(cz, 0, _heightmap.Height));
        }

        for (int z = zMin; z <= zMax; z++)
        {
            for (int x = xMin; x <= xMax; x++)
            {
                float dx = x - centerX;
                float dz = z - centerZ;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                if (dist > Radius) continue;

                // Smooth falloff
                float falloff = 1f - (dist / Radius);
                falloff = falloff * falloff; // quadratic

                float current = _heightmap.GetWorldHeight(x, z);
                float delta = Strength * falloff * 0.016f; // ~60fps frame time

                float newHeight = Mode switch
                {
                    BrushMode.Raise => current + delta,
                    BrushMode.Lower => current - delta,
                    BrushMode.Smooth => current + (GetNeighborAverage(_heightmap, x, z) - current) * falloff * 0.1f,
                    BrushMode.Flatten => current + (flattenTarget - current) * falloff * 0.2f,
                    BrushMode.Plateau => current >= flattenTarget - 0.5f ? Math.Max(current, flattenTarget) : current,
                    _ => current
                };

                _heightmap.SetWorldHeight(x, z, Math.Clamp(newHeight, 0, 512));
            }
        }
    }

    public HeightmapBrushOp? EndStroke()
    {
        if (!_strokeActive || _heightmap == null || _beforeData == null)
        {
            _strokeActive = false;
            return null;
        }

        var afterData = HeightmapBrushOp.CaptureRegion(_heightmap, _regionX, _regionY, _regionW, _regionH);

        // Check if anything actually changed
        bool changed = false;
        for (int i = 0; i < _beforeData.Length; i++)
        {
            if (_beforeData[i] != afterData[i]) { changed = true; break; }
        }

        _strokeActive = false;

        if (!changed) return null;

        return new HeightmapBrushOp(_heightmap, _regionX, _regionY, _regionW, _regionH,
            _beforeData, afterData, $"Heightmap {Mode}");
    }

    private static float GetNeighborAverage(Heightmap hm, int x, int z)
    {
        float sum = 0;
        int count = 0;
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                int nx = x + dx, nz = z + dz;
                if (nx >= 0 && nx <= hm.Width && nz >= 0 && nz <= hm.Height)
                {
                    sum += hm.GetWorldHeight(nx, nz);
                    count++;
                }
            }
        }
        return count > 0 ? sum / count : hm.GetWorldHeight(x, z);
    }
}
