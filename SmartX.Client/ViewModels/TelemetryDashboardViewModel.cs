using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using SmartX.Client.Services;
using SmartX.Core.Models;
using SmartX.Core.Services;

namespace SmartX.Client.ViewModels;

public class TelemetryDashboardViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly DispatcherTimer _timer;
    private readonly Random _rng = new();

    public ObservableCollection<SensorRowViewModel> Sensors { get; } = new();
    public IEnumerable<SensorCategory> Categories { get; } = Enum.GetValues<SensorCategory>();

    private string _newMac = string.Empty;
    public string NewMac { get => _newMac; set => SetField(ref _newMac, value); }

    private string _newLocation = string.Empty;
    public string NewLocation { get => _newLocation; set => SetField(ref _newLocation, value); }

    private SensorCategory _newCategory = SensorCategory.Environmental;
    public SensorCategory NewCategory { get => _newCategory; set => SetField(ref _newCategory, value); }

    private SensorRowViewModel? _selectedSensor;
    public SensorRowViewModel? SelectedSensor { get => _selectedSensor; set => SetField(ref _selectedSensor, value); }

    private bool _isSimulating = true;
    public bool IsSimulating { get => _isSimulating; set => SetField(ref _isSimulating, value); }

    private string _deploymentTreeText =
        "Facility A\n  Zone 1\n    Sub-Zone B\n      NODE-01\n  Zone 2\n    Sub-Zone A\n      NODE-02";
    public string DeploymentTreeText { get => _deploymentTreeText; set => SetField(ref _deploymentTreeText, value); }

    private string _validationResult = string.Empty;
    public string ValidationResult { get => _validationResult; private set => SetField(ref _validationResult, value); }

    private string _statusMessage = "Ready.";
    public string StatusMessage { get => _statusMessage; private set => SetField(ref _statusMessage, value); }

    public ICommand RegisterSensorCommand { get; }
    public ICommand AttachFileCommand { get; }
    public ICommand ValidateDeploymentCommand { get; }

    public TelemetryDashboardViewModel(ApiClient api)
    {
        _api = api;

        RegisterSensorCommand = new RelayCommand(async _ => await RegisterSensorAsync());
        AttachFileCommand = new RelayCommand(async _ => await AttachFileAsync(), _ => SelectedSensor is not null);
        ValidateDeploymentCommand = new RelayCommand(_ => ValidateDeployment());

        SeedDemoSensors();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    private void SeedDemoSensors()
    {
        AddSensorLocal(new SensorRegistration
        {
            MacAddress = "24:6F:28:AA:01",
            Location = "Sub-Zone B > Zone 1 > Facility A",
            Category = SensorCategory.Environmental
        });
        AddSensorLocal(new SensorRegistration
        {
            MacAddress = "24:6F:28:AA:02",
            Location = "Sub-Zone C > Zone 1 > Facility A",
            Category = SensorCategory.PowerConsumption
        });
        AddSensorLocal(new SensorRegistration
        {
            MacAddress = "24:6F:28:AA:03",
            Location = "Sub-Zone A > Zone 2 > Facility A",
            Category = SensorCategory.Actuator
        });
    }

    private void AddSensorLocal(SensorRegistration registration) => Sensors.Add(new SensorRowViewModel(registration));

    private async Task RegisterSensorAsync()
    {
        if (string.IsNullOrWhiteSpace(NewMac))
        {
            StatusMessage = "A MAC address / unique identifier is required.";
            return;
        }

        var registration = new SensorRegistration
        {
            MacAddress = NewMac.Trim(),
            Location = string.IsNullOrWhiteSpace(NewLocation) ? "Unassigned" : NewLocation.Trim(),
            Category = NewCategory
        };

        try
        {
            await _api.RegisterSensorAsync(registration);
            StatusMessage = $"Registered {registration.MacAddress} via API.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"API unreachable, sensor added locally only ({ex.Message}).";
        }

        AddSensorLocal(registration);
        NewMac = string.Empty;
        NewLocation = string.Empty;
    }

    private async Task AttachFileAsync()
    {
        if (SelectedSensor is null) return;

        var dialog = new OpenFileDialog
        {
            Title = "Attach device configuration, hardware log, or deployment photo",
            Filter = "Device files (*.json;*.cfg;*.log;*.jpg;*.jpeg;*.png)|*.json;*.cfg;*.log;*.jpg;*.jpeg;*.png|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            await _api.UploadAttachmentAsync(SelectedSensor.Registration.Id, dialog.FileName);
            StatusMessage = $"Attached '{Path.GetFileName(dialog.FileName)}' to {SelectedSensor.MacAddress}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Upload failed: {ex.Message}";
        }
    }

    private void ValidateDeployment()
    {
        var root = DeploymentTreeParser.Parse(DeploymentTreeText);
        var result = DeploymentTreeValidator.Validate(root);
        ValidationResult = result.Success
            ? $"Valid deployment path: {result.Path}"
            : $"Invalid: {result.Error}";
    }

    private void Tick()
    {
        foreach (var sensor in Sensors)
        {
            sensor.RefreshStatus();

            if (!IsSimulating || sensor.Status == ConnectionStatus.Disconnected) continue;

            double baseline = sensor.Category switch
            {
                SensorCategory.Environmental => 45,
                SensorCategory.PowerConsumption => 220,
                _ => 1
            };

            double noise = (_rng.NextDouble() - 0.5) * 4;
            double spike = _rng.NextDouble() < 0.05 ? (_rng.NextDouble() > 0.5 ? 25 : -25) : 0;
            double value = baseline + noise + spike;

            sensor.PushReading(value);
            _ = PushTelemetrySafeAsync(sensor.MacAddress, sensor.Category.ToString(), (float)value);
        }
    }

    private async Task PushTelemetrySafeAsync(string deviceId, string metric, float value)
    {
        try
        {
            await _api.PushTelemetryAsync(deviceId, metric, value);
        }
        catch
        {
        }
    }
}
