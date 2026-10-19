using SmartX.Core.Models;

namespace SmartX.Core.Services;

public static class ActionKey
{
    public static string Alert(string mac, string type) => $"alert|{mac}|{type}";

    public static string Search(string mac) => $"search|{mac}|";

    public static string Command(string mac, CommandAction action) => $"cmd|{mac}|{action}";

    public static bool TryParse(string key, out string kind, out string mac, out string detail)
    {
        var parts = key.Split('|');
        if (parts.Length >= 3)
        {
            kind = parts[0];
            mac = parts[1];
            detail = parts[2];
            return true;
        }

        kind = string.Empty;
        mac = string.Empty;
        detail = string.Empty;
        return false;
    }
}

public readonly record struct Prediction(string TriggerKey, string NextKey, int Support, double Confidence);

public sealed class ActionPredictor
{
    private const int MaxRecent = 128;

    private readonly Dictionary<string, Dictionary<string, int>> _transitions = new();
    private readonly Dictionary<string, int> _occurrences = new();
    private readonly List<(string Key, DateTimeOffset At)> _recent = new();
    private readonly object _gate = new();
    private readonly TimeSpan _window;

    public ActionPredictor(TimeSpan window)
    {
        _window = window;
    }

    public void Record(string eventKey, DateTimeOffset? at = null)
    {
        var when = at ?? DateTimeOffset.UtcNow;

        lock (_gate)
        {
            var antecedents = new HashSet<string>();
            foreach (var earlier in _recent)
            {
                var delta = when - earlier.At;
                if (delta > TimeSpan.Zero && delta <= _window && earlier.Key != eventKey)
                    antecedents.Add(earlier.Key);
            }

            foreach (var antecedent in antecedents)
            {
                if (!_transitions.TryGetValue(antecedent, out var nexts))
                {
                    nexts = new Dictionary<string, int>();
                    _transitions[antecedent] = nexts;
                }

                nexts[eventKey] = nexts.TryGetValue(eventKey, out var count) ? count + 1 : 1;
            }

            _occurrences[eventKey] = _occurrences.TryGetValue(eventKey, out var seen) ? seen + 1 : 1;

            _recent.Add((eventKey, when));
            if (_recent.Count > MaxRecent) _recent.RemoveAt(0);
        }
    }

    public List<Prediction> Predict(int minSupport = 2, double minConfidence = 0.5)
    {
        var now = DateTimeOffset.UtcNow;
        var best = new Dictionary<string, Prediction>();

        lock (_gate)
        {
            foreach (var trigger in _recent)
            {
                if (now - trigger.At > _window) continue;
                if (!_transitions.TryGetValue(trigger.Key, out var nexts)) continue;

                var occurrences = _occurrences.TryGetValue(trigger.Key, out var occ) ? occ : 1;

                foreach (var next in nexts)
                {
                    if (next.Value < minSupport) continue;

                    var confidence = Math.Min(1.0, (double)next.Value / occurrences);
                    if (confidence < minConfidence) continue;
                    if (HappenedAfterLocked(next.Key, trigger.At)) continue;

                    if (!best.TryGetValue(next.Key, out var existing) || confidence > existing.Confidence)
                        best[next.Key] = new Prediction(trigger.Key, next.Key, next.Value, confidence);
                }
            }
        }

        return best.Values
            .OrderByDescending(p => p.Confidence)
            .ThenByDescending(p => p.Support)
            .ToList();
    }

    public List<Prediction> Patterns(int minSupport = 2)
    {
        lock (_gate)
        {
            var list = new List<Prediction>();

            foreach (var outer in _transitions)
            {
                var occurrences = _occurrences.TryGetValue(outer.Key, out var occ) ? occ : 1;

                foreach (var inner in outer.Value)
                {
                    if (inner.Value < minSupport) continue;
                    var confidence = Math.Min(1.0, (double)inner.Value / occurrences);
                    list.Add(new Prediction(outer.Key, inner.Key, inner.Value, confidence));
                }
            }

            return list
                .OrderByDescending(p => p.Support)
                .ThenByDescending(p => p.Confidence)
                .ToList();
        }
    }

    private bool HappenedAfterLocked(string key, DateTimeOffset after)
    {
        foreach (var entry in _recent)
        {
            if (entry.Key == key && entry.At > after) return true;
        }

        return false;
    }
}
