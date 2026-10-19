using SmartX.Core.Models;

namespace SmartX.Core.Demo;

public sealed record DemoDevice(string Mac, string Name, string Kind, SensorCategory Category, string Location);

public static class DemoRoster
{
    public const string MoistureA = "24:6F:28:A1:00:01";
    public const string MoistureB = "24:6F:28:A1:00:02";
    public const string MoistureC = "24:6F:28:A1:00:03";
    public const string TempA = "24:6F:28:A1:00:04";
    public const string TempB = "24:6F:28:A1:00:05";
    public const string Meter1 = "24:6F:28:A1:00:06";
    public const string Meter2 = "24:6F:28:A1:00:07";
    public const string Meter3 = "24:6F:28:A1:00:08";
    public const string PumpA = "24:6F:28:A1:00:09";
    public const string PumpB = "24:6F:28:A1:00:0A";
    public const string Valve1 = "24:6F:28:A1:00:0B";
    public const string Valve2 = "24:6F:28:A1:00:0C";
    public const string Valve3 = "24:6F:28:A1:00:0D";
    public const string Relay1 = "24:6F:28:A1:00:0E";

    public static readonly IReadOnlyList<DemoDevice> Devices = new List<DemoDevice>
    {
        new(MoistureA, "Moisture Sensor A", "Moisture", SensorCategory.Environmental, "Bay 1 > Greenhouse > Farm Alpha"),
        new(MoistureB, "Moisture Sensor B", "Moisture", SensorCategory.Environmental, "Bay 2 > Greenhouse > Farm Alpha"),
        new(MoistureC, "Moisture Sensor C", "Moisture", SensorCategory.Environmental, "Bay 3 > Greenhouse > Farm Alpha"),
        new(TempA, "Temp Sensor A", "Temperature", SensorCategory.Environmental, "Bay 1 > Greenhouse > Farm Alpha"),
        new(TempB, "Temp Sensor B", "Temperature", SensorCategory.Environmental, "Bay 3 > Greenhouse > Farm Alpha"),
        new(Meter1, "Smart Meter 1", "Power", SensorCategory.PowerConsumption, "Main Board > Plant Room > Farm Alpha"),
        new(Meter2, "Smart Meter 2", "Power", SensorCategory.PowerConsumption, "Pump Board > Plant Room > Farm Alpha"),
        new(Meter3, "Smart Meter 3", "Power", SensorCategory.PowerConsumption, "Lighting Board > Plant Room > Farm Alpha"),
        new(PumpA, "Pump A", "Pump", SensorCategory.Actuator, "Bay 1-2 Loop > Plant Room > Farm Alpha"),
        new(PumpB, "Pump B", "Pump", SensorCategory.Actuator, "Bay 3 Loop > Plant Room > Farm Alpha"),
        new(Valve1, "Valve 1", "Valve", SensorCategory.Actuator, "Bay 1 > Greenhouse > Farm Alpha"),
        new(Valve2, "Valve 2", "Valve", SensorCategory.Actuator, "Bay 2 > Greenhouse > Farm Alpha"),
        new(Valve3, "Valve 3", "Valve", SensorCategory.Actuator, "Bay 3 > Greenhouse > Farm Alpha"),
        new(Relay1, "Relay 1", "Relay", SensorCategory.Actuator, "Pump Board > Plant Room > Farm Alpha"),
    };
}
