using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Otchlan.Sim;

namespace Otchlan.Tests;

public static class EnemyTests
{
    private static readonly string[] Keys =
        { "slime", "big_slime", "bat", "shooter", "charger", "turret", "bomber", "summoner", "golem", "sniper", "ghost", "spinner", "imp" };

    /// <summary>Pokój walki bez przeszkód i wrogów, gracz na środku (nieśmiertelny, nie rusza się).</summary>
    private static RunSim ArenaSim(ulong seed, int diff = 1)
    {
        var g = new RunSim(new[] { "rycerz" }, diff, seed) { God = true };
        var room = g.Floor.Rooms.Values.First(r => r.Kind == RoomKind.Combat);
        g.EnterRoom(room, null);
        g.Room.Waves.Clear();
        g.Enemies.Clear();
        g.Spawns.Clear();
        Array.Clear(g.Room.Grid);
        g.Players[0].X = C.RCX;
        g.Players[0].Y = C.RCY;
        return g;
    }

    [Test]
    public static void AllEnemyKeysHaveOwnClass()
    {
        var db = Content.Db;
        Assert.Equal(13, db.Enemies.Count, "liczba wrogów w data/enemies.json");
        foreach (var def in db.Enemies)
        {
            Assert.True(EnemyFactory.Has(def.Key), $"brak klasy dla '{def.Key}'");
            var e = EnemyFactory.Create(def.Key);
            Assert.Equal(def.Cls, e.GetType().Name, $"klasa dla '{def.Key}'");
        }
        foreach (var k in Keys) Assert.True(EnemyFactory.Has(k), k);
    }

    [Test]
    public static void EachEnemyRunsTwentySeconds()
    {
        const float dt = 1f / 60;
        foreach (var key in Keys)
            foreach (int diff in new[] { 0, 3 })
            {
                var g = ArenaSim(100 + (ulong)diff, diff);
                var e = g.SpawnEnemy(key, C.RCX + 300, C.RCY - 100);
                Assert.True(e != null, $"nie udało się stworzyć {key}");
                Assert.Equal(key, e.Key);
                for (int i = 0; i < 20 * 60; i++)
                {
                    g.Update(dt, new SimInput[] { new() });
                    if (g.Enemies.Count == 0) break;
                    foreach (var en in g.Enemies)
                        Assert.True(!float.IsNaN(en.X) && !float.IsNaN(en.Y), $"{key}: NaN w pozycji");
                }
            }
    }

    [Test]
    public static void SummonerCapsMinions()
    {
        const float dt = 1f / 60;
        var g = ArenaSim(7);
        var s = g.SpawnEnemy("summoner", C.RCX + 350, C.RCY);
        s.Fr = 4; // szybkie przyzywanie
        int max = 0, seen = 0;
        for (int i = 0; i < 40 * 60; i++)
        {
            g.Update(dt, new SimInput[] { new() });
            foreach (var m in g.Enemies) if (m.Summoner == s) m.Hp = m.MaxHp = 1e9f; // żeby nie ginęły
            int mine = g.Enemies.Count(m => m.Summoner == s);
            max = Math.Max(max, mine);
            seen = Math.Max(seen, mine);
            if (!s.Alive) break;
        }
        Assert.True(seen >= 2, $"nekromanta powinien przyzwać sługi (było {seen})");
        Assert.True(max <= Otchlan.Sim.Enemies.Summoner.MaxMinions, $"za dużo sług: {max}");
        Assert.True(g.Enemies.Where(m => m.Summoner == s).All(m => m.Minion));
    }

    [Test]
    public static void BomberDetonatesNearPlayer()
    {
        const float dt = 1f / 60;
        var g = ArenaSim(11);
        var b = g.SpawnEnemy("bomber", C.RCX + 120, C.RCY);
        for (int i = 0; i < 5 * 60 && b.Alive; i++)
            g.Update(dt, new SimInput[] { new() });
        Assert.True(!b.Alive, "wybuchacz powinien eksplodować");
        Assert.True(g.Stats.GodHits > 0, "wybuch powinien zranić gracza (God liczy trafienia)");
    }

