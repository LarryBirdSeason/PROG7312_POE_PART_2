using SmartX.Core.Models;

namespace SmartX.Api.Services;

public sealed class TelemetryHistory
{
    private const int MaxPointsPerDevice = 600;

    private readonly Dictionary<string, SortedDictionary<DateTimeOffset, double>> _series =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _gate = new();

    public void Add(string mac, DateTimeOffset at, double value)
    {
        lock (_gate)
        {
            if (!_series.TryGetValue(mac, out var sorted))
            {
                sorted = new SortedDictionary<DateTimeOffset, double>();
                _series[mac] = sorted;
            }

            while (sorted.ContainsKey(at)) at = at.AddTicks(1);
            sorted[at] = value;

            while (sorted.Count > MaxPointsPerDevice) sorted.Remove(sorted.Keys.First());
        }
    }

    public List<(DateTimeOffset At, double Value)> RecentPoints(string mac, int take)
    {
        lock (_gate)
        {
            if (!_series.TryGetValue(mac, out var sorted)) return new List<(DateTimeOffset At, double Value)>();

            var skip = Math.Max(0, sorted.Count - take);
            return sorted.Skip(skip).Select(pair => (pair.Key, pair.Value)).ToList();
        }
    }

    public List<HistoryPointDto> Recent(string mac, int take)
    {
        return RecentPoints(mac, take)
            .Select(p => new HistoryPointDto(p.At, p.Value))
            .ToList();
    }

    public List<double> RecentValues(string mac, int take)
    {
        return RecentPoints(mac, take).Select(p => p.Value).ToList();
    }
}
