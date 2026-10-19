using System.Net.Http.Json;
using SmartX.Core.Demo;
using SmartX.Core.Models;
using SmartX.Simulator;

var baseUrl = args.Length > 0 ? args[0] : "http://localhost:5080";

using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(5) };
using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var world = new World();
var devices = DemoRoster.Devices.Select(d => new SimDevice(d, world)).ToList();

Console.WriteLine($"Smart-X device simulator -> {baseUrl}");
Console.WriteLine($"{devices.Count} virtual devices. Press Ctrl+C to stop.");

try
{
    await RegisterAllAsync(http, cts.Token);

    var tasks = new List<Task>();
    foreach (var device in devices)
        tasks.Add(RunDeviceAsync(device, http, world, cts.Token));

    tasks.Add(RunScenariosAsync(world, cts.Token));
    tasks.Add(ReportAsync(cts.Token));

    await Task.WhenAll(tasks);
}
catch (OperationCanceledException)
{
}

Console.WriteLine("Simulator stopped.");

static async Task RegisterAllAsync(HttpClient http, CancellationToken ct)
{
    foreach (var spec in DemoRoster.Devices)
    {
        var registration = new SensorRegistration
        {
            MacAddress = spec.Mac,
            Location = spec.Location,
            Category = spec.Category,
            DisplayName = spec.Name,
            DeviceKind = spec.Kind
        };

        while (true)
        {
            try
            {
                var response = await http.PostAsJsonAsync("/api/sensors", registration, ApiJson.Options, ct);
                response.EnsureSuccessStatusCode();
                break;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"API not ready ({ex.Message}). Retrying in 2 s...");
                await Task.Delay(2000, ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Console.WriteLine("API timed out. Retrying in 2 s...");
                await Task.Delay(2000, ct);
            }
        }
    }

    Console.WriteLine($"Registered {DemoRoster.Devices.Count} devices.");
}

static async Task RunDeviceAsync(SimDevice device, HttpClient http, World world, CancellationToken ct)
{
    await Task.Delay(Random.Shared.Next(0, 1000), ct);

    while (!ct.IsCancellationRequested)
    {
        try
        {
            if (!world.Active("outage", device.Spec.Mac))
            {
                var payload = device.NextPayload();
                using var response = await http.PostAsJsonAsync("/api/telemetry", payload, ApiJson.Options, ct);

                if (response.IsSuccessStatusCode)
                {
                    Interlocked.Increment(ref Stats.Sent);

                    var ack = await response.Content.ReadFromJsonAsync<TelemetryAckDto>(ApiJson.Options, ct);
                    if (ack?.DesiredState is bool desired)
                        world.SetCommanded(device.Spec.Mac, desired);
                }
                else
                {
                    Interlocked.Increment(ref Stats.Failed);
                }
            }

            await Task.Delay(1000, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            break;
        }
        catch (Exception)
        {
            Interlocked.Increment(ref Stats.Failed);
            await Task.Delay(2000, ct);
        }
    }
}

static async Task RunScenariosAsync(World world, CancellationToken ct)
{
    var script = new (int AfterSeconds, string Scenario, string Mac, int Duration)[]
    {
        (20, "dry", DemoRoster.MoistureB, 25),
        (60, "outage", DemoRoster.TempB, 22),
        (100, "spike", DemoRoster.Meter2, 12),
        (140, "heat", DemoRoster.TempA, 16),
        (180, "stuck", DemoRoster.MoistureC, 25),
    };

    var started = DateTimeOffset.UtcNow;
    foreach (var step in script)
    {
        var wait = started.AddSeconds(step.AfterSeconds) - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);

        Fire(world, step.Scenario, step.Mac, step.Duration);
    }

    while (!ct.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(35, 70)), ct);

        var pick = script[Random.Shared.Next(script.Length)];
        Fire(world, pick.Scenario, pick.Mac, pick.Duration);
    }
}

static void Fire(World world, string scenario, string mac, int seconds)
{
    world.Trigger(scenario, mac, seconds);

    var name = DemoRoster.Devices.First(d => d.Mac == mac).Name;
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] scenario '{scenario}' on {name} for {seconds} s");
}

static async Task ReportAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        await Task.Delay(10_000, ct);

        Console.WriteLine(
            $"[{DateTime.Now:HH:mm:ss}] sent {Interlocked.Read(ref Stats.Sent)} ok, {Interlocked.Read(ref Stats.Failed)} failed");
    }
}
