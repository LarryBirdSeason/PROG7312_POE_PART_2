using SmartX.Core.Models;

namespace SmartX.Api.Services;

public sealed class DeviceMonitorService : BackgroundService
{
    private readonly DeviceRegistry _registry;
    private readonly AlertTracker _alerts;
    private readonly TelemetryPipeline _pipeline;

    public DeviceMonitorService(DeviceRegistry registry, AlertTracker alerts, TelemetryPipeline pipeline)
    {
        _registry = registry;
        _alerts = alerts;
        _pipeline = pipeline;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var now = DateTimeOffset.UtcNow;

                foreach (var device in _registry.OfflineDevices(now))
                {
                    var key = AlertTracker.KeyFor(device.Mac, "Disconnected");
                    if (_alerts.IsActive(key)) continue;

                    var message = new TelemetryMessage(device.Mac, "disconnect", 0, ValueKind.Bool, now, IsDisconnect: true);
                    _pipeline.EnqueuePriority(message, rank: 1);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
