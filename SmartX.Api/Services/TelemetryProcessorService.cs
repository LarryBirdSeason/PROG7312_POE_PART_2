using SmartX.Core.Models;
using SmartX.Core.Services;

namespace SmartX.Api.Services;

public sealed class TelemetryProcessorService : BackgroundService
{
    private readonly TelemetryPipeline _pipeline;
    private readonly DeviceRegistry _registry;
    private readonly TelemetryHistory _history;
    private readonly AlertTracker _alerts;
    private readonly ActionPredictor _predictor;
    private readonly ILogger<TelemetryProcessorService> _logger;

    public TelemetryProcessorService(
        TelemetryPipeline pipeline,
        DeviceRegistry registry,
        TelemetryHistory history,
        AlertTracker alerts,
        ActionPredictor predictor,
        ILogger<TelemetryProcessorService> logger)
    {
        _pipeline = pipeline;
        _registry = registry;
        _history = history;
        _alerts = alerts;
        _predictor = predictor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _pipeline.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (!_pipeline.TryDequeue(out var message, out var fromPriority)) continue;

            var latencyMs = (DateTimeOffset.UtcNow - message.ReceivedAt).TotalMilliseconds;
            _pipeline.RecordProcessed(fromPriority, latencyMs);

            try
            {
                Process(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process telemetry from {Device}", message.DeviceId);
            }
        }
    }

    private void Process(TelemetryMessage message)
    {
        if (message.IsDisconnect)
        {
            HandleDisconnect(message);
            return;
        }

        var outcome = _registry.ApplyReading(message);
        _history.Add(outcome.Mac, message.ReceivedAt, message.Value);

        _alerts.Clear(AlertTracker.KeyFor(outcome.Mac, "Disconnected"));

        var evaluation = outcome.Evaluation;

        foreach (var type in AlertTracker.SeverityTypes)
        {
            if (type != evaluation.AlertType)
                _alerts.Clear(AlertTracker.KeyFor(outcome.Mac, type));
        }

        if (evaluation.AlertType is string alertType)
        {
            var alert = new AlertDto(
                AlertTracker.KeyFor(outcome.Mac, alertType),
                outcome.Mac,
                outcome.Name,
                alertType,
                evaluation.Severity,
                evaluation.Message,
                message.ReceivedAt);

            if (_alerts.TryRaise(alert) && evaluation.Severity == Severity.Critical)
                _predictor.Record(ActionKey.Alert(outcome.Mac, alertType));
        }
    }

    private void HandleDisconnect(TelemetryMessage message)
    {
        var name = _registry.NameOf(message.DeviceId);

        var alert = new AlertDto(
            AlertTracker.KeyFor(message.DeviceId, "Disconnected"),
            message.DeviceId,
            name,
            "Disconnected",
            Severity.Critical,
            $"No telemetry for over {DeviceRegistry.OfflineAfter.TotalSeconds:0} s",
            message.ReceivedAt);

        if (_alerts.TryRaise(alert))
            _predictor.Record(ActionKey.Alert(message.DeviceId, "Disconnected"));
    }
}
