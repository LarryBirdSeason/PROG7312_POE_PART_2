using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SmartX.Client.Services;
using SmartX.Core.Models;
using SmartX.Core.Services;

namespace SmartX.Client.ViewModels;

public sealed class CommandStreamViewModel : ObservableObject, INavigable
{
    private const double ChartWidth = 560;
    private const double ChartHeight = 170;

    private readonly ApiClient _api;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _debounce;
    private bool _refreshing;
    private bool _active;
    private long _lastProcessed;
    private DateTime _lastSample = DateTime.MinValue;

    private string _searchText = string.Empty;
    private string _selectedCategory = "All categories";
    private string _selectedAlertFilter = "All";
    private DeviceItemViewModel? _selectedDevice;
    private bool _apiOnline = true;
    private string _connectionMessage = string.Empty;
    private string _statusMessage = "Select a device to see its live chart and controls.";
    private string _pipelineText = "-";
    private string _latencyText = "-";
    private string _rateText = "-";
    private string _loadText = "-";
    private string _alertsText = "-";
    private int _undoDepth;
    private PointCollection _chartPoints = new();
    private double _critLowY = double.NaN;
    private double _critHighY = double.NaN;
    private string _chartCaption = "No device selected";

    public CommandStreamViewModel(ApiClient api)
    {
        _api = api;

        CategoryOptions = new[] { "All categories" }
            .Concat(Enum.GetNames<SensorCategory>()).ToList();
        AlertOptions = new[] { "All", "Alerting", "Critical", "Warning", "Offline", "Normal" };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await RefreshAsync();

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await RefreshAsync();
        };

