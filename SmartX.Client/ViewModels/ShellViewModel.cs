using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using SmartX.Client.Services;

namespace SmartX.Client.ViewModels;

public interface INavigable
{
    Task ActivateAsync();
    void Deactivate();
}

public sealed class NavItemViewModel : ObservableObject
{
    private bool _isActive;

    public NavItemViewModel(string key, string title, string subtitle, bool isEnabled)
    {
        Key = key;
        Title = title;
        Subtitle = subtitle;
        IsEnabled = isEnabled;
    }

    public string Key { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public bool IsEnabled { get; }

    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }
}

public sealed class HomeViewModel : ObservableObject
{
    public HomeViewModel(Action<string> navigate)
    {
        OpenTelemetryCommand = new RelayCommand(_ => navigate("telemetry"));
        OpenCommandStreamCommand = new RelayCommand(_ => navigate("commands"));
    }

    public ICommand OpenTelemetryCommand { get; }
    public ICommand OpenCommandStreamCommand { get; }
}

public sealed class ShellViewModel : ObservableObject
{
    private object _currentViewModel;

    public ShellViewModel(ApiClient api)
    {
        Telemetry = new TelemetryDashboardViewModel(api);
        CommandStream = new CommandStreamViewModel(api);
        Home = new HomeViewModel(key => _ = NavigateAsync(key));

        NavItems = new ObservableCollection<NavItemViewModel>
        {
            new("home", "Home", "Overview of the three modules", true),
            new("telemetry", "1  Sensor Ingestion and Telemetry", "Register, attach files, live sparklines", true),
            new("commands", "2  Command Stream and History", "Live devices, overrides, insights", true),
            new("topology", "3  Network Topology and Mesh Routing", "Final PoE", false),
        };

        _currentViewModel = Home;
        NavItems[0].IsActive = true;

        NavigateCommand = new RelayCommand(async parameter => await NavigateAsync(parameter as string ?? "home"));
    }

    public HomeViewModel Home { get; }
    public TelemetryDashboardViewModel Telemetry { get; }
    public CommandStreamViewModel CommandStream { get; }
    public ObservableCollection<NavItemViewModel> NavItems { get; }
    public ICommand NavigateCommand { get; }

    public object CurrentViewModel
    {
        get => _currentViewModel;
        private set => SetField(ref _currentViewModel, value);
    }

    public async Task NavigateAsync(string key)
    {
        object target = key switch
        {
            "telemetry" => Telemetry,
            "commands" => CommandStream,
            _ => Home
        };

        if (ReferenceEquals(target, _currentViewModel)) return;

        if (_currentViewModel is INavigable leaving) leaving.Deactivate();

        CurrentViewModel = target;

        var activeKey = ReferenceEquals(target, Telemetry)
            ? "telemetry"
            : ReferenceEquals(target, CommandStream) ? "commands" : "home";

        foreach (var item in NavItems)
            item.IsActive = item.Key == activeKey;

        if (target is INavigable entering) await entering.ActivateAsync();
    }
}
