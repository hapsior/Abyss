using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Otchlan.Core;
using Otchlan.Sim;

namespace Otchlan.Tests;

public static class SimTests
{
    [Test]
    public static void ContentLoads()
    {
        var db = Content.Db;
        Assert.Equal(7, db.Characters.Count);
        Assert.Equal(74, db.Items.Count);
        Assert.Equal(8, db.Biomes.Count);
        Assert.True(db.EnemyByKey.ContainsKey("sniper"));
        Assert.Equal("lich", db.BiomeFor(10).Boss);
        Assert.Equal("abyss", db.BiomeFor(40).Boss);
        Assert.Equal("piwnice", db.BiomeFor(41).Key, "Głębia zaczyna krainy od nowa");
    }

    [Test]
    public static void FloorsAreConnectedAndHaveExits()
    {
        var diff = Content.Db.Difficulties[1];
        for (int depth = 1; depth <= 42; depth++)
        {
            var f = Floor.Generate(depth, diff, new Rng((ulong)(depth * 7919)));
            Assert.True(f.Rooms.Count >= 6, $"piętro {depth}: za mało pokoi ({f.Rooms.Count})");
            bool boss = f.Rooms.Values.Any(r => r.Kind == RoomKind.Boss), exit = f.Rooms.Values.Any(r => r.Kind == RoomKind.Exit);
            Assert.Equal(depth % 5 == 0, boss, $"piętro {depth}: boss");
            Assert.Equal(depth % 5 != 0, exit, $"piętro {depth}: wyjście");
            Assert.True(f.Rooms.Values.Any(r => r.Kind == RoomKind.Shop), $"piętro {depth}: sklep");
            foreach (var r in f.Rooms.Values.Where(r => r.Kind is RoomKind.Combat or RoomKind.Exit))
                Assert.True(r.Waves.Count > 0 && r.Waves.All(w => w.Count > 0), $"piętro {depth}: pusta fala");
        }
    }

    [Test]
    public static void SameSeedSameFloor()
    {
        var diff = Content.Db.Difficulties[1];
        var a = Floor.Generate(7, diff, new Rng(99));
        var b = Floor.Generate(7, diff, new Rng(99));
        Assert.Equal(a.Rooms.Count, b.Rooms.Count);
        foreach (var (k, r) in a.Rooms)
        {
            Assert.Equal(r.Kind, b.Rooms[k].Kind);
            Assert.Equal(string.Join(",", r.Waves.SelectMany(w => w)), string.Join(",", b.Rooms[k].Waves.SelectMany(w => w)));
        }
    }

    [Test]
    public static void ItemsAndUpgradesApply()
    {
        var p = new PlayerSim(Content.Db.CharByKey["mag"]);
        var g = new RunSim(new[] { "mag" }, 1, 5, startFloor: false);
        float dmg0 = p.Damage;
        p.ApplyItem(Content.Db.ItemByKey["ostry_kamien"], g);
        Assert.Near(dmg0 + 3, p.Damage, 1e-4);
        float hp0 = p.MaxHp;
        p.ApplyItem(Content.Db.ItemByKey["serce"], g);
        Assert.Near(hp0 + 25, p.MaxHp, 1e-4);
        var ups = Items.RollUpgrades(new Rng(3), 0, 5);
        Assert.Equal(3, ups.Count);
        Assert.True(ups.All(u => u.Rarity >= 1), "co 5. poziom ulepszenia co najmniej niezwykłe");
        float fm = p.FireMult;
        Items.ApplyUpgrade(p, new Upgrade { Def = Content.Db.Upgrades.First(u => u.Key == "furia"), Rarity = 0, Value = 10 });
        Assert.Near(fm * 1.1, p.FireMult, 1e-4);
    }

    /// <summary>Bot przechodzi kilka pięter w trybie nieśmiertelności – rdzeń pętli gry działa bez wyjątków.</summary>
    [Test]
    public static void BotPlaysThroughFloors()
    {
        var g = new RunSim(new[] { "rycerz" }, 1, 2024) { God = true };
        var bot = new Bot(g, g.Players[0], 7);
        const float dt = 1f / 60;
        float simTime = 0;
        int levelUps = 0;
        while (g.Depth < 6 && simTime < 60 * 25)
        {
            g.Update(dt, new[] { bot.Input() });
            simTime += dt;
            while (g.Events.Count > 0)
            {
                var ev = g.Events.Dequeue();
                if (ev.Kind == RunEventKind.LevelUp)
                {
                    levelUps++;
                    var p = ev.Player;
                    var ups = Items.RollUpgrades(g.R, p.Luck, p.Level, banned: p.Banished);
                    Items.ApplyUpgrade(p, ups[0]);
                    p.PendingLevelUps--;
                }
            }
        }
        GD.Print($"    bot: piętro {g.Depth}, poziom {g.Players[0].Level}, zabici {g.Stats.Kills}, przedmioty {g.Players[0].Items.Count}, czas gry {simTime / 60:0.0} min, bossowie {g.Stats.Bosses}");
        var pl = g.Players[0];
        Assert.True(g.Depth >= 6, $"bot utknął na piętrze {g.Depth} (pokój {g.Room.Kind} ({g.Room.Gx},{g.Room.Gy}) czysty={g.Room.Cleared}, " +
            $"wrogów {g.Enemies.Count}, spawny {g.Spawns.Count}, znajdźki {g.Room.Pickups.Count}, piedestały {g.Room.Pedestals.Count}, " +
            $"gracz ({pl.X:0},{pl.Y:0}), drzwi {string.Join(",", g.Room.Doors.Keys)}, fale {g.WaveIdx}/{g.Room.Waves.Count}, stan {g.State}, monety {g.Coins})");
        Assert.True(g.Stats.Bosses >= 1, "bot powinien pokonać bossa piętra 5");
        Assert.True(levelUps > 3, "awanse");
    }
}
