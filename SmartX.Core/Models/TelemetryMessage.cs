namespace SmartX.Core.Models;

public readonly record struct TelemetryMessage(
    string DeviceId,
    string Metric,
    double Value,
    ValueKind Kind,
    DateTimeOffset ReceivedAt,
    bool IsDisconnect = false);
