using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Operations;

/// <summary>
/// Undoable record of a splatmap brush stroke. Snapshots the full DDS byte buffer before and
/// after — small relative to the total work the stroke does, and trivial to restore.
/// </summary>
public class SplatmapBrushOp : IMapOperation
{
    private readonly TextureMask _mask;
    private readonly byte[] _before;
    private readonly byte[] _after;

    public string Description { get; }

    public SplatmapBrushOp(TextureMask mask, byte[] before, byte[] after, string description)
    {
        _mask = mask;
        _before = before;
        _after = after;
        Description = description;
    }

    public void Execute() => _mask.DdsData = _after;
    public void Undo()    => _mask.DdsData = _before;
}

/// <summary>
/// Paints one terrain strata into the corresponding splatmap channel. Call <see cref="BeginStroke"/>,
/// <see cref="ApplyBrush"/> on every mouse move, then <see cref="EndStroke"/> to obtain the
/// undoable op.
///
/// Channel mapping (DDS pixel byte order is BGRA — D3DFMT_A8R8G8B8 in little-endian memory):
///   strata 1 / 5 → byte 2 (R component, shader .r)
///   strata 2 / 6 → byte 1 (G)
///   strata 3 / 7 → byte 0 (B)
///   strata 4 / 8 → byte 3 (A)
/// strata 1-4 live in <c>TextureMaskLow</c>, strata 5-8 in <c>TextureMaskHigh</c>.
/// </summary>
public class SplatmapBrushTool
{
    private const int DdsHeader = 128;

    private ScMap? _map;
    private TextureMask? _mask;
    private byte[]? _before;
    private int _channelOffset;
    private bool _active;

    /// <summary>1-8 — which strata to paint.</summary>
    public int StrataIndex { get; set; } = 1;
    /// <summary>Brush radius in heightmap (world) units. Scaled to splatmap pixels inside the tool.</summary>
    public float Radius { get; set; } = 15f;
    /// <summary>0-100 — how aggressively each frame adds to the channel.</summary>
    public float Strength { get; set; } = 10f;

    public void BeginStroke(ScMap map, float centerX, float centerZ)
    {
        var (mask, off) = ResolveMaskAndChannel(map, StrataIndex);
        if (mask == null) return;
        if (mask.DdsData.Length < DdsHeader + mask.Width * mask.Height * 4) return;

        _map = map;
        _mask = mask;
        _channelOffset = off;
        _before = (byte[])mask.DdsData.Clone();
        _active = true;
    }

    public void ApplyBrush(float centerX, float centerZ)
    {
        if (!_active || _mask == null || _map == null) return;
        int mW = _mask.Width, mH = _mask.Height;
        if (mW <= 0 || mH <= 0) return;
        if (_mask.DdsData.Length < DdsHeader + mW * mH * 4) return;

        // World → splatmap pixel coordinates. SC maps usually have splatmap = heightmap size, but
        // we scale defensively so the brush feels right on the rare half-res splatmaps too.
        float scaleX = (float)mW / Math.Max(1, _map.Heightmap.Width);
        float scaleZ = (float)mH / Math.Max(1, _map.Heightmap.Height);
        float sx = centerX * scaleX;
        float sz = centerZ * scaleZ;
        float r = Radius * scaleX;
        if (r < 1f) r = 1f;

        int xMin = Math.Max(0, (int)MathF.Floor(sx - r));
        int xMax = Math.Min(mW - 1, (int)MathF.Ceiling(sx + r));
        int zMin = Math.Max(0, (int)MathF.Floor(sz - r));
        int zMax = Math.Min(mH - 1, (int)MathF.Ceiling(sz + r));
        float r2 = r * r;

        // Per-frame painted amount. 0.018 ≈ heightmap brush's 0.016 — feels similar at equal Strength.
        float perFrame = Strength * 0.018f;

        for (int z = zMin; z <= zMax; z++)
        {
            int rowOffset = DdsHeader + z * mW * 4;
            for (int x = xMin; x <= xMax; x++)
            {
                float dx = x - sx;
                float dz = z - sz;
                float d2 = dx * dx + dz * dz;
                if (d2 > r2) continue;

                float falloff = 1f - MathF.Sqrt(d2) / r;
                falloff = falloff * falloff; // quadratic — softer edges
                float delta = perFrame * falloff * 255f;

                int idx = rowOffset + x * 4 + _channelOffset;
                int newVal = _mask.DdsData[idx] + (int)delta;
                _mask.DdsData[idx] = (byte)Math.Min(255, newVal);
            }
        }
    }

    public SplatmapBrushOp? EndStroke()
    {
        if (!_active || _mask == null || _before == null)
        {
            _active = false;
            return null;
        }
        var after = (byte[])_mask.DdsData.Clone();
        _active = false;

        // Skip pushing an op if the user just pressed without moving and nothing actually changed.
        bool changed = false;
        for (int i = 0; i < _before.Length; i++)
        {
            if (_before[i] != after[i]) { changed = true; break; }
        }
        if (!changed) return null;

        return new SplatmapBrushOp(_mask, _before, after, $"Paint strata {StrataIndex}");
    }

    private static (TextureMask? mask, int channelOffset) ResolveMaskAndChannel(ScMap map, int strata)
    {
        if (strata < 1 || strata > 8) return (null, 0);
        var mask = strata <= 4 ? map.TextureMaskLow : map.TextureMaskHigh;
        // Within each mask: channels R/G/B/A map to strata {1,2,3,4} or {5,6,7,8}. Byte order in
        // a D3DFMT_A8R8G8B8 DDS is BGRA in memory, so R is at offset 2, G at 1, B at 0, A at 3.
        int withinMask = ((strata - 1) % 4) + 1;
        int channelOffset = withinMask switch
        {
            1 => 2, // R
            2 => 1, // G
            3 => 0, // B
            4 => 3, // A
            _ => 0,
        };
        return (mask, channelOffset);
    }
}
