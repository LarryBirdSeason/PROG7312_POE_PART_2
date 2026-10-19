namespace SmartX.Core.Models;

public readonly struct SensorReading : IEquatable<SensorReading>
{
    public string DeviceId { get; }
    public double Value { get; }
    public string Unit { get; }
    public DateTimeOffset Timestamp { get; }

    public SensorReading(string deviceId, double value, string unit, DateTimeOffset? timestamp = null)
    {
        DeviceId = deviceId;
        Value = value;
        Unit = unit;
        Timestamp = timestamp ?? DateTimeOffset.UtcNow;
    }

    public static SensorReading operator +(SensorReading a, SensorReading b)
    {
        RequireCompatible(a, b);
        return new SensorReading($"{a.DeviceId}+{b.DeviceId}", a.Value + b.Value, a.Unit);
    }

    public static SensorReading operator -(SensorReading a, SensorReading b)
    {
        RequireCompatible(a, b);
        return new SensorReading($"{a.DeviceId}-{b.DeviceId}", a.Value - b.Value, a.Unit);
    }

    public static bool operator >(SensorReading a, SensorReading b) => a.Value > b.Value;
    public static bool operator <(SensorReading a, SensorReading b) => a.Value < b.Value;
    public static bool operator >=(SensorReading a, SensorReading b) => a.Value >= b.Value;
    public static bool operator <=(SensorReading a, SensorReading b) => a.Value <= b.Value;

    public static bool operator ==(SensorReading a, SensorReading b) => a.Equals(b);
    public static bool operator !=(SensorReading a, SensorReading b) => !a.Equals(b);

    public bool Equals(SensorReading other)
        => DeviceId == other.DeviceId && Value.Equals(other.Value) && Unit == other.Unit;

    public override bool Equals(object? obj) => obj is SensorReading other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DeviceId, Value, Unit);

    private static void RequireCompatible(SensorReading a, SensorReading b)
    {
        if (a.Unit != b.Unit)
            throw new InvalidOperationException(
                $"Cannot combine readings reported in different units: '{a.Unit}' vs '{b.Unit}'.");
    }

    public override string ToString() => $"{DeviceId}: {Value:0.##} {Unit}";
}
