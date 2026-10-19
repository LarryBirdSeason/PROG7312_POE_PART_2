using System.Collections.Concurrent;

namespace SmartX.Simulator;

internal sealed class World
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _activeUntil = new();
    private readonly ConcurrentDictionary<string, bool> _commanded = new();

    public void Trigger(string scenario, string mac, int seconds)
    {
        _activeUntil[$"{scenario}:{mac}"] = DateTimeOffset.UtcNow.AddSeconds(seconds);
    }

    public bool Active(string scenario, string mac)
    {
        return _activeUntil.TryGetValue($"{scenario}:{mac}", out var until) && until > DateTimeOffset.UtcNow;
    }

    public void SetCommanded(string mac, bool on)
    {
        _commanded[mac] = on;
    }

    public bool IsOn(string mac, bool defaultOn)
    {
        return _commanded.TryGetValue(mac, out var on) ? on : defaultOn;
    }
}

internal static class Stats
{
    public static long Sent;
    public static long Failed;
}