        TurnOnCommand = new RelayCommand(async _ => await SendAsync(CommandAction.TurnOn), _ => CanControl);
        TurnOffCommand = new RelayCommand(async _ => await SendAsync(CommandAction.TurnOff), _ => CanControl);
        ShutdownValvesCommand = new RelayCommand(async _ => await RunBulkAsync("Valve", CommandAction.TurnOff));
        UndoCommand = new RelayCommand(async _ => await UndoAsync(), _ => UndoDepth > 0);
        ApplyInsightCommand = new RelayCommand(async p => await ApplyInsightAsync(p as InsightItemViewModel));
        ShowDeviceCommand = new RelayCommand(p => ShowInsightDevice(p as InsightItemViewModel));
        CommitSearchCommand = new RelayCommand(async _ => await CommitSearchAsync());
        ClearFiltersCommand = new RelayCommand(_ =>
        {
            SearchText = string.Empty;
            SelectedCategory = CategoryOptions[0];
            SelectedAlertFilter = AlertOptions[0];
        });
    }

    public ObservableCollection<DeviceItemViewModel> Devices { get; } = new();
    public ObservableCollection<InsightItemViewModel> Insights { get; } = new();
    public ObservableCollection<PatternDto> Patterns { get; } = new();
    public ObservableCollection<AlertItemViewModel> Alerts { get; } = new();
    public ObservableCollection<CommandItemViewModel> CommandLog { get; } = new();

    public IReadOnlyList<string> CategoryOptions { get; }
    public IReadOnlyList<string> AlertOptions { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetField(ref _searchText, value)) return;
            RestartDebounce();
        }
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetField(ref _selectedCategory, value)) RestartDebounce();
        }
    }

    public string SelectedAlertFilter
    {
        get => _selectedAlertFilter;
        set
        {
            if (SetField(ref _selectedAlertFilter, value)) RestartDebounce();
        }
    }

    private void RestartDebounce()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    public DeviceItemViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!SetField(ref _selectedDevice, value)) return;

            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CanControl));
            ChartPoints = new PointCollection();
            CritLowY = double.NaN;
            CritHighY = double.NaN;

            if (value is not null)
            {
                _ = _api.RecordSearchAsync(value.Mac, value.Name).ContinueWith(_ => { });
                _ = RefreshChartAsync();
            }
            else
            {
                ChartCaption = "No device selected";
            }
        }
    }

    public bool HasSelection => _selectedDevice is not null;
    public bool CanControl => _selectedDevice is { IsActuator: true } && ApiOnline;

    public PointCollection ChartPoints
    {
        get => _chartPoints;
        private set => SetField(ref _chartPoints, value);
    }

    public double CritLowY { get => _critLowY; private set { SetField(ref _critLowY, value); OnPropertyChanged(nameof(HasCritLow)); } }
    public double CritHighY { get => _critHighY; private set { SetField(ref _critHighY, value); OnPropertyChanged(nameof(HasCritHigh)); } }
    public bool HasCritLow => !double.IsNaN(_critLowY);
    public bool HasCritHigh => !double.IsNaN(_critHighY);
    public string ChartCaption { get => _chartCaption; private set => SetField(ref _chartCaption, value); }

    public double ChartCanvasWidth => ChartWidth;
    public double ChartCanvasHeight => ChartHeight;

    public bool ApiOnline
    {
        get => _apiOnline;
        private set
        {
            if (SetField(ref _apiOnline, value))
            {
                OnPropertyChanged(nameof(CanControl));
                OnPropertyChanged(nameof(ApiOffline));
            }
        }
    }

    public bool ApiOffline => !_apiOnline;
    public string ConnectionMessage { get => _connectionMessage; private set => SetField(ref _connectionMessage, value); }
    public string StatusMessage { get => _statusMessage; private set => SetField(ref _statusMessage, value); }
    public string PipelineText { get => _pipelineText; private set => SetField(ref _pipelineText, value); }
    public string LatencyText { get => _latencyText; private set => SetField(ref _latencyText, value); }
    public string RateText { get => _rateText; private set => SetField(ref _rateText, value); }
    public string LoadText { get => _loadText; private set => SetField(ref _loadText, value); }
    public string AlertsText { get => _alertsText; private set => SetField(ref _alertsText, value); }

    public int UndoDepth
    {
        get => _undoDepth;
        private set
        {
            if (SetField(ref _undoDepth, value)) OnPropertyChanged(nameof(UndoText));
        }
    }

    public string UndoText => $"Undo last command ({_undoDepth})";

    public ICommand TurnOnCommand { get; }
    public ICommand TurnOffCommand { get; }
    public ICommand ShutdownValvesCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand ApplyInsightCommand { get; }
    public ICommand ShowDeviceCommand { get; }
    public ICommand CommitSearchCommand { get; }
    public ICommand ClearFiltersCommand { get; }

    public async Task ActivateAsync()
    {
        _active = true;
        _timer.Start();
        await RefreshAsync();
    }

    public void Deactivate()
    {
        _active = false;
        _timer.Stop();
        _debounce.Stop();
    }

    private async Task RefreshAsync()
    {
        if (_refreshing || !_active) return;
        _refreshing = true;

        try
        {
            var category = _selectedCategory == CategoryOptions[0] ? null : _selectedCategory;

            var devicesTask = _api.GetDevicesAsync(_searchText, category, _selectedAlertFilter);
            var statsTask = _api.GetStatsAsync();
            var insightsTask = _api.GetInsightsAsync();
            var alertsTask = _api.GetAlertsAsync();
            var commandsTask = _api.GetCommandsAsync();
            var patternsTask = _api.GetPatternsAsync();

            await Task.WhenAll(devicesTask, statsTask, insightsTask, alertsTask, commandsTask, patternsTask);

            var insights = insightsTask.Result;
            SyncInsights(insights);
            SyncDevices(devicesTask.Result, insights);
            ApplyStats(statsTask.Result);
            SyncAlerts(alertsTask.Result);
            SyncCommands(commandsTask.Result);
            SyncPatterns(patternsTask.Result);

            await RefreshChartAsync();

            ApiOnline = true;
            ConnectionMessage = string.Empty;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException
                                       or System.Text.Json.JsonException)
        {
            ApiOnline = false;
            ConnectionMessage = "API unreachable at http://localhost:5080. Showing the last known data; retrying every second.";
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void SyncDevices(List<DeviceDto> dtos, List<InsightDto> insights)
    {
        var flagged = insights
            .Where(i => !string.IsNullOrEmpty(i.Mac) && i.Severity >= Severity.Warning)
            .Select(i => i.Mac!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existing = Devices.ToDictionary(d => d.Mac, StringComparer.OrdinalIgnoreCase);
        var incoming = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < dtos.Count; index++)
        {
            var dto = dtos[index];
            incoming.Add(dto.Mac);

            if (!existing.TryGetValue(dto.Mac, out var item))
            {
                item = new DeviceItemViewModel(dto);
                Devices.Insert(Math.Min(index, Devices.Count), item);
            }
            else
            {
                item.Update(dto);
                var current = Devices.IndexOf(item);
                if (current != index && index < Devices.Count) Devices.Move(current, index);
            }

            item.IsFlagged = flagged.Contains(dto.Mac);
        }

        for (var i = Devices.Count - 1; i >= 0; i--)
        {
            if (!incoming.Contains(Devices[i].Mac))
            {
                var removed = Devices[i];
                Devices.RemoveAt(i);
                if (ReferenceEquals(removed, _selectedDevice))
                {
                }
            }
        }

        if (_selectedDevice is not null)
        {
            var fresh = Devices.FirstOrDefault(d => d.Mac == _selectedDevice.Mac);
            if (fresh is not null && !ReferenceEquals(fresh, _selectedDevice))
                SelectedDevice = fresh;
        }
    }

    private void SyncInsights(List<InsightDto> dtos)
    {
        var existing = Insights.ToDictionary(i => i.Id);
        var ids = new HashSet<string>(dtos.Select(d => d.Id));

        for (var index = 0; index < dtos.Count; index++)
        {
            var dto = dtos[index];
            if (existing.TryGetValue(dto.Id, out var item))
            {
                item.Update(dto);
                var current = Insights.IndexOf(item);
                if (current != index && index < Insights.Count) Insights.Move(current, index);
            }
            else
            {
                Insights.Insert(Math.Min(index, Insights.Count), new InsightItemViewModel(dto));
            }
        }

        for (var i = Insights.Count - 1; i >= 0; i--)
            if (!ids.Contains(Insights[i].Id)) Insights.RemoveAt(i);
    }

    private void SyncAlerts(List<AlertDto> dtos)
    {
        Alerts.Clear();
        foreach (var alert in dtos.Take(30)) Alerts.Add(new AlertItemViewModel(alert));
    }

    private void SyncCommands(List<CommandRecordDto> dtos)
    {
        CommandLog.Clear();
        foreach (var record in dtos.Take(40)) CommandLog.Add(new CommandItemViewModel(record));
        UndoDepth = dtos.Count(r => r.CanUndo);
    }

    private void SyncPatterns(List<PatternDto> dtos)
    {
        if (Patterns.Count == dtos.Count && Patterns.Zip(dtos).All(pair => pair.First == pair.Second)) return;

        Patterns.Clear();
        foreach (var pattern in dtos) Patterns.Add(pattern);
    }

    private void ApplyStats(PipelineStatsDto s)
    {
        PipelineText = $"Queue {s.StandardDepth}  ·  Priority {s.PriorityDepth}  ·  Peak {s.PeakStandardDepth}/{s.PeakPriorityDepth}";
        LatencyText = $"Latency  std {s.StandardAvgLatencyMs:0.0} ms  ·  critical {s.PriorityAvgLatencyMs:0.0} ms";

        var processed = s.StandardProcessed + s.PriorityProcessed;
        var now = DateTime.UtcNow;
        if (_lastSample != DateTime.MinValue)
        {
            var seconds = Math.Max(0.1, (now - _lastSample).TotalSeconds);
            var rate = Math.Max(0, processed - _lastProcessed) / seconds;
            RateText = $"{rate.ToString("0", CultureInfo.InvariantCulture)} msg/s  ·  {s.TotalReceived} total  ·  {s.Rejected} rejected";
        }

        _lastProcessed = processed;
        _lastSample = now;

        LoadText = $"Aggregate load {s.AggregateLoadWatts.ToString("0", CultureInfo.InvariantCulture)} W";
        AlertsText = $"{s.ActiveAlerts} active alerts  ·  {s.DuplicateAlertsSuppressed} duplicates suppressed";
        UndoDepth = s.UndoDepth;
    }

    private async Task RefreshChartAsync()
    {
        var device = _selectedDevice;
        if (device is null) return;

        try
        {
            var points = await _api.GetHistoryAsync(device.Mac, 120);
            if (!ReferenceEquals(device, _selectedDevice)) return;

            BuildChart(device, points);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException
                                       or System.Text.Json.JsonException)
        {
        }
    }

    private void BuildChart(DeviceItemViewModel device, List<HistoryPointDto> points)
    {
        if (points.Count < 2)
        {
            ChartPoints = new PointCollection();
            ChartCaption = $"{device.Name}: waiting for readings";
            return;
        }

        var limits = ThresholdRules.ForKind(device.Kind);
        var lowLimit = limits.CritLow;
        var highLimit = limits.CritHigh;

        var min = points.Min(p => p.Value);
        var max = points.Max(p => p.Value);
        if (lowLimit.HasValue) min = Math.Min(min, lowLimit.Value);
        if (highLimit.HasValue) max = Math.Max(max, highLimit.Value);
        if (Math.Abs(max - min) < 1e-6) { min -= 1; max += 1; }

        var pad = (max - min) * 0.08;
        min -= pad;
        max += pad;

        var t0 = points[0].Timestamp;
        var span = Math.Max(1, (points[^1].Timestamp - t0).TotalSeconds);

        double X(DateTimeOffset t) => (t - t0).TotalSeconds / span * ChartWidth;
        double Y(double v) => ChartHeight - (v - min) / (max - min) * ChartHeight;

        var collection = new PointCollection(points.Count);
        foreach (var p in points) collection.Add(new Point(X(p.Timestamp), Y(p.Value)));
        collection.Freeze();

        ChartPoints = collection;
        CritLowY = lowLimit.HasValue ? Y(lowLimit.Value) : double.NaN;
        CritHighY = highLimit.HasValue ? Y(highLimit.Value) : double.NaN;
        ChartCaption = $"{device.Name}  ·  last {span:0} s  ·  range {min + pad:0.#} to {max - pad:0.#}";
    }

    private async Task SendAsync(CommandAction action)
    {
        var device = _selectedDevice;
        if (device is null) return;

        await RunAsync(() => _api.IssueCommandAsync(device.Mac, action));
    }

    private Task RunBulkAsync(string kind, CommandAction action)
        => RunAsync(() => _api.BulkCommandAsync(kind, action));

    private Task UndoAsync() => RunAsync(() => _api.UndoCommandAsync());

    private async Task ApplyInsightAsync(InsightItemViewModel? insight)
    {
        if (insight is null || insight.Mac is null || insight.Action is not { } action) return;

        await RunAsync(() => _api.IssueCommandAsync(insight.Mac, action));
    }

    private void ShowInsightDevice(InsightItemViewModel? insight)
    {
        if (insight?.Mac is null) return;

        var match = Devices.FirstOrDefault(d => d.Mac.Equals(insight.Mac, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            SearchText = string.Empty;
            SelectedCategory = CategoryOptions[0];
            SelectedAlertFilter = AlertOptions[0];
            StatusMessage = "Filters cleared to show the suggested device.";
            return;
        }

        SelectedDevice = match;
    }

    private async Task CommitSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(_searchText)) return;

        try
        {
            var matches = Devices.ToList();
            var mac = matches.Count == 1 ? matches[0].Mac : null;
            await _api.RecordSearchAsync(mac, _searchText.Trim());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
        }
    }

    private async Task RunAsync(Func<Task<CommandResultDto>> call)
    {
        try
        {
            var result = await call();
            StatusMessage = result.Message;
            ApiOnline = true;
            await RefreshAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException
                                       or System.Text.Json.JsonException)
        {
            ApiOnline = false;
            StatusMessage = "Command not sent: the API is unreachable.";
        }
    }
}
