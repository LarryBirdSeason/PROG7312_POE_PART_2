namespace SmartX.Core.Models;

public enum Severity
{
    Normal = 0,
    Warning = 1,
    Critical = 2
}

public enum ConnectionState
{
    Online = 0,
    Stale = 1,
    Offline = 2
}

public enum ValueKind
{
    Float = 0,
    Int = 1,
    Bool = 2
}

public enum CommandAction
{
    TurnOn = 0,
    TurnOff = 1,
    Toggle = 2
}

public enum InsightKind
{
    Pattern = 0,
    Forecast = 1,
    Fault = 2
}
