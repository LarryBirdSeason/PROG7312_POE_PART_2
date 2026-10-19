namespace SmartX.Core.Models;

public readonly struct TelemetryPacket<T> where T : struct
{
    public string DeviceId { get; }
    public string Metric { get; }
    public T Value { get; }
    public DateTimeOffset Timestamp { get; }

    public TelemetryPacket(string deviceId, string metric, T value, DateTimeOffset? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("Device id is required.", nameof(deviceId));
        if (string.IsNullOrWhiteSpace(metric))
            throw new ArgumentException("Metric name is required.", nameof(metric));

        DeviceId = deviceId;
        Metric = metric;
        Value = value;
        Timestamp = timestamp ?? DateTimeOffset.UtcNow;
    }

    public override string ToString() => $"[{Timestamp:HH:mm:ss}] {DeviceId}/{Metric} = {Value}";
}

public static class TelemetryPacket
{
    public static TelemetryPacket<T> Create<T>(string deviceId, string metric, T value) where T : struct
        => new(deviceId, metric, value);
}
