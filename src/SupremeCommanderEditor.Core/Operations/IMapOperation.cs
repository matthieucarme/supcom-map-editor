namespace SupremeCommanderEditor.Core.Operations;

public interface IMapOperation
{
    string Description { get; }
    void Execute();
    void Undo();
}
