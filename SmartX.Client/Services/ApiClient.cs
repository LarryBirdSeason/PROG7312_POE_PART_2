using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using SmartX.Core.Models;

namespace SmartX.Client.Services;

public class ApiClient
{
    private static readonly JsonSerializerOptions Json = ApiJson.Options;

    private readonly HttpClient _http;

    public ApiClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(3) };
    }

    public Task<HttpResponseMessage> RegisterSensorAsync(SensorRegistration registration)
        => _http.PostAsJsonAsync("/api/sensors", registration);

    public Task<HttpResponseMessage> PushTelemetryAsync(string deviceId, string metric, float value)
        => _http.PostAsJsonAsync("/api/telemetry", new
        {
            DeviceId = deviceId,
            Metric = metric,
            ValueType = "float",
            Value = value
        });

    public async Task<HttpResponseMessage> UploadAttachmentAsync(Guid sensorId, string filePath)
    {
        using var content = new MultipartFormDataContent();
        var bytes = await File.ReadAllBytesAsync(filePath);
        var fileContent = new ByteArrayContent(bytes);
        content.Add(fileContent, "file", Path.GetFileName(filePath));
        return await _http.PostAsync($"/api/sensors/{sensorId}/attachments", content);
    }

    public Task<HttpResponseMessage> ValidateDeploymentAsync(DeploymentNode root)
        => _http.PostAsJsonAsync("/api/deployment/validate", root);

    public Task<List<DeviceDto>> GetDevicesAsync(string? search, string? category, string? alert)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) query.Add("search=" + Uri.EscapeDataString(search.Trim()));
        if (!string.IsNullOrWhiteSpace(category)) query.Add("category=" + Uri.EscapeDataString(category));
        if (!string.IsNullOrWhiteSpace(alert)) query.Add("alert=" + Uri.EscapeDataString(alert));

        var url = "/api/devices" + (query.Count > 0 ? "?" + string.Join("&", query) : string.Empty);
        return GetAsync<List<DeviceDto>>(url);
    }

    public Task<List<HistoryPointDto>> GetHistoryAsync(string mac, int take)
        => GetAsync<List<HistoryPointDto>>($"/api/devices/{Uri.EscapeDataString(mac)}/history?take={take}");

    public Task<PipelineStatsDto> GetStatsAsync() => GetAsync<PipelineStatsDto>("/api/stats");

    public Task<List<AlertDto>> GetAlertsAsync() => GetAsync<List<AlertDto>>("/api/alerts");

    public Task<List<InsightDto>> GetInsightsAsync() => GetAsync<List<InsightDto>>("/api/insights");

    public Task<List<PatternDto>> GetPatternsAsync() => GetAsync<List<PatternDto>>("/api/insights/patterns");

    public Task<List<CommandRecordDto>> GetCommandsAsync() => GetAsync<List<CommandRecordDto>>("/api/commands");

    public async Task<CommandResultDto> IssueCommandAsync(string mac, CommandAction action)
    {
        using var response = await _http.PostAsJsonAsync("/api/commands", new IssueCommandRequest(mac, action), Json);
        return await ReadResultAsync(response);
    }

    public async Task<CommandResultDto> BulkCommandAsync(string kind, CommandAction action)
    {
        using var response = await _http.PostAsJsonAsync("/api/commands/bulk", new BulkCommandRequest(kind, action), Json);
        return await ReadResultAsync(response);
    }

    public async Task<CommandResultDto> UndoCommandAsync()
    {
        using var response = await _http.PostAsync("/api/commands/undo", null);
        return await ReadResultAsync(response);
    }

    public async Task RecordSearchAsync(string? mac, string? detail)
    {
        using var response = await _http.PostAsJsonAsync(
            "/api/insights/events", new InsightEventRequest("search", mac, detail), Json);
    }

    private async Task<T> GetAsync<T>(string url) where T : class
    {
        var result = await _http.GetFromJsonAsync<T>(url, Json);
        return result ?? throw new InvalidOperationException($"Empty response from {url}");
    }

    private static async Task<CommandResultDto> ReadResultAsync(HttpResponseMessage response)
    {
        var result = await response.Content.ReadFromJsonAsync<CommandResultDto>(Json);
        return result ?? new CommandResultDto(false, $"Unexpected response ({(int)response.StatusCode}).", null);
    }
}
