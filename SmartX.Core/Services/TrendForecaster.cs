namespace SmartX.Core.Services;

public readonly record struct TrendFit(double Slope, double Intercept, double RSquared);

public static class TrendForecaster
{
    public static TrendFit? Fit(IReadOnlyList<(double Seconds, double Value)> points)
    {
        int n = points.Count;
        if (n < 3) return null;

        double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
        foreach (var (x, y) in points)
        {
            sumX += x;
            sumY += y;
            sumXY += x * y;
            sumXX += x * x;
        }

        double denominator = n * sumXX - sumX * sumX;
        if (Math.Abs(denominator) < 1e-9) return null;

        double slope = (n * sumXY - sumX * sumY) / denominator;
        double intercept = (sumY - slope * sumX) / n;

        double mean = sumY / n;
        double ssTotal = 0, ssResidual = 0;
        foreach (var (x, y) in points)
        {
            double predicted = slope * x + intercept;
            ssResidual += (y - predicted) * (y - predicted);
            ssTotal += (y - mean) * (y - mean);
        }

        double rSquared = ssTotal < 1e-12 ? 0 : 1 - ssResidual / ssTotal;
        return new TrendFit(slope, intercept, rSquared);
    }

    public static double? SecondsUntil(double current, double slopePerSecond, double target)
    {
        if (Math.Abs(slopePerSecond) < 1e-9) return null;

        double seconds = (target - current) / slopePerSecond;
        if (seconds > 0) return seconds;
        return null;
    }
}
