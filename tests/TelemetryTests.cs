using System;
using System.Linq;
using Otchlan.Sim;

namespace Otchlan.Tests;

/// <summary>Telemetria pokój po pokoju i profile bota.</summary>
public static class TelemetryTests
{
    [Test]
    public static void TelemetryMatchesRunStats()
    {
        var g = new RunSim(new[] { "rycerz" }, 1, 17) { Bench = true };
        var bot = new Bot(g, g.Players[0], 4, BotProfile.Casual);
        for (int i = 0; i < 60 * 240 && g.Depth < 3; i++)
        {
            g.Update(1f / 60, new[] { bot.Input() });
            while (g.Events.Count > 0)
            {
                var ev = g.Events.Dequeue();
                if (ev.Kind == RunEventKind.LevelUp)
                {
                    Items.ApplyUpgrade(ev.Player, Items.RollUpgrades(g.R, 0, ev.Player.Level)[0]);
                    ev.Player.PendingLevelUps--;
                }
            }
        }
        g.Telemetry.EndRoom(g, "quit");
        var logs = g.Telemetry.All;
        Assert.True(logs.Count >= 5, $"za mało wpisów telemetrii: {logs.Count}");
        Assert.True(logs.Any(l => l.Room == "Combat" && l.Kills > 0), "brak pokoju walki z zabójstwami");
        Assert.Near(g.Stats.DamageTaken, logs.Sum(l => l.Damage), 0.01);
        Assert.Equal(g.Stats.Kills, logs.Sum(l => l.Kills), "zabójstwa w telemetrii");
        Assert.True(logs.All(l => l.Seconds >= 0 && l.MaxHp > 0), "błędne pola wpisu");
    }

    [Test]
    public static void HumanProfilesMissSomeBullets()
    {
        Assert.True(BotProfile.Human.Notice < 1 && BotProfile.Human.Reaction > 0, "profil „człowiek” bez niedoskonałości");
        Assert.True(BotProfile.Casual.Notice < BotProfile.Human.Notice, "„niedzielny” powinien przeoczać więcej niż „człowiek”");
        Assert.True(BotProfile.ByName("human") == BotProfile.Human && BotProfile.ByName("niedzielny") == BotProfile.Casual, "nazwy profili");
    }
}
