using SmartX.Core.Demo;

namespace SmartX.Simulator;

internal sealed record Payload(string DeviceId, string Metric, string ValueType, object Value);

internal sealed class SimDevice
{
    private readonly World _world;
    private double _value;

    public DemoDevice Spec { get; }

    public SimDevice(DemoDevice spec, World world)
    {
        Spec = spec;
        _world = world;

        _value = spec.Kind switch
        {
            "Moisture" => 46 + Random.Shared.NextDouble() * 6,
            "Temperature" => 22 + Random.Shared.NextDouble() * 3,
            "Power" => BasePower(spec.Mac),
            _ => 0
        };
    }

    public static bool DefaultOn(string mac) =>
        mac == DemoRoster.Valve2 || mac == DemoRoster.Valve3 || mac == DemoRoster.Relay1;

    public Payload NextPayload()
    {
        switch (Spec.Kind)
        {
            case "Moisture":
                return new Payload(Spec.Mac, "moisture", "float", Math.Round(StepMoisture(), 2));
            case "Temperature":
                return new Payload(Spec.Mac, "temperature", "float", Math.Round(StepTemperature(), 2));
            case "Power":
                return new Payload(Spec.Mac, "power", "int", (int)Math.Round(StepPower()));
            default:
                return new Payload(Spec.Mac, Spec.Kind.ToLowerInvariant(), "bool",
                    _world.IsOn(Spec.Mac, DefaultOn(Spec.Mac)));
        }
    }

    private double StepMoisture()
    {
        var mac = Spec.Mac;

        if (_world.Active("stuck", mac)) return _value;

        var pump = mac == DemoRoster.MoistureC ? DemoRoster.PumpB : DemoRoster.PumpA;

        if (_world.IsOn(pump, false))
            _value = Math.Min(88, _value + 1.1 + Noise(0.2));
        else if (_world.Active("dry", mac))
            _value -= 1.4 + Noise(0.2);
        else
            _value += (50 - _value) * 0.01 + Noise(0.25);

        return Math.Clamp(_value, 0, 100);
    }

    private double StepTemperature()
    {
        const double baseline = 23;

        if (Spec.Mac == DemoRoster.TempA && _world.IsOn(DemoRoster.Valve1, false))
            _value = Math.Max(baseline - 1, _value - 1.8);
        else if (_world.Active("heat", Spec.Mac))
            _value += 1.4 + Noise(0.2);
        else
            _value += (baseline - _value) * 0.08 + Noise(0.15);

        return Math.Clamp(_value, -5, 60);
    }

    private double StepPower()
    {
        var mac = Spec.Mac;
        var target = BasePower(mac);

        var shed = mac == DemoRoster.Meter2 && !_world.IsOn(DemoRoster.Relay1, true);
        if (shed) target *= 0.35;

        if (!shed && _world.Active("spike", mac))
            _value = 3300 + Noise(300);
        else
            _value += (target - _value) * 0.3 + Noise(20);

        return Math.Max(0, _value);
    }

    private static double BasePower(string mac) =>
        mac == DemoRoster.Meter1 ? 420d : mac == DemoRoster.Meter2 ? 640d : 310d;

    private static double Noise(double amplitude) => (Random.Shared.NextDouble() - 0.5) * 2 * amplitude;
}
