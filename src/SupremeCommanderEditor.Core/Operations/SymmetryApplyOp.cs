using SupremeCommanderEditor.Core.Models;
using SupremeCommanderEditor.Core.Services;

namespace SupremeCommanderEditor.Core.Operations;

/// <summary>
/// Apply a symmetry pattern to a map. Captures the full pre-state so Undo restores it exactly.
/// Snapshots cover everything the service mirrors: heightmap, both splatmaps, markers, props,
/// and per-army InitialUnits lists.
/// </summary>
public class SymmetryApplyOp : IMapOperation
{
    private readonly ScMap _map;
    private readonly SymmetryPattern _pattern;
    private readonly SymmetryRegion _source;
    private readonly SymmetryMode _mode;
    private readonly bool _terrainOnly;

    private ushort[]? _prevHeightmap;
    private byte[]? _prevMaskLow;
    private byte[]? _prevMaskHigh;
    private List<Marker>? _prevMarkers;
    private List<Prop>? _prevProps;
    private Dictionary<Army, List<UnitSpawn>>? _prevUnits;

    public string Description => _terrainOnly
        ? $"Symmetry {_pattern} ({_mode}) terrain ({_source})"
        : $"Symmetry {_pattern} ({_mode}) ({_source})";

    /// <param name="terrainOnly">When true, only the heightmap and splatmaps are mirrored;
    /// markers, props, and per-army pre-placed units stay where they are. Useful for refining a
    /// generated map's terrain without scrambling the already-balanced spawn layout.</param>
    public SymmetryApplyOp(ScMap map, SymmetryPattern pattern, SymmetryRegion source, SymmetryMode mode = SymmetryMode.Mirror, bool terrainOnly = false)
    {
        _map = map;
        _pattern = pattern;
        _source = source;
        _mode = mode;
        _terrainOnly = terrainOnly;
    }

    public void Execute()
    {
        // Snapshot before mutating so Undo can restore. Skip the entity snapshots in terrain-only
        // mode — those lists aren't touched and copying them would be wasted memory.
        _prevHeightmap = (ushort[])_map.Heightmap.Data.Clone();
        _prevMaskLow = (byte[])_map.TextureMaskLow.DdsData.Clone();
        _prevMaskHigh = (byte[])_map.TextureMaskHigh.DdsData.Clone();

        if (_terrainOnly)
        {
            SymmetryService.ApplyTerrainOnly(_map, _pattern, _source, _mode);
        }
        else
        {
            _prevMarkers = [.._map.Markers];
            _prevProps = [.._map.Props];
            _prevUnits = _map.Info.Armies.ToDictionary(a => a, a => a.InitialUnits.ToList());
            SymmetryService.Apply(_map, _pattern, _source, _mode);
        }
    }

    public void Undo()
    {
        if (_prevHeightmap != null)
            _map.Heightmap.Data = _prevHeightmap;
        if (_prevMaskLow != null)
            _map.TextureMaskLow.DdsData = _prevMaskLow;
        if (_prevMaskHigh != null)
            _map.TextureMaskHigh.DdsData = _prevMaskHigh;
        if (_prevMarkers != null)
        {
            _map.Markers.Clear();
            _map.Markers.AddRange(_prevMarkers);
        }
        if (_prevProps != null)
        {
            _map.Props.Clear();
            _map.Props.AddRange(_prevProps);
        }
        if (_prevUnits != null)
        {
            foreach (var (army, units) in _prevUnits)
            {
                army.InitialUnits.Clear();
                army.InitialUnits.AddRange(units);
            }
        }
    }
}
