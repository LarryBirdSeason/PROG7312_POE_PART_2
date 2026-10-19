using System.Globalization;
using SmartX.Core.Models;

namespace SmartX.Core.Services;

public readonly record struct LimitSet(double? WarnLow, double? WarnHigh, double? CritLow, double? CritHigh, string Unit);

public readonly record struct Evaluation(Severity Severity, string? AlertType, string Message)
{
    public static readonly Evaluation Normal = new(Severity.Normal, null, string.Empty);
}

public static class ThresholdRules
{
    public static LimitSet ForKind(string kind) => kind switch
    {
        "Moisture" => new LimitSet(30, 85, 20, 95, "%"),
        "Temperature" => new LimitSet(10, 32, 2, 40, "°C"),
        "Power" => new LimitSet(null, 1800, null, 3000, "W"),
        _ => new LimitSet(null, null, null, null, string.Empty)
    };

    public static Evaluation Evaluate(string kind, double value)
    {
        var limits = ForKind(kind);
        var unit = limits.Unit;

        if (limits.CritLow is double critLow && value < critLow)
            return new Evaluation(Severity.Critical, "CriticalLow",
                $"Critical low: {Fmt(value)} {unit} (limit {Fmt(critLow)})");

        if (limits.CritHigh is double critHigh && value > critHigh)
            return new Evaluation(Severity.Critical, "CriticalHigh",
                $"Critical high: {Fmt(value)} {unit} (limit {Fmt(critHigh)})");

        if (limits.WarnLow is double warnLow && value < warnLow)
            return new Evaluation(Severity.Warning, "WarningLow",
                $"Low: {Fmt(value)} {unit} (warn below {Fmt(warnLow)})");

        if (limits.WarnHigh is double warnHigh && value > warnHigh)
            return new Evaluation(Severity.Warning, "WarningHigh",
                $"High: {Fmt(value)} {unit} (warn above {Fmt(warnHigh)})");

        return Evaluation.Normal;
    }

    public static string DefaultKind(SensorCategory category) => category switch
    {
        SensorCategory.Environmental => "Moisture",
        SensorCategory.PowerConsumption => "Power",
        _ => "Valve"
    };

    public static SensorCategory InferCategory(string metric)
    {
        var m = (metric ?? string.Empty).ToLowerInvariant();

        if (m.Contains("pump") || m.Contains("valve") || m.Contains("relay") ||
            m.Contains("switch") || m.Contains("actuator"))
            return SensorCategory.Actuator;

        if (m.Contains("power") || m.Contains("watt") || m.Contains("meter") || m.Contains("consumption"))
            return SensorCategory.PowerConsumption;

        return SensorCategory.Environmental;
    }

    public static string InferKind(SensorCategory category, string metric)
    {
        var m = (metric ?? string.Empty).ToLowerInvariant();

        if (m.Contains("moist")) return "Moisture";
        if (m.Contains("temp")) return "Temperature";
        if (m.Contains("power") || m.Contains("watt") || m.Contains("meter")) return "Power";
        if (m.Contains("pump")) return "Pump";
        if (m.Contains("valve")) return "Valve";
        if (m.Contains("relay") || m.Contains("switch")) return "Relay";

        return DefaultKind(category);
    }

    private static string Fmt(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
