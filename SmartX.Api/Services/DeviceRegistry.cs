using SmartX.Core.Models;
using SmartX.Core.Services;

namespace SmartX.Api.Services;

public sealed class DeviceState
{
    public string Mac { get; init; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public SensorCategory Category { get; set; }
    public double LastValue { get; set; }
    public string Unit { get; set; } = string.Empty;
    public ValueKind DataType { get; set; }
    public bool HasReading { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public Severity Severity { get; set; }
    public string AlertText { get; set; } = string.Empty;
    public bool? CommandedOn { get; set; }
}

public sealed record ReadingOutcome(string Mac, string Name, Evaluation Evaluation, bool WasOffline);

public sealed class DeviceRegistry
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan OfflineAfter = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, DeviceState> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<SensorCategory, HashSet<string>> _byCategory = new();
    private readonly object _gate = new();

    public int Count
    {
        get
        {
            lock (_gate) return _devices.Count;
        }
    }

    public DeviceDto Register(SensorRegistration registration)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;

            if (!_devices.TryGetValue(registration.MacAddress, out var device))
            {
                device = new DeviceState { Mac = registration.MacAddress };
                _devices[registration.MacAddress] = device;
            }
            else
            {
                RemoveFromIndexLocked(device);
            }

            device.Name = string.IsNullOrWhiteSpace(registration.DisplayName)
                ? registration.MacAddress
                : registration.DisplayName.Trim();
            device.Location = registration.Location;
            device.Category = registration.Category;
            device.Kind = string.IsNullOrWhiteSpace(registration.DeviceKind)
                ? ThresholdRules.DefaultKind(registration.Category)
                : registration.DeviceKind.Trim();
            device.Unit = ThresholdRules.ForKind(device.Kind).Unit;
            device.LastSeen = now;

            AddToIndexLocked(device);
            return ToDto(device, now);
        }
    }

    public string ResolveKind(string mac, string metric)
    {
        lock (_gate)
        {
            if (_devices.TryGetValue(mac, out var device)) return device.Kind;
            return ThresholdRules.InferKind(ThresholdRules.InferCategory(metric), metric);
        }
    }

    public bool? GetCommandedState(string mac)
    {
        lock (_gate)
        {
            return _devices.TryGetValue(mac, out var device) ? device.CommandedOn : null;
        }
    }

    public bool SetCommanded(string mac, bool on)
    {
        lock (_gate)
        {
            if (!_devices.TryGetValue(mac, out var device)) return false;
            device.CommandedOn = on;
            return true;
        }
    }

    public ReadingOutcome ApplyReading(TelemetryMessage message)
    {
        lock (_gate)
        {
            var device = GetOrCreateLocked(message.DeviceId, message.Metric);
            var wasOffline = (message.ReceivedAt - device.LastSeen) >= OfflineAfter;

            device.LastValue = message.Value;
            device.DataType = message.Kind;
            device.HasReading = true;
            device.LastSeen = message.ReceivedAt;

            var evaluation = device.Category == SensorCategory.Actuator || message.Kind == ValueKind.Bool
                ? Evaluation.Normal
                : ThresholdRules.Evaluate(device.Kind, message.Value);

            device.Severity = evaluation.Severity;
            device.AlertText = evaluation.Message;

            return new ReadingOutcome(device.Mac, device.Name, evaluation, wasOffline);
        }
    }

    public string NameOf(string mac)
    {
        lock (_gate)
        {
            return _devices.TryGetValue(mac, out var device) ? device.Name : mac;
        }
    }

    public DeviceDto? Get(string mac)
    {
        lock (_gate)
        {
            return _devices.TryGetValue(mac, out var device) ? ToDto(device, DateTimeOffset.UtcNow) : null;
        }
    }

    public List<DeviceDto> Snapshot()
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            return _devices.Values.Select(d => ToDto(d, now)).ToList();
        }
    }

    public List<DeviceDto> ByKind(string kind)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            return _devices.Values
                .Where(d => string.Equals(d.Kind, kind, StringComparison.OrdinalIgnoreCase))
                .Select(d => ToDto(d, now))
                .ToList();
        }
    }

    public List<(string Mac, string Name, DateTimeOffset LastSeen)> OfflineDevices(DateTimeOffset now)
    {
        lock (_gate)
        {
            var list = new List<(string Mac, string Name, DateTimeOffset LastSeen)>();
            foreach (var device in _devices.Values)
            {
                if (now - device.LastSeen >= OfflineAfter)
                    list.Add((device.Mac, device.Name, device.LastSeen));
            }

            return list;
        }
    }

    public List<SensorReading> PowerReadings()
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var list = new List<SensorReading>();

            foreach (var device in _devices.Values)
            {
                if (device.Kind == "Power" && device.HasReading && now - device.LastSeen < OfflineAfter)
                    list.Add(new SensorReading(device.Mac, device.LastValue, "W"));
            }

            return list;
        }
    }

    public List<DeviceDto> Query(string? search, SensorCategory? category, string? alertState)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var term = search?.Trim();

            IEnumerable<DeviceState> candidates;
            if (category is { } selected)
            {
                candidates = _byCategory.TryGetValue(selected, out var members)
                    ? members.Select(mac => _devices[mac])
                    : Enumerable.Empty<DeviceState>();
            }
            else
            {
                candidates = _devices.Values;
            }

            var result = new List<DeviceDto>();
            foreach (var device in candidates)
            {
                if (!string.IsNullOrEmpty(term) && !Matches(device, term)) continue;

                var dto = ToDto(device, now);
                if (!PassesAlertFilter(dto, alertState)) continue;

                result.Add(dto);
            }

            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return result;
        }
    }

    private static bool Matches(DeviceState device, string term)
    {
        var haystack = $"{device.Name} {device.Kind} {device.Category} {device.Location} {device.Mac}";
        return haystack.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PassesAlertFilter(DeviceDto device, string? state)
    {
        if (string.IsNullOrWhiteSpace(state) || state.Equals("All", StringComparison.OrdinalIgnoreCase))
            return true;

        var offline = device.Connection == ConnectionState.Offline;

        return state.ToLowerInvariant() switch
        {
            "alerting" => offline || device.Severity != Severity.Normal,
            "critical" => !offline && device.Severity == Severity.Critical,
            "warning" => !offline && device.Severity == Severity.Warning,
            "offline" => offline,
            "normal" => !offline && device.Severity == Severity.Normal,
            _ => true
        };
    }

    private DeviceState GetOrCreateLocked(string mac, string metric)
    {
        if (_devices.TryGetValue(mac, out var existing)) return existing;

        var category = ThresholdRules.InferCategory(metric);
        var kind = ThresholdRules.InferKind(category, metric);

        var device = new DeviceState
        {
            Mac = mac,
            Name = mac,
            Category = category,
            Kind = kind,
            Location = "Unassigned",
            Unit = ThresholdRules.ForKind(kind).Unit,
            LastSeen = DateTimeOffset.UtcNow
        };

        _devices[mac] = device;
        AddToIndexLocked(device);
        return device;
    }

    private void AddToIndexLocked(DeviceState device)
    {
        if (!_byCategory.TryGetValue(device.Category, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _byCategory[device.Category] = set;
        }

        set.Add(device.Mac);
    }

    private void RemoveFromIndexLocked(DeviceState device)
    {
        if (_byCategory.TryGetValue(device.Category, out var set)) set.Remove(device.Mac);
    }

    private static DeviceDto ToDto(DeviceState device, DateTimeOffset now)
    {
        var age = now - device.LastSeen;
        var connection = age >= OfflineAfter
            ? ConnectionState.Offline
            : age >= StaleAfter ? ConnectionState.Stale : ConnectionState.Online;

        return new DeviceDto(
            device.Mac, device.Name, device.Kind, device.Location, device.Category,
            device.LastValue, device.Unit, device.DataType, device.HasReading, device.LastSeen,
            connection, device.Severity, device.CommandedOn, device.AlertText);
    }
}
