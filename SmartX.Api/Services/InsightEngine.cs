using System.Globalization;
using SmartX.Core.Models;
using SmartX.Core.Services;

namespace SmartX.Api.Services;

public sealed class InsightEngine
{
    private readonly DeviceRegistry _registry;
    private readonly TelemetryHistory _history;
    private readonly ActionPredictor _predictor;

    public InsightEngine(DeviceRegistry registry, TelemetryHistory history, ActionPredictor predictor)
    {
        _registry = registry;
        _history = history;
        _predictor = predictor;
    }

    public List<InsightDto> Generate()
    {
        var insights = new List<InsightDto>();

        AddPatternInsights(insights);
        AddForecastInsights(insights);
        AddFaultInsights(insights);

        return insights
            .OrderByDescending(i => i.Severity)
            .ThenByDescending(i => i.Confidence)
            .Take(10)
            .ToList();
    }

    public List<PatternDto> LearnedPatterns()
    {
        return _predictor.Patterns(minSupport: 2)
            .Take(6)
            .Select(p => new PatternDto($"{Describe(p.TriggerKey)}  ->  {Describe(p.NextKey)}", p.Support, p.Confidence))
            .ToList();
    }

    private void AddPatternInsights(List<InsightDto> insights)
    {
        foreach (var prediction in _predictor.Predict())
        {
            if (!ActionKey.TryParse(prediction.NextKey, out var kind, out var mac, out var detail)) continue;

            var device = _registry.Get(mac);
            if (device is null) continue;

            var trigger = Describe(prediction.TriggerKey);
            var percent = (prediction.Confidence * 100).ToString("0", CultureInfo.InvariantCulture);
            var reason = $"After {trigger}, this followed {prediction.Support} time(s) before ({percent}% of the time).";

            if (kind == "cmd" && Enum.TryParse<CommandAction>(detail, out var action))
            {
                var wantOn = action == CommandAction.TurnOn;
                if (CommandService.IsOn(device) == wantOn) continue;

                var verb = wantOn ? "ON" : "OFF";
                insights.Add(new InsightDto(
                    $"pattern|{prediction.NextKey}", InsightKind.Pattern, Severity.Warning,
                    $"Turn {verb} {device.Name}", reason, mac, action, prediction.Confidence));
            }
            else if (kind == "search")
            {
                insights.Add(new InsightDto(
                    $"pattern|{prediction.NextKey}", InsightKind.Pattern, Severity.Normal,
                    $"Check {device.Name}", reason, mac, null, prediction.Confidence));
            }
        }
    }

    private void AddForecastInsights(List<InsightDto> insights)
    {
        foreach (var device in _registry.Snapshot())
        {
            if (device.Category == SensorCategory.Actuator || !device.HasReading ||
                device.Connection != ConnectionState.Online) continue;

            var limits = ThresholdRules.ForKind(device.Kind);
            var points = _history.RecentPoints(device.Mac, 20);
            if (points.Count < 8) continue;

            var origin = points[0].At;
            var series = new List<(double Seconds, double Value)>();
            foreach (var point in points)
                series.Add(((point.At - origin).TotalSeconds, point.Value));

            if (TrendForecaster.Fit(series) is not { } fit || fit.RSquared < 0.6) continue;

            var latest = points[points.Count - 1].Value;
            double? secondsToBreach = null;
            double limit = 0;
            var direction = string.Empty;

            if (fit.Slope < 0 && limits.CritLow is double lowLimit && latest > lowLimit)
            {
                secondsToBreach = TrendForecaster.SecondsUntil(latest, fit.Slope, lowLimit);
                limit = lowLimit;
                direction = "below";
            }
            else if (fit.Slope > 0 && limits.CritHigh is double highLimit && latest < highLimit)
            {
                secondsToBreach = TrendForecaster.SecondsUntil(latest, fit.Slope, highLimit);
                limit = highLimit;
                direction = "above";
            }

            if (secondsToBreach is not { } eta || eta > 90) continue;

            var etaText = eta.ToString("0", CultureInfo.InvariantCulture);
            var slopeText = fit.Slope.ToString("+0.0;-0.0", CultureInfo.InvariantCulture);
            var limitText = limit.ToString("0.#", CultureInfo.InvariantCulture);
            var fitText = fit.RSquared.ToString("0.00", CultureInfo.InvariantCulture);

            insights.Add(new InsightDto(
                $"forecast|{device.Mac}", InsightKind.Forecast, Severity.Warning,
                $"{device.Name} heading {direction} {limitText} {device.Unit} in about {etaText} s",
                $"Trend {slopeText} {device.Unit}/s over the last {points.Count} readings (fit R² {fitText}). Act before the critical limit is reached.",
                device.Mac, null, fit.RSquared));
        }
    }

    private void AddFaultInsights(List<InsightDto> insights)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var device in _registry.Snapshot())
        {
            if (device.Connection == ConnectionState.Offline)
            {
                var silentFor = (now - device.LastSeen).TotalSeconds.ToString("0", CultureInfo.InvariantCulture);
                insights.Add(new InsightDto(
                    $"fault|{device.Mac}|offline", InsightKind.Fault, Severity.Critical,
                    $"{device.Name} is offline",
                    $"No telemetry for {silentFor} s. Check power and Wi-Fi at {device.Location}.",
                    device.Mac, null, 1.0));
                continue;
            }

            if (device.Severity == Severity.Critical)
            {
                insights.Add(new InsightDto(
                    $"fault|{device.Mac}|critical", InsightKind.Fault, Severity.Critical,
                    $"{device.Name} is outside its safe range", device.AlertText, device.Mac, null, 1.0));
                continue;
            }

            if (device.Category != SensorCategory.Actuator && device.HasReading)
            {
                var values = _history.RecentValues(device.Mac, 10);
                if (values.Count >= 10 && values.Max() - values.Min() < 1e-9)
                {
                    insights.Add(new InsightDto(
                        $"fault|{device.Mac}|flatline", InsightKind.Fault, Severity.Warning,
                        $"{device.Name} may be stuck",
                        "The last 10 readings are identical. A live sensor normally shows some noise.",
                        device.Mac, null, 0.7));
                }
            }
        }
    }

    private string Describe(string key)
    {
        if (!ActionKey.TryParse(key, out var kind, out var mac, out var detail)) return key;

        var name = _registry.NameOf(mac);

        return kind switch
        {
            "alert" => $"{name} {AlertWords(detail)}",
            "search" => $"you opened {name}",
            "cmd" => detail == nameof(CommandAction.TurnOn) ? $"you turned on {name}" : $"you turned off {name}",
            _ => key
        };
    }

    private static string AlertWords(string type) => type switch
    {
        "CriticalLow" => "critical low reading",
        "CriticalHigh" => "critical high reading",
        "WarningLow" => "low reading",
        "WarningHigh" => "high reading",
        "Disconnected" => "went offline",
        _ => type
    };
}
