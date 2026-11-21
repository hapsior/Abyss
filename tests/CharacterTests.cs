using System;
using System.Linq;
using Godot;
using Otchlan.Sim;

namespace Otchlan.Tests;

/// <summary>Testy postaci: każda postać strzela i używa umiejętności bez błędów, mechaniki nowych postaci działają.</summary>
public static class CharacterTests
{
    /// <summary>Pusty pokój walki z kilkoma wrogami w zasięgu, gracz na środku (nieśmiertelny).</summary>
    private static RunSim Arena(string key, ulong seed = 31, int enemies = 4)
    {
        var g = new RunSim(new[] { key }, 1, seed) { God = true };
        var room = g.Floor.Rooms.Values.First(r => r.Kind == RoomKind.Combat);
        g.EnterRoom(room, null);
        g.Room.Waves.Clear();
        g.Enemies.Clear();
        g.Spawns.Clear();
        Array.Clear(g.Room.Grid);
        var p = g.Players[0];
        p.X = C.RCX;
        p.Y = C.RCY;
        for (int i = 0; i < enemies; i++)
        {
            var e = g.SpawnEnemy("golem", C.RCX + 220 + i * 40, C.RCY - 90 + i * 60);
            e.Grace = 0;
        }
        return g;
    }

    private static SimInput Aimed(RunSim g, bool fire, bool ability = false)
    {
        var p = g.Players[0];
        var t = g.Enemies.Count > 0 ? new Vector2(g.Enemies[0].X, g.Enemies[0].Y) : new Vector2(C.RCX + 200, C.RCY);
        return new SimInput { Aim = MathF.Atan2(t.Y - p.Y, t.X - p.X), Target = t, Fire = fire, Ability = ability };
    }

    private static void Run(RunSim g, float seconds, Func<float, SimInput> input)
    {
        const float dt = 1f / 60;
        for (float t = 0; t < seconds; t += dt)
        {
            g.Update(dt, new[] { input(t) });
            g.Events.Clear();
        }
    }

    [Test]
    public static void EveryCharacterShootsAndUsesAbility()
    {
        foreach (var def in Content.Db.Characters)
        {
            var g = Arena(def.Key);
            float hp0 = g.Enemies.Sum(e => e.Hp);
            Run(g, 4, t => Aimed(g, true, t > 1 && t < 1.05f));
            Assert.True(g.Stats.Abilities >= 1, $"{def.Key}: umiejętność nie została użyta");
            float hp1 = g.Enemies.Where(e => e.Alive).Sum(e => e.Hp);
            Assert.True(hp1 < hp0, $"{def.Key}: wrogowie nie otrzymali obrażeń");
        }
    }

    [Test]
    public static void EngineerSentryShoots()
    {
        var g = Arena("inzynier");
        Run(g, 0.1f, _ => Aimed(g, false, true));
        var sentry = g.Effects.OfType<Sentry>().FirstOrDefault();
        Assert.True(sentry != null, "brak wieżyczki");
        int before = g.Bullets.Count(b => b.Friendly && b.Source == "sentry");
        bool fired = false;
        Run(g, 2, _ =>
        {
            fired |= g.Bullets.Any(b => b.Friendly && b.Source == "sentry");
            return Aimed(g, false);
        });
        Assert.True(fired || before > 0, "wieżyczka nie strzela");
        Run(g, 12, _ => Aimed(g, false));
        Assert.True(!g.Effects.OfType<Sentry>().Any(s => s.Alive), "wieżyczka nie znika po czasie");
    }

    [Test]
    public static void ShamanTotemStrikes()
    {
        var g = Arena("szaman");
        float hp0 = g.Enemies.Sum(e => e.Hp);
        Run(g, 0.1f, _ => Aimed(g, false, true));
        Assert.True(g.Effects.OfType<Totem>().Any(), "brak totemu");
        Run(g, 3, _ => Aimed(g, false));
        Assert.True(g.Enemies.Where(e => e.Alive).Sum(e => e.Hp) < hp0, "totem nie zadaje obrażeń");
    }

    [Test]
    public static void MonkBlinksAndClearsBullets()
    {
        var g = Arena("mnich", enemies: 0);
        var p = g.Players[0];
        g.Bullets.Add(new Bullet(C.RCX + 230, C.RCY + 20, 0, 0, 5, 7, Colors.Red, false));
        Run(g, 0.05f, _ => new SimInput { Aim = 0, Move = Vector2.Right, Ability = true });
        Assert.True(p.X > C.RCX + 150, $"mnich nie przeskoczył (x = {p.X - C.RCX:0})");
        Assert.True(g.Bullets.All(b => b.Friendly || !b.Alive), "fala ki nie zniszczyła pocisku");
    }

    [Test]
    public static void GunslingerReloadsAndLastRoundCrits()
    {
        var g = Arena("rewolwerowiec", enemies: 0);
        var p = g.Players[0];
        p.Crit = 0;
        int shots = 0, crits = 0;
        float lastCd = 0;
        // strzela bez przerwy: po 6 strzałach przeładowanie (dłuższa przerwa)
        for (int i = 0; i < 400 && shots < 6; i++)
        {
            int before = g.Bullets.Count;
            g.Update(1f / 60, new[] { new SimInput { Aim = 0, Fire = true } });
            var fresh = g.Bullets.Skip(before).Where(b => b.Friendly).ToList();
            shots += fresh.Count;
            crits += fresh.Count(b => b.Crit);
            lastCd = p.FireCd;
        }
        Assert.Equal(6, shots, "liczba strzałów w bębenku");
        Assert.Equal(1, crits, "tylko szósty nabój krytyczny");
        Assert.True(lastCd > 0.5f, $"brak przeładowania (cd {lastCd:0.00})");
        Assert.Equal(PlayerSim.DrumSize, p.Ammo, "pełny bębenek po przeładowaniu");
    }

    [Test]
    public static void BardSongBuffsAllPlayers()
    {
        var g = new RunSim(new[] { "bard", "rycerz" }, 1, 5) { God = true };
        var room = g.Floor.Rooms.Values.First(r => r.Kind == RoomKind.Combat);
        g.EnterRoom(room, null);
        float baseInterval = g.Players[1].FireInterval();
        g.Update(1f / 60, new[] { new SimInput { Ability = true }, new SimInput() });
        Assert.True(g.Players[0].SongT > 0 && g.Players[1].SongT > 0, "pieśń nie objęła obu graczy");
        Assert.True(g.Players[1].FireInterval() < baseInterval, "pieśń nie zwiększa szybkostrzelności");
    }
}
