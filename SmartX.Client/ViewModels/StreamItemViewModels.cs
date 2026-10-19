using System.Globalization;
using System.Linq;
using System.Windows.Media;
using SmartX.Core.Models;

namespace SmartX.Client.ViewModels;

public static class Palette
{
    public static readonly Brush Red = Make(0xEF, 0x44, 0x44);
    public static readonly Brush Amber = Make(0xF5, 0x9E, 0x0B);
    public static readonly Brush Green = Make(0x22, 0xC5, 0x5E);
    public static readonly Brush Blue = Make(0x38, 0xBD, 0xF8);
    public static readonly Brush Grey = Make(0x64, 0x74, 0x8B);

    private static Brush Make(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

public sealed class DeviceItemViewModel : ObservableObject
{
    private DeviceDto _dto;
    private bool _isFlagged;

    public DeviceItemViewModel(DeviceDto dto)
    {
        _dto = dto;
    }

    public void Update(DeviceDto dto)
    {
        _dto = dto;
        OnPropertyChanged(string.Empty);
    }

    public string Mac => _dto.Mac;
    public string Name => _dto.Name;
    public string Kind => _dto.Kind;
    public string Location => _dto.Location;
    public string CategoryText => _dto.Category.ToString();
    public bool IsActuator => _dto.Category == SensorCategory.Actuator;
    public string Summary => $"{_dto.Kind}  ·  {_dto.Mac}";

    public string AlertText =>
        _dto.Connection == ConnectionState.Offline ? "No telemetry received" : _dto.AlertText;

    public bool IsFlagged
    {
        get => _isFlagged;
        set => SetField(ref _isFlagged, value);
    }

    public string ValueText
    {
        get
        {
            if (!_dto.HasReading) return "no data";

            if (IsActuator || _dto.DataType == ValueKind.Bool)
                return _dto.LastValue >= 0.5 ? "ON" : "OFF";

            var format = _dto.DataType == ValueKind.Int ? "0" : "0.0";
            return $"{_dto.LastValue.ToString(format, CultureInfo.InvariantCulture)} {_dto.Unit}".Trim();
        }
    }

    public string BadgeText
    {
        get
        {
            if (_dto.Connection == ConnectionState.Offline) return "OFFLINE";
            if (_dto.Severity == Severity.Critical) return "CRITICAL";
            if (_dto.Severity == Severity.Warning) return "WARNING";
            if (_dto.Connection == ConnectionState.Stale) return "STALE";
            return "OK";
        }
    }

    public Brush BadgeBrush
    {
        get
        {
            if (_dto.Connection == ConnectionState.Offline) return Palette.Grey;
            if (_dto.Severity == Severity.Critical) return Palette.Red;
            if (_dto.Severity == Severity.Warning || _dto.Connection == ConnectionState.Stale) return Palette.Amber;
            return Palette.Green;
        }
    }

    public Brush StatusBrush => BadgeBrush;
}

public sealed class InsightItemViewModel : ObservableObject
{
    private InsightDto _dto;

    public InsightItemViewModel(InsightDto dto)
    {
        _dto = dto;
    }

    public void Update(InsightDto dto)
    {
        _dto = dto;
        OnPropertyChanged(string.Empty);
    }

    public string Id => _dto.Id;
    public string Title => _dto.Title;
    public string Detail => _dto.Detail;
    public string? Mac => _dto.Mac;
    public CommandAction? Action => _dto.SuggestedAction;
    public bool HasAction => _dto.SuggestedAction.HasValue;
    public bool HasDevice => !string.IsNullOrEmpty(_dto.Mac);
    public string KindText => _dto.Kind.ToString().ToUpperInvariant();

    public string ApplyLabel => _dto.SuggestedAction switch
    {
        CommandAction.TurnOn => "Apply: turn ON",
        CommandAction.TurnOff => "Apply: turn OFF",
        CommandAction.Toggle => "Apply: toggle",
        _ => "Apply"
    };

    public string ConfidenceText => _dto.Kind == InsightKind.Fault
        ? string.Empty
        : (_dto.Confidence * 100).ToString("0", CultureInfo.InvariantCulture) + "% confidence";

    public Brush AccentBrush => _dto.Severity switch
    {
        Severity.Critical => Palette.Red,
        Severity.Warning => Palette.Amber,
        _ => Palette.Blue
    };
}

public sealed class AlertItemViewModel
{
    public AlertItemViewModel(AlertDto alert)
    {
        Key = alert.Key;
        Title = $"{alert.DeviceName}  ·  {alert.Type}";
        Message = alert.Message;
        TimeText = alert.RaisedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        Brush = alert.Severity == Severity.Critical ? Palette.Red : Palette.Amber;
    }

    public string Key { get; }
    public string Title { get; }
    public string Message { get; }
    public string TimeText { get; }
    public Brush Brush { get; }
}

public sealed class CommandItemViewModel
{
    public CommandItemViewModel(CommandRecordDto record)
    {
        Id = record.Id;
        CanUndo = record.CanUndo;
        TimeText = record.IssuedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        Description = record.Description;

        Detail = string.Join(", ", record.Changes.Select(c =>
            c.Name + ": " + (c.PreviousOn ? "ON" : "OFF") + " -> " + (c.NewOn ? "ON" : "OFF")));

        StatusText = record.IsUndo ? "UNDO" : record.CanUndo ? "ACTIVE" : "UNDONE";
        StatusBrush = record.IsUndo ? Palette.Blue : record.CanUndo ? Palette.Green : Palette.Grey;
    }

    public Guid Id { get; }
    public bool CanUndo { get; }
    public string TimeText { get; }
    public string Description { get; }
    public string Detail { get; }
    public string StatusText { get; }
    public Brush StatusBrush { get; }
}
