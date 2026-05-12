using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Operations;

/// <summary>Add a prop to a map. Undo removes it.</summary>
public class AddPropOp : IMapOperation
{
    private readonly List<Prop> _props;
    private readonly Prop _prop;
    public string Description => "Add prop";
    public AddPropOp(List<Prop> props, Prop prop) { _props = props; _prop = prop; }
    public void Execute() => _props.Add(_prop);
    public void Undo() => _props.Remove(_prop);
}

/// <summary>Move a prop between two positions in a single undoable step (one per drag).</summary>
public class MovePropOp : IMapOperation
{
    private readonly Prop _prop;
    private readonly System.Numerics.Vector3 _from;
    private readonly System.Numerics.Vector3 _to;
    public string Description => "Move prop";
    public MovePropOp(Prop prop, System.Numerics.Vector3 from, System.Numerics.Vector3 to)
    {
        _prop = prop; _from = from; _to = to;
    }
    public void Execute() => _prop.Position = _to;
    public void Undo() => _prop.Position = _from;
}

/// <summary>Add a UnitSpawn to an army's INITIAL units list. Undo removes it.</summary>
public class AddUnitSpawnOp : IMapOperation
{
    private readonly List<UnitSpawn> _list;
    private readonly UnitSpawn _unit;
    public string Description => $"Add unit {_unit.BlueprintId}";
    public AddUnitSpawnOp(List<UnitSpawn> list, UnitSpawn unit) { _list = list; _unit = unit; }
    public void Execute() => _list.Add(_unit);
    public void Undo() => _list.Remove(_unit);
}

/// <summary>Move a pre-placed unit (UnitSpawn) between two positions in one undoable step.</summary>
public class MoveUnitSpawnOp : IMapOperation
{
    private readonly UnitSpawn _unit;
    private readonly System.Numerics.Vector3 _from;
    private readonly System.Numerics.Vector3 _to;
    public string Description => $"Move {_unit.Name}";
    public MoveUnitSpawnOp(UnitSpawn unit, System.Numerics.Vector3 from, System.Numerics.Vector3 to)
    {
        _unit = unit; _from = from; _to = to;
    }
    public void Execute() => _unit.Position = _to;
    public void Undo() => _unit.Position = _from;
}

/// <summary>Remove a prop. Undo re-inserts at the original index.</summary>
public class RemovePropOp : IMapOperation
{
    private readonly List<Prop> _props;
    private readonly Prop _prop;
    private readonly int _originalIndex;
    public string Description => "Delete prop";
    public RemovePropOp(List<Prop> props, Prop prop)
    {
        _props = props; _prop = prop; _originalIndex = props.IndexOf(prop);
    }
    public void Execute() => _props.Remove(_prop);
    public void Undo() => _props.Insert(Math.Clamp(_originalIndex, 0, _props.Count), _prop);
}

/// <summary>Remove a UnitSpawn from an army's INITIAL list. Undo re-inserts at the original index.</summary>
public class RemoveUnitSpawnOp : IMapOperation
{
    private readonly List<UnitSpawn> _list;
    private readonly UnitSpawn _unit;
    private readonly int _originalIndex;
    public string Description => $"Delete {_unit.Name}";
    public RemoveUnitSpawnOp(List<UnitSpawn> list, UnitSpawn unit)
    {
        _list = list; _unit = unit; _originalIndex = list.IndexOf(unit);
    }
    public void Execute() => _list.Remove(_unit);
    public void Undo() => _list.Insert(Math.Clamp(_originalIndex, 0, _list.Count), _unit);
}
