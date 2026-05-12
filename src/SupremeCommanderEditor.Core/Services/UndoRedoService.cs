using SupremeCommanderEditor.Core.Operations;

namespace SupremeCommanderEditor.Core.Services;

public class UndoRedoService
{
    private readonly Stack<IMapOperation> _undoStack = new();
    private readonly Stack<IMapOperation> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public string? UndoDescription => _undoStack.TryPeek(out var op) ? op.Description : null;
    public string? RedoDescription => _redoStack.TryPeek(out var op) ? op.Description : null;

    public event Action? StateChanged;

    public void Execute(IMapOperation operation)
    {
        operation.Execute();
        _undoStack.Push(operation);
        _redoStack.Clear();
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Push an already-executed operation (e.g. from a brush stroke that applied incrementally).
    /// </summary>
    public void PushExecuted(IMapOperation operation)
    {
        _undoStack.Push(operation);
        _redoStack.Clear();
        StateChanged?.Invoke();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var op = _undoStack.Pop();
        op.Undo();
        _redoStack.Push(op);
        StateChanged?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var op = _redoStack.Pop();
        op.Execute();
        _undoStack.Push(op);
        StateChanged?.Invoke();
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        StateChanged?.Invoke();
    }
}
