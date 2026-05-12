using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Operations;

/// <summary>
/// Generic batch removal: snapshots each item's original index so Undo restores positional order.
/// Used for any list&lt;T&gt; — props, markers, decals, etc.
/// </summary>
public class BatchRemoveOp<T> : IMapOperation where T : class
{
    private readonly List<T> _list;
    private readonly List<(int index, T item)> _removed;
    private readonly string _label;

    public string Description => $"Delete {_removed.Count} {_label}";

    public BatchRemoveOp(List<T> list, IEnumerable<T> toRemove, string label = "items")
    {
        _list = list;
        _label = label;
        // Resolve indices BEFORE any mutation. Skip items not in the list (caller might double-select).
        _removed = toRemove
            .Select(x => (index: list.IndexOf(x), item: x))
            .Where(x => x.index >= 0)
            .OrderBy(x => x.index)
            .ToList();
    }

    public void Execute()
    {
        // Remove from the end so earlier indices stay valid as the list shrinks.
        for (int i = _removed.Count - 1; i >= 0; i--)
            _list.Remove(_removed[i].item);
    }

    public void Undo()
    {
        // Re-insert in original index order; later entries' indices were captured assuming earlier
        // ones are already back, so this restores positional order exactly.
        foreach (var (idx, item) in _removed)
            _list.Insert(Math.Clamp(idx, 0, _list.Count), item);
    }
}

/// <summary>
/// Move an arbitrary set of entities in one undoable step. Each move is described by a setter
/// closure plus its from/to positions, so the op is polymorphic — works for props, markers, and
/// unit spawns equally without a generic type parameter.
/// </summary>
public class BatchMoveOp : IMapOperation
{
    private readonly List<(Action<System.Numerics.Vector3> apply, System.Numerics.Vector3 from, System.Numerics.Vector3 to)> _moves;

    public string Description => $"Move {_moves.Count} elements";

    public BatchMoveOp(IEnumerable<(Action<System.Numerics.Vector3> apply, System.Numerics.Vector3 from, System.Numerics.Vector3 to)> moves)
    {
        _moves = moves.ToList();
    }

    public void Execute() { foreach (var (apply, _, to) in _moves) apply(to); }
    public void Undo()    { foreach (var (apply, from, _) in _moves) apply(from); }
}

/// <summary>Add a batch of props (e.g. a brush stroke) in one undoable step.</summary>
public class BatchAddPropsOp : IMapOperation
{
    private readonly List<Prop> _props;
    private readonly List<Prop> _added;

    public string Description => $"Add {_added.Count} props";

    public BatchAddPropsOp(List<Prop> allProps, IEnumerable<Prop> toAdd)
    {
        _props = allProps;
        _added = toAdd.ToList();
    }

    public void Execute() { foreach (var p in _added) _props.Add(p); }
    public void Undo() { foreach (var p in _added) _props.Remove(p); }
}

/// <summary>
/// Backwards-compat alias used by the prop-brush stroke and box-delete code paths. Equivalent
/// to <c>new BatchRemoveOp&lt;Prop&gt;(list, items, "props")</c>; kept as a named class so call
/// sites stay readable.
/// </summary>
public class BatchRemovePropsOp : BatchRemoveOp<Prop>
{
    public BatchRemovePropsOp(List<Prop> allProps, IEnumerable<Prop> toRemove)
        : base(allProps, toRemove, "props") { }
}

/// <summary>
/// Delete a batch of UnitSpawns spread across multiple armies in one undoable step. Captures
/// the source army for each unit so Undo restores ownership and per-army index.
/// </summary>
public class BatchRemoveUnitSpawnsOp : IMapOperation
{
    private readonly List<(Army army, int index, UnitSpawn unit)> _removed;

    public string Description => $"Delete {_removed.Count} units";

    public BatchRemoveUnitSpawnsOp(IEnumerable<(Army army, UnitSpawn unit)> items)
    {
        _removed = items
            .Select(x => (army: x.army, index: x.army.InitialUnits.IndexOf(x.unit), unit: x.unit))
            .Where(x => x.index >= 0)
            .OrderBy(x => x.index)
            .ToList();
    }

    public void Execute()
    {
        // Group by army and remove from the end of each list so indices stay valid.
        foreach (var group in _removed.GroupBy(x => x.army))
            foreach (var entry in group.OrderByDescending(x => x.index))
                entry.army.InitialUnits.Remove(entry.unit);
    }

    public void Undo()
    {
        foreach (var (army, idx, unit) in _removed)
            army.InitialUnits.Insert(Math.Clamp(idx, 0, army.InitialUnits.Count), unit);
    }
}
