using SmartX.Core.Demo;
using SmartX.Core.Models;
using SmartX.Core.Services;

namespace SmartX.Api.Services;

public static class DemoSeeder
{
    public static void SeedPredictor(ActionPredictor predictor)
    {
        var start = DateTimeOffset.UtcNow.AddDays(-3);

        for (int i = 0; i < 6; i++)
        {
            var t = start.AddHours(i * 9);
            predictor.Record(ActionKey.Alert(DemoRoster.MoistureB, "CriticalLow"), t);
            predictor.Record(ActionKey.Search(DemoRoster.PumpA), t.AddSeconds(14));
            predictor.Record(ActionKey.Command(DemoRoster.PumpA, CommandAction.TurnOn), t.AddSeconds(27));
        }

        for (int i = 0; i < 5; i++)
        {
            var t = start.AddHours(4 + i * 11);
            predictor.Record(ActionKey.Alert(DemoRoster.Meter2, "CriticalHigh"), t);
            predictor.Record(ActionKey.Search(DemoRoster.Relay1), t.AddSeconds(10));
            predictor.Record(ActionKey.Command(DemoRoster.Relay1, CommandAction.TurnOff), t.AddSeconds(22));
        }

        for (int i = 0; i < 4; i++)
        {
            var t = start.AddHours(1 + i * 16);
            predictor.Record(ActionKey.Alert(DemoRoster.TempA, "CriticalHigh"), t);
            predictor.Record(ActionKey.Command(DemoRoster.Valve1, CommandAction.TurnOn), t.AddSeconds(18));
        }
    }
}
