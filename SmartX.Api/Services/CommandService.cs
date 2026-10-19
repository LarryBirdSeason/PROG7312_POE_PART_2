using SmartX.Core.Models;
using SmartX.Core.Services;

namespace SmartX.Api.Services;

public sealed class CommandService
{
    private readonly DeviceRegistry _registry;
    private readonly CommandHistory _history;
    private readonly ActionPredictor _predictor;

    public CommandService(DeviceRegistry registry, CommandHistory history, ActionPredictor predictor)
    {
        _registry = registry;
        _history = history;
        _predictor = predictor;
    }

    public static bool IsOn(DeviceDto device) => device.CommandedOn ?? (device.HasReading && device.LastValue >= 0.5);

    public CommandResultDto Execute(string mac, CommandAction action)
    {
        var device = _registry.Get(mac);
        if (device is null) return new CommandResultDto(false, $"Unknown device {mac}.", null);

        if (device.Category != SensorCategory.Actuator)
            return new CommandResultDto(false, $"{device.Name} is not an actuator.", null);

        var current = IsOn(device);
        var target = Resolve(action, current);

        if (target == current)
            return new CommandResultDto(true, $"{device.Name} is already {StateWord(target)}.", null);

        _registry.SetCommanded(device.Mac, target);

        var record = new CommandRecord
        {
            Description = $"{device.Name} {StateWord(target)}",
            Changes = { new CommandChangeDto(device.Mac, device.Name, current, target) }
        };

        _history.PushUndoable(record);
        _predictor.Record(ActionKey.Command(device.Mac, target ? CommandAction.TurnOn : CommandAction.TurnOff));

        return new CommandResultDto(true, $"{record.Description}.", record.ToDto());
    }

    public CommandResultDto ExecuteBulk(string kind, CommandAction action)
    {
        var targets = _registry.ByKind(kind)
            .Where(d => d.Category == SensorCategory.Actuator)
            .ToList();

        if (targets.Count == 0)
            return new CommandResultDto(false, $"No {kind.ToLowerInvariant()} devices are registered.", null);

        var changes = new List<CommandChangeDto>();
        foreach (var device in targets)
        {
            var current = IsOn(device);
            var target = Resolve(action, current);
            if (target == current) continue;

            _registry.SetCommanded(device.Mac, target);
            changes.Add(new CommandChangeDto(device.Mac, device.Name, current, target));
            _predictor.Record(ActionKey.Command(device.Mac, target ? CommandAction.TurnOn : CommandAction.TurnOff));
        }

        if (changes.Count == 0)
            return new CommandResultDto(true, $"Every {kind.ToLowerInvariant()} is already in that state.", null);

        var record = new CommandRecord
        {
            Description = $"{BulkVerb(action)} all {kind.ToLowerInvariant()}s ({changes.Count})",
            Changes = changes
        };

        _history.PushUndoable(record);
        return new CommandResultDto(true, $"{record.Description}.", record.ToDto());
    }

    public CommandResultDto Undo()
    {
        var original = _history.PopForUndo();
        if (original is null) return new CommandResultDto(false, "Nothing to undo.", null);

        foreach (var change in original.Changes)
            _registry.SetCommanded(change.Mac, change.PreviousOn);

        original.Undone = true;

        var undo = new CommandRecord
        {
            Description = $"Undo: {original.Description}",
            IsUndo = true,
            Changes = original.Changes
                .Select(c => new CommandChangeDto(c.Mac, c.Name, c.NewOn, c.PreviousOn))
                .ToList()
        };

        _history.AppendLog(undo);
        return new CommandResultDto(true, $"{undo.Description}.", undo.ToDto());
    }

    private static bool Resolve(CommandAction action, bool current) => action switch
    {
        CommandAction.TurnOn => true,
        CommandAction.TurnOff => false,
        _ => !current
    };

    private static string StateWord(bool on) => on ? "turned ON" : "turned OFF";

    private static string BulkVerb(CommandAction action) => action switch
    {
        CommandAction.TurnOff => "Shut down",
        CommandAction.TurnOn => "Start",
        _ => "Toggle"
    };
}
