using SmartX.Core.Models;

namespace SmartX.Api.Services;

public sealed class AlertTracker
{
    public static readonly string[] SeverityTypes = { "CriticalLow", "CriticalHigh", "WarningLow", "WarningHigh" };

    private const int MaxLog = 100;

    private readonly HashSet<string> _active = new();
    private readonly Dictionary<string, AlertDto> _details = new();
    private readonly Queue<AlertDto> _log = new();
    private readonly object _gate = new();
    private long _duplicatesSuppressed;

    public static string KeyFor(string mac, string type) => $"{mac}|{type}";

    public int Count
    {
        get
        {
            lock (_gate) return _active.Count;
        }
    }

    public long DuplicatesSuppressed
    {
        get
        {
            lock (_gate) return _duplicatesSuppressed;
        }
    }

    public bool TryRaise(AlertDto alert)
    {
        lock (_gate)
        {
            if (!_active.Add(alert.Key))
            {
                _duplicatesSuppressed++;
                return false;
            }

            _details[alert.Key] = alert;
            _log.Enqueue(alert);
            while (_log.Count > MaxLog) _log.Dequeue();
            return true;
        }
    }

    public bool Clear(string key)
    {
        lock (_gate)
        {
            if (!_active.Remove(key)) return false;
            _details.Remove(key);
            return true;
        }
    }

    public bool IsActive(string key)
    {
        lock (_gate) return _active.Contains(key);
    }

    public List<AlertDto> Active()
    {
        lock (_gate)
        {
            return _active
                .Select(key => _details[key])
                .OrderByDescending(a => a.Severity)
                .ThenByDescending(a => a.RaisedAt)
                .ToList();
        }
    }
}