    [Test]
    public static void ChargerChargesOnlyWithLineOfSight()
    {
        const float dt = 1f / 60;
        // mur z kamieni między dzikiem a graczem: bez linii strzału nie wolno zacząć szarży
        var g = ArenaSim(21);
        for (int r = 0; r < C.GRows; r++)
            g.Room.Grid[r, 12] = new Obstacle(ObstacleKind.Rock, 12, r);
        var e = g.SpawnEnemy("charger", C.RCX + 150, C.RCY);
        for (int i = 0; i < 8 * 60; i++)
        {
            g.Update(dt, new SimInput[] { new() });
            Assert.True(e.State is "walk", $"dzik zaczął szarżę bez linii strzału (stan {e.State})");
        }
        // czysta arena: szarża w końcu następuje
        var g2 = ArenaSim(22);
        var e2 = g2.SpawnEnemy("charger", C.RCX + 300, C.RCY);
        bool charged = false;
        for (int i = 0; i < 10 * 60 && !charged; i++)
        {
            g2.Update(dt, new SimInput[] { new() });
            charged = e2.State == "windup";
        }
        Assert.True(charged, "dzik nigdy nie zaczął szarży na otwartej arenie");
    }

    [Test]
    public static void SniperPeeksOutFromCover()
    {
        const float dt = 1f / 60;
        var g = ArenaSim(31);
        // pionowy mur z kamieni (kolumna 12) z przejściami u góry (wiersze 0-1) i u dołu (10-11)
        for (int r = 2; r <= 9; r++)
            g.Room.Grid[r, 12] = new Obstacle(ObstacleKind.Rock, 12, r);
        var p = g.Players[0];
        p.X = C.RX0 + 110;
        p.Y = C.RCY;
        var sn = g.SpawnEnemy("sniper", C.RX1 - 110, C.RCY);
        Assert.True(!g.Room.LosThick(sn.X, sn.Y, p.X, p.Y), "na starcie mur powinien zasłaniać gracza");
        var seen = new HashSet<Bullet>();
        int fired = 0;
        for (int i = 0; i < 30 * 60; i++)
        {
            g.Update(dt, new SimInput[] { new() });
            foreach (var b in g.Bullets)
                if (b.Source == "sniper" && !b.Friendly && seen.Add(b))
                {
                    fired++;
                    // początek pocisku = pozycja sprzed ruchu w tej klatce
                    float sx = b.X - b.Vx * dt, sy = b.Y - b.Vy * dt;
                    Assert.True(g.Room.LosThick(sx, sy, p.X, p.Y),
                        $"snajper strzelił bez czystej linii strzału z ({sx:0},{sy:0}) w gracza ({p.X:0},{p.Y:0})");
                }
            if (fired >= 3) break;
        }
        Assert.True(fired >= 1, "snajper nigdy nie wychylił się zza osłony, żeby strzelić");
    }

    [Test]
    public static void ImpTeleportsToLineOfSight()
    {
        const float dt = 1f / 60;
        var g = ArenaSim(41);
        for (int r = 2; r <= 9; r++)
            g.Room.Grid[r, 12] = new Obstacle(ObstacleKind.Rock, 12, r);
        var p = g.Players[0];
        p.X = C.RX0 + 110;
        p.Y = C.RCY;
        var imp = g.SpawnEnemy("imp", C.RX1 - 110, C.RCY);
        var seen = new HashSet<Bullet>();
        int fired = 0;
        for (int i = 0; i < 20 * 60 && fired < 5; i++)
        {
            g.Update(dt, new SimInput[] { new() });
            foreach (var b in g.Bullets)
                if (b.Source == "imp" && seen.Add(b))
                {
                    fired++;
                    Assert.True(g.Room.LosThick(imp.X, imp.Y, p.X, p.Y) || imp.State != "idle",
                        "chochlik strzelił bez linii strzału");
                }
        }
        Assert.True(fired > 0, "chochlik powinien się teleportować na pozycję z liniią strzału i strzelić");
    }
}
