using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartX.Api;
using SmartX.Api.Services;
using SmartX.Core.Models;
using SmartX.Core.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://localhost:5080");

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSingleton<SensorStore>();
builder.Services.AddSingleton<TelemetryStore>();

builder.Services.AddSingleton<DeviceRegistry>();
builder.Services.AddSingleton<TelemetryHistory>();
builder.Services.AddSingleton<AlertTracker>();
builder.Services.AddSingleton<CommandHistory>();
builder.Services.AddSingleton<TelemetryPipeline>();
builder.Services.AddSingleton(new ActionPredictor(TimeSpan.FromMinutes(3)));
builder.Services.AddSingleton<CommandService>();
builder.Services.AddSingleton<InsightEngine>();
builder.Services.AddHostedService<TelemetryProcessorService>();
builder.Services.AddHostedService<DeviceMonitorService>();

var app = builder.Build();

DemoSeeder.SeedPredictor(app.Services.GetRequiredService<ActionPredictor>());

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow }));

app.MapPost("/api/sensors", (SensorRegistration reg, SensorStore store, DeviceRegistry registry) =>
{
    if (string.IsNullOrWhiteSpace(reg.MacAddress))
        return Results.BadRequest(new { error = "MacAddress is required." });

    var existing = store.All().FirstOrDefault(s =>
        string.Equals(s.MacAddress, reg.MacAddress, StringComparison.OrdinalIgnoreCase));

    if (existing is not null)
    {
        existing.Location = reg.Location;
        existing.Category = reg.Category;
        existing.DisplayName = reg.DisplayName;
        existing.DeviceKind = reg.DeviceKind;
        registry.Register(existing);
        return Results.Ok(existing);
    }

    store.Add(reg);
    registry.Register(reg);
    return Results.Created($"/api/sensors/{reg.Id}", reg);
});

app.MapGet("/api/sensors", (SensorStore store) => Results.Ok(store.All()));

app.MapGet("/api/sensors/{id:guid}", (Guid id, SensorStore store) =>
    store.Get(id) is { } sensor ? Results.Ok(sensor) : Results.NotFound());

app.MapPost("/api/sensors/{id:guid}/attachments", async (
    Guid id, IFormFile file, SensorStore store, IWebHostEnvironment env) =>
{
    var sensor = store.Get(id);
    if (sensor is null) return Results.NotFound();
    if (file.Length == 0) return Results.BadRequest("Empty file.");

    var uploadsDir = Path.Combine(env.ContentRootPath, "attachments", id.ToString());
    Directory.CreateDirectory(uploadsDir);

    var storedFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
    var fullPath = Path.Combine(uploadsDir, storedFileName);

    await using (var stream = File.Create(fullPath))
        await file.CopyToAsync(stream);

    var attachment = new SensorAttachment
    {
        FileName = file.FileName,
        ContentType = file.ContentType,
        SizeBytes = file.Length,
        StoredPath = fullPath,
        UploadedAt = DateTimeOffset.UtcNow
    };
    sensor.Attachments.Add(attachment);

    return Results.Created($"/api/sensors/{id}/attachments/{attachment.Id}", attachment);
}).DisableAntiforgery();

app.MapGet("/api/sensors/{id:guid}/attachments", (Guid id, SensorStore store) =>
    store.Get(id) is { } sensor ? Results.Ok(sensor.Attachments) : Results.NotFound());

