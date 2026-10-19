using System.Windows;
using System.Windows.Media;
using SmartX.Core.Models;

namespace SmartX.Client.ViewModels;

public enum ConnectionStatus { Live, Stale, Disconnected }

public class SensorRowViewModel : ObservableObject
{
    private const int WindowSize = 40;
    private const double CanvasWidth = 220;
    private const double CanvasHeight = 48;

    private readonly Queue<double> _window = new();

    public SensorRegistration Registration { get; }

    public string MacAddress => Registration.MacAddress;
    public string Location => Registration.Location;
    public SensorCategory Category => Registration.Category;

    private DateTimeOffset _lastSeen;
    public DateTimeOffset LastSeen
    {
        get => _lastSeen;
        private set => SetField(ref _lastSeen, value);
    }

    private ConnectionStatus _status = ConnectionStatus.Live;
    public ConnectionStatus Status
    {
        get => _status;
        private set
        {
            if (SetField(ref _status, value))
                OnPropertyChanged(nameof(StatusBrush));
        }
    }

    public Brush StatusBrush => Status switch
    {
        ConnectionStatus.Live => Brushes.LimeGreen,
        ConnectionStatus.Stale => Brushes.Orange,
        _ => Brushes.Red
    };

    private PointCollection _sparkline = new();
    public PointCollection Sparkline
    {
        get => _sparkline;
        private set => SetField(ref _sparkline, value);
    }

    private PointCollection _upperBand = new();
    public PointCollection UpperBand
    {
        get => _upperBand;
        private set => SetField(ref _upperBand, value);
    }

    private PointCollection _lowerBand = new();
    public PointCollection LowerBand
    {
        get => _lowerBand;
        private set => SetField(ref _lowerBand, value);
    }

    private bool _isAnomalous;
    public bool IsAnomalous
    {
        get => _isAnomalous;
        private set => SetField(ref _isAnomalous, value);
    }

    private double _lastValue;
    public double LastValue
    {
        get => _lastValue;
        private set => SetField(ref _lastValue, value);
    }

    public SensorRowViewModel(SensorRegistration registration)
    {
        Registration = registration;
        LastSeen = registration.LastSeen;
    }

    public void PushReading(double value)
    {
        _window.Enqueue(value);
        while (_window.Count > WindowSize) _window.Dequeue();

        LastValue = value;
        LastSeen = DateTimeOffset.UtcNow;
        RefreshStatus();

        var values = _window.ToArray();
        if (values.Length < 2) return;

        double mean = values.Average();
        double variance = values.Select(v => (v - mean) * (v - mean)).Average();
        double stdDev = Math.Sqrt(variance);
        double upper = mean + 2 * stdDev;
        double lower = mean - 2 * stdDev;

        double min = Math.Min(values.Min(), lower);
        double max = Math.Max(values.Max(), upper);
        double range = Math.Max(max - min, 0.0001);

        double StepX(int i) => i * (CanvasWidth / (values.Length - 1));
        double ScaleY(double v) => CanvasHeight - ((v - min) / range) * CanvasHeight;

        var points = new PointCollection();
        for (int i = 0; i < values.Length; i++)
            points.Add(new Point(StepX(i), ScaleY(values[i])));
        Sparkline = points;

        UpperBand = new PointCollection { new Point(0, ScaleY(upper)), new Point(CanvasWidth, ScaleY(upper)) };
        LowerBand = new PointCollection { new Point(0, ScaleY(lower)), new Point(CanvasWidth, ScaleY(lower)) };

        IsAnomalous = values[^1] > upper || values[^1] < lower;
    }

    public void RefreshStatus()
    {
        var elapsedSeconds = (DateTimeOffset.UtcNow - LastSeen).TotalSeconds;
        Status = elapsedSeconds switch
        {
            < 5 => ConnectionStatus.Live,
            < 12 => ConnectionStatus.Stale,
            _ => ConnectionStatus.Disconnected
        };
    }
}
