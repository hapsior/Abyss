using System.Linq;
using Otchlan.Core;
using Otchlan.Save;
using Otchlan.Sim;

namespace Otchlan.Tests;

public static class SaveTests
{
    private static RunSim PlayTo(int depth, string[] chars, ulong seed)
    {
        var g = new RunSim(chars, 1, seed) { God = true };
        var bots = g.Players.Select((p, i) => new Bot(g, p, (ulong)(i + 3))).ToList();
        float t = 0;
        while (g.Depth < depth && t < 60 * 20)
        {
            g.Update(1f / 60, bots.Select(b => b.Input()).ToList());
            t += 1f / 60;
            while (g.Events.Count > 0)
            {
                var ev = g.Events.Dequeue();
                if (ev.Kind == RunEventKind.LevelUp)
                {
                    var ups = Items.RollUpgrades(g.R, ev.Player.Luck, ev.Player.Level);
                    Items.ApplyUpgrade(ev.Player, ups[0]);
                    ev.Player.PendingLevelUps--;
                }
            }
        }
        return g;
    }

    [Test]
    public static void SaveRoundtripKeepsRun()
    {
        var g = PlayTo(3, new[] { "mag" }, 77);
        Assert.True(g.Depth >= 3, "bot powinien dojść do piętra 3");
        var json = System.Text.Json.JsonSerializer.Serialize(SaveSystem.Capture(g), JsonStore.Options);
        var back = System.Text.Json.JsonSerializer.Deserialize<RunSave>(json, JsonStore.Options);
        var r = SaveSystem.Restore(back);
        Assert.Equal(g.Depth, r.Depth);
        Assert.Equal(g.Coins, r.Coins);
        Assert.Equal(g.Floor.Rooms.Count, r.Floor.Rooms.Count);
        Assert.Equal(g.Room.Gx, r.Room.Gx);
        var a = g.Players[0];
        var b = r.Players[0];
        Assert.Equal(string.Join(",", a.Items), string.Join(",", b.Items));
        Assert.Equal(a.Level, b.Level);
        Assert.Near(a.Damage, b.Damage, 1e-4);
        Assert.Near(a.MaxHp, b.MaxHp, 1e-4);
        Assert.Equal(g.R.GetState().State, r.R.GetState().State);
        // stan pokoi: odwiedzone i przeszkody
        foreach (var (k, room) in g.Floor.Rooms)
        {
            var rr = r.Floor.Rooms[k];
            Assert.Equal(room.Visited, rr.Visited);
            Assert.Equal(room.Cleared, rr.Cleared);
            Assert.Equal(room.Obstacles().Count(), rr.Obstacles().Count());
        }
    }

    [Test]
    public static void DownedPartnerStaysDownedAfterLoad()
    {
        var g = new RunSim(new[] { "rycerz", "mag" }, 1, 9);
        g.Players[1].Hp = 1;
        g.Players[1].Hurt(50, g, 0, 0);
        Assert.True(g.Players[1].Downed, "gracz 2 powinien być powalony");
        var r = SaveSystem.Restore(SaveSystem.Capture(g));
        Assert.True(r.Players[1].Downed, "po wczytaniu gracz 2 dalej powalony (błąd z wersji 1.1)");
        Assert.True(!r.Players[0].Downed, "gracz 1 aktywny");
    }

    [Test]
    public static void SlotsWriteListAndDelete()
    {
        var g = new RunSim(new[] { "lowczyni" }, 2, 5);
        Assert.True(SaveSystem.Save(3, g), "zapis do slotu 3");
        var info = SaveSystem.Info(3);
        Assert.True(!info.Empty, "slot 3 zajęty");
        Assert.Equal(1, info.Depth);
        Assert.Equal(2, info.Difficulty);
        var loaded = SaveSystem.Load(3);
        Assert.True(loaded != null && loaded.Players[0].Key == "lowczyni", "wczytanie slotu");
        SaveSystem.Delete(3);
        Assert.True(SaveSystem.Info(3).Empty, "slot 3 pusty po usunięciu");
    }
}