app.MapPost("/api/telemetry", (TelemetryIngestDto dto, TelemetryPipeline pipeline, DeviceRegistry registry) =>
{
    TelemetryMessage message;
    try
    {
        message = (dto.ValueType ?? string.Empty).ToLowerInvariant() switch
        {
            "float" => FromFloat(dto, dto.Value.GetSingle()),
            "int" => FromInt(dto, dto.Value.GetInt32()),
            "bool" => FromBool(dto, dto.Value.GetBoolean()),
            _ => throw new ArgumentException($"Unsupported value type '{dto.ValueType}'.")
        };
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    var evaluation = message.Kind == ValueKind.Bool
        ? Evaluation.Normal
        : ThresholdRules.Evaluate(registry.ResolveKind(message.DeviceId, message.Metric), message.Value);

    var isCritical = evaluation.Severity == Severity.Critical;

    if (isCritical)
    {
        pipeline.EnqueuePriority(message, rank: 0);
    }
    else if (!pipeline.TryEnqueueStandard(message))
    {
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }

    var ack = new TelemetryAckDto(true, isCritical ? "priority" : "standard", registry.GetCommandedState(message.DeviceId));
    return Results.Accepted(value: ack);
});

app.MapGet("/api/telemetry/{deviceId}", (string deviceId, int? take, TelemetryHistory history) =>
    Results.Ok(history.Recent(deviceId, take ?? 100)));

app.MapPost("/api/telemetry/{deviceId}/batch", (
    string deviceId, string metric, double[][] batches, TelemetryStore store, TelemetryHistory history) =>
{
    var processor = new TelemetryBatchProcessor();
    processor.LoadBatches(batches);
    var readings = processor.Flatten(deviceId, metric);

    var at = DateTimeOffset.UtcNow.AddSeconds(-readings.Count);
    foreach (var reading in readings)
    {
        store.Add(reading);
        history.Add(deviceId, at, reading.Value);
        at = at.AddSeconds(1);
    }

    return Results.Ok(new { ingested = readings.Count });
});

app.MapPost("/api/deployment/validate", (DeploymentNode root) =>
    Results.Ok(DeploymentTreeValidator.Validate(root)));

app.MapGet("/api/devices", (string? search, SensorCategory? category, string? alert, DeviceRegistry registry) =>
    Results.Ok(registry.Query(search, category, alert)));

app.MapGet("/api/devices/{mac}", (string mac, DeviceRegistry registry) =>
    registry.Get(mac) is { } device ? Results.Ok(device) : Results.NotFound());

app.MapGet("/api/devices/{mac}/history", (string mac, int? take, TelemetryHistory history) =>
    Results.Ok(history.Recent(mac, take ?? 60)));

app.MapPost("/api/commands", (IssueCommandRequest request, CommandService commands) =>
{
    var result = commands.Execute(request.Mac, request.Action);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapPost("/api/commands/bulk", (BulkCommandRequest request, CommandService commands) =>
{
    var result = commands.ExecuteBulk(request.Kind, request.Action);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapPost("/api/commands/undo", (CommandService commands) =>
{
    var result = commands.Undo();
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapGet("/api/commands", (CommandHistory history) => Results.Ok(history.Snapshot()));

app.MapGet("/api/alerts", (AlertTracker alerts) => Results.Ok(alerts.Active()));

app.MapGet("/api/stats", (
    TelemetryPipeline pipeline, DeviceRegistry registry, AlertTracker alerts, CommandHistory commands) =>
{
    var s = pipeline.Snapshot();

    return Results.Ok(new PipelineStatsDto(
        s.StdDepth, s.PrioDepth, s.StdProcessed, s.PrioProcessed, s.PeakStd, s.PeakPrio,
        s.StdAvgMs, s.PrioAvgMs, s.Total, s.Rejected,
        registry.Count, alerts.Count, alerts.DuplicatesSuppressed, commands.UndoDepth,
        AggregateLoad(registry)));
});

app.MapGet("/api/insights", (InsightEngine engine) => Results.Ok(engine.Generate()));

app.MapGet("/api/insights/patterns", (InsightEngine engine) => Results.Ok(engine.LearnedPatterns()));

app.MapPost("/api/insights/events", (InsightEventRequest request, DeviceRegistry registry, ActionPredictor predictor) =>
{
    if (!string.Equals(request.Kind, "search", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "Only 'search' events are accepted." });

    var mac = request.Mac;

    if (string.IsNullOrWhiteSpace(mac) && !string.IsNullOrWhiteSpace(request.Detail))
    {
        var matches = registry.Query(request.Detail, null, null);
        if (matches.Count == 1) mac = matches[0].Mac;
    }

    if (!string.IsNullOrWhiteSpace(mac) && registry.Get(mac) is not null)
        predictor.Record(ActionKey.Search(mac));

    return Results.NoContent();
});

app.Run();

static TelemetryMessage FromFloat(TelemetryIngestDto dto, float value)
{
    var packet = new TelemetryPacket<float>(dto.DeviceId, dto.Metric, value);
    return new TelemetryMessage(packet.DeviceId, packet.Metric, packet.Value, ValueKind.Float, packet.Timestamp);
}

static TelemetryMessage FromInt(TelemetryIngestDto dto, int value)
{
    var packet = new TelemetryPacket<int>(dto.DeviceId, dto.Metric, value);
    return new TelemetryMessage(packet.DeviceId, packet.Metric, packet.Value, ValueKind.Int, packet.Timestamp);
}

static TelemetryMessage FromBool(TelemetryIngestDto dto, bool value)
{
    var packet = new TelemetryPacket<bool>(dto.DeviceId, dto.Metric, value);
    return new TelemetryMessage(packet.DeviceId, packet.Metric, packet.Value ? 1d : 0d, ValueKind.Bool, packet.Timestamp);
}

static double AggregateLoad(DeviceRegistry registry)
{
    var readings = registry.PowerReadings();
    if (readings.Count == 0) return 0;

    var total = readings[0];
    for (int i = 1; i < readings.Count; i++)
        total = total + readings[i];

    return total.Value;
}

public record TelemetryIngestDto(string DeviceId, string Metric, string ValueType, JsonElement Value);
