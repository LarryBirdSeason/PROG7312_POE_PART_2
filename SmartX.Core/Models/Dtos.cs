namespace SmartX.Core.Models;

public sealed record DeviceDto(
    string Mac,
    string Name,
    string Kind,
    string Location,
    SensorCategory Category,
    double LastValue,
    string Unit,
    ValueKind DataType,
    bool HasReading,
    DateTimeOffset LastSeen,
    ConnectionState Connection,
    Severity Severity,
    bool? CommandedOn,
    string AlertText);

public sealed record HistoryPointDto(DateTimeOffset Timestamp, double Value);

public sealed record CommandChangeDto(string Mac, string Name, bool PreviousOn, bool NewOn);

public sealed record CommandRecordDto(
    Guid Id,
    string Description,
    DateTimeOffset IssuedAt,
    bool IsUndo,
    bool CanUndo,
    List<CommandChangeDto> Changes);

public sealed record AlertDto(
    string Key,
    string Mac,
    string DeviceName,
    string Type,
    Severity Severity,
    string Message,
    DateTimeOffset RaisedAt);

public sealed record InsightDto(
    string Id,
    InsightKind Kind,
    Severity Severity,
    string Title,
    string Detail,
    string? Mac,
    CommandAction? SuggestedAction,
    double Confidence);

public sealed record PatternDto(string Description, int Support, double Confidence);

public sealed record PipelineStatsDto(
    int StandardDepth,
    int PriorityDepth,
    long StandardProcessed,
    long PriorityProcessed,
    int PeakStandardDepth,
    int PeakPriorityDepth,
    double StandardAvgLatencyMs,
    double PriorityAvgLatencyMs,
    long TotalReceived,
    long Rejected,
    int RegisteredDevices,
    int ActiveAlerts,
    long DuplicateAlertsSuppressed,
    int UndoDepth,
    double AggregateLoadWatts);

public sealed record IssueCommandRequest(string Mac, CommandAction Action);

public sealed record BulkCommandRequest(string Kind, CommandAction Action);

public sealed record InsightEventRequest(string Kind, string? Mac, string? Detail);

public sealed record CommandResultDto(bool Success, string Message, CommandRecordDto? Record);

public sealed record TelemetryAckDto(bool Accepted, string Queue, bool? DesiredState);
