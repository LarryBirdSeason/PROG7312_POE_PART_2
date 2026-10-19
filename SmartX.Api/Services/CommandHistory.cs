using SmartX.Core.Models;

namespace SmartX.Api.Services;

public sealed class CommandRecord
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Description { get; init; } = string.Empty;
    public DateTimeOffset IssuedAt { get; init; } = DateTimeOffset.UtcNow;
    public bool IsUndo { get; init; }
    public bool Undone { get; set; }
    public List<CommandChangeDto> Changes { get; init; } = new();

    public CommandRecordDto ToDto() => new(Id, Description, IssuedAt, IsUndo, !IsUndo && !Undone, Changes);
}

public sealed class CommandHistory
{
    private const int MaxLog = 200;

    private readonly Stack<CommandRecord> _undoStack = new();
    private readonly List<CommandRecord> _log = new();
    private readonly object _gate = new();

    public int UndoDepth
    {
        get
        {
            lock (_gate) return _undoStack.Count;
        }
    }

    public void PushUndoable(CommandRecord record)
    {
        lock (_gate)
        {
            _undoStack.Push(record);
            AppendLocked(record);
        }
    }

    public void AppendLog(CommandRecord record)
    {
        lock (_gate) AppendLocked(record);
    }

    public CommandRecord? PopForUndo()
    {
        lock (_gate)
        {
            return _undoStack.Count > 0 ? _undoStack.Pop() : null;
        }
    }

    public List<CommandRecordDto> Snapshot(int take = 50)
    {
        lock (_gate)
        {
            var list = new List<CommandRecordDto>();
            for (int i = _log.Count - 1; i >= 0 && list.Count < take; i--)
                list.Add(_log[i].ToDto());

            return list;
        }
    }

    private void AppendLocked(CommandRecord record)
    {
        _log.Add(record);
        if (_log.Count > MaxLog) _log.RemoveAt(0);
    }
}
