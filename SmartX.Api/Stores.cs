using System.Collections.Concurrent;
using SmartX.Core.Models;

namespace SmartX.Api;

public class SensorStore
{
    private readonly ConcurrentDictionary<Guid, SensorRegistration> _sensors = new();

    public SensorRegistration Add(SensorRegistration reg)
    {
        _sensors[reg.Id] = reg;
        return reg;
    }

    public SensorRegistration? Get(Guid id) => _sensors.TryGetValue(id, out var s) ? s : null;

    public IEnumerable<SensorRegistration> All() => _sensors.Values.OrderBy(s => s.RegisteredAt);
}

public class TelemetryStore
{
    private readonly ConcurrentDictionary<string, List<SensorReading>> _series = new();
    private readonly object _lock = new();

    public void Add(SensorReading reading)
    {
        lock (_lock)
        {
            var list = _series.GetOrAdd(reading.DeviceId, _ => new List<SensorReading>());
            list.Add(reading);
            const int maxPerDevice = 500;
            if (list.Count > maxPerDevice)
                list.RemoveRange(0, list.Count - maxPerDevice);
        }
    }

    public IReadOnlyList<SensorReading> Recent(string deviceId, int take = 100)
    {
        lock (_lock)
        {
            if (!_series.TryGetValue(deviceId, out var list)) return Array.Empty<SensorReading>();
            return list.Skip(Math.Max(0, list.Count - take)).ToList();
        }
    }
}
