using System.Numerics;
using SupremeCommanderEditor.Core.Models;
using SupremeCommanderEditor.Core.Services;

namespace SupremeCommanderEditor.Core.Operations;

/// <summary>
/// Snap props (and optionally ground-bound markers) to the heightmap surface in one undoable step.
/// </summary>
public class ClampToGroundOp : IMapOperation
{
    private readonly ScMap _map;
    private readonly bool _includeMarkers;
    private List<Vector3>? _prevPropPos;
    private List<Vector3>? _prevMarkerPos;

    public string Description => _includeMarkers ? "Clamp props + markers to ground" : "Clamp props to ground";

    public ClampToGroundOp(ScMap map, bool includeMarkers = true)
    {
        _map = map;
        _includeMarkers = includeMarkers;
    }

    public void Execute()
    {
        _prevPropPos = _map.Props.Select(p => p.Position).ToList();
        GroundClampService.ClampPropsToGround(_map);
        if (_includeMarkers)
        {
            _prevMarkerPos = _map.Markers.Select(m => m.Position).ToList();
            GroundClampService.ClampGroundMarkers(_map);
        }
    }

    public void Undo()
    {
        if (_prevPropPos != null)
            for (int i = 0; i < _map.Props.Count && i < _prevPropPos.Count; i++)
                _map.Props[i].Position = _prevPropPos[i];
        if (_prevMarkerPos != null)
            for (int i = 0; i < _map.Markers.Count && i < _prevMarkerPos.Count; i++)
                _map.Markers[i].Position = _prevMarkerPos[i];
    }
}
