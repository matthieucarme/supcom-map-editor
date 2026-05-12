using System.Numerics;
using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Operations;

/// <summary>Add a marker to a map's marker list. Undo removes it.</summary>
public class AddMarkerOp : IMapOperation
{
    private readonly List<Marker> _markers;
    private readonly Marker _marker;
    private int _index;

    public string Description => $"Add marker {_marker.Name}";

    public AddMarkerOp(List<Marker> markers, Marker marker)
    {
        _markers = markers;
        _marker = marker;
        _index = -1;
    }

    public void Execute()
    {
        if (_index < 0) _index = _markers.Count;
        _markers.Insert(Math.Min(_index, _markers.Count), _marker);
    }

    public void Undo()
    {
        _markers.Remove(_marker);
    }
}

/// <summary>Remove a marker from a map's marker list. Undo re-inserts at the original index.</summary>
public class RemoveMarkerOp : IMapOperation
{
    private readonly List<Marker> _markers;
    private readonly Marker _marker;
    private readonly int _originalIndex;

    public string Description => $"Delete marker {_marker.Name}";

    public RemoveMarkerOp(List<Marker> markers, Marker marker)
    {
        _markers = markers;
        _marker = marker;
        _originalIndex = markers.IndexOf(marker);
    }

    public void Execute()
    {
        _markers.Remove(_marker);
    }

    public void Undo()
    {
        int idx = Math.Clamp(_originalIndex, 0, _markers.Count);
        _markers.Insert(idx, _marker);
    }
}

/// <summary>Move a marker between two positions in a single undoable step (one per drag).</summary>
public class MoveMarkerOp : IMapOperation
{
    private readonly Marker _marker;
    private readonly Vector3 _from;
    private readonly Vector3 _to;

    public string Description => $"Move {_marker.Name}";

    public MoveMarkerOp(Marker marker, Vector3 from, Vector3 to)
    {
        _marker = marker;
        _from = from;
        _to = to;
    }

    public void Execute() => _marker.Position = _to;
    public void Undo() => _marker.Position = _from;
}
