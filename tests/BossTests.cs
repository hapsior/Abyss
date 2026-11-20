using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Otchlan.Sim;

namespace Otchlan.Tests;

/// <summary>Testy bossów: każdy boss używa wszystkich swoich ataków, strzela, a po ostrzale umiera (z odpowiednimi zdarzeniami).</summary>
public static class BossTests
{
    private static readonly (string key, int depth)[] Bosses =
    {
        ("slime_king", 5), ("lich", 10), ("hive", 15), ("forge", 20), ("abyss", 40),
    };

    /// <summary>Ataki dostępne dopiero w późniejszych fazach (poza listą z bosses.json).</summary>
    private static readonly Dictionary<string, string[]> PhaseExtras = new()
    {
        ["lich"] = new[] { "cross" },
        ["forge"] = new[] { "meteors" },
        ["abyss"] = new[] { "streams", "rain", "hell" },
    };

    private static RunSim Setup(string key, int depth, ulong seed)
    {
        var g = new RunSim(new[] { "rycerz" }, 1, seed) { God = true };
        g.NewFloor(depth);
        var room = g.Floor.Rooms.Values.First(r => r.Kind == RoomKind.Boss);
        g.EnterRoom(room, null);
        Assert.True(g.Boss != null, $"{key}: brak bossa w pokoju");
        Assert.Equal(key, g.Boss.Key, "klucz bossa");
        return g;
    }

    private static SimInput Strafe(RunSim g, float t, bool fire)
    {
        var p = g.Players[0];
        var center = new Vector2(C.RCX, C.RCY);
        float a = t * 0.7f;
        var want = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 170;
        var mv = want - new Vector2(p.X, p.Y);
        var inp = new SimInput { Move = mv.LengthSquared() > 1 ? mv.Normalized() : Vector2.Zero };
        if (fire && g.Boss != null)
        {
            inp.Aim = MathF.Atan2(g.Boss.Y - p.Y, g.Boss.X - p.X);
            inp.Target = new Vector2(g.Boss.X, g.Boss.Y);
            inp.Fire = true;
        }
        return inp;
    }

    [Test]
    public static void BossesUseAllAttacksAndFire()
    {
        foreach (var (key, depth) in Bosses)
        {
            var g = Setup(key, depth, 4242);
            var boss = g.Boss;
            var seen = new HashSet<string>();
            var phases = new HashSet<int>();
            bool fired = false;
            const float dt = 1f / 60;
            float t = 0;
            while (t < 120)
            {
                // wymuszanie kolejnych faz (bez obrażeń od gracza): co 30 s niższe HP
                float frac = key == "abyss" ? (t < 30 ? 1f : t < 60 ? 0.6f : 0.3f) : (t < 30 ? 1f : 0.45f);
                if (boss.Frac > frac) boss.Hp = boss.MaxHp * frac;
                g.Update(dt, new[] { Strafe(g, t, false) });
                t += dt;
                if (boss.LastAttack != null) seen.Add(boss.LastAttack);
                phases.Add(boss.Phase);
                if (g.Bullets.Count > 0) fired = true;
                Assert.True(boss.Alive && g.Boss == boss, $"{key}: boss zniknął bez ostrzału");
                g.Events.Clear();
            }
            var expected = boss.BDef.Attacks.Concat(PhaseExtras.GetValueOrDefault(key, Array.Empty<string>())).ToList();
            var missing = expected.Where(a => !seen.Contains(a)).ToList();
            GD.Print($"    {key}: ataki {string.Join(",", seen)}, fazy {string.Join(",", phases)}");
            Assert.True(missing.Count == 0, $"{key}: nieużyte ataki: {string.Join(",", missing)}");
            Assert.True(fired, $"{key}: nie wystrzelił pocisków");
            Assert.True(phases.Count >= boss.BDef.Phases.Length + 1, $"{key}: nie przeszedł wszystkich faz");
        }
    }

    [Test]
    public static void BossesCanBeKilled()
    {
        foreach (var (key, depth) in Bosses)
        {
            var g = Setup(key, depth, 777);
            g.Players[0].Damage = 500;
            var boss = g.Boss;
            bool defeated = false, story = false;
            const float dt = 1f / 60;
            float t = 0;
            while (t < 120 && !defeated)
            {
                g.Update(dt, new[] { Strafe(g, t, true) });
                t += dt;
                while (g.Events.Count > 0)
                {
                    var ev = g.Events.Dequeue();
                    if (ev.Kind == RunEventKind.BossDefeated) defeated = true;
                    if (ev.Kind == RunEventKind.StoryComplete) story = true;
                }
            }
            GD.Print($"    {key}: pokonany={defeated} po {t:0.0} s");
            Assert.True(defeated, $"{key}: boss nie zginął w 120 s (hp {boss.Hp:0}/{boss.MaxHp:0})");
            Assert.True(!boss.Alive, $"{key}: boss wciąż żyje");
            Assert.Equal(key == "abyss", story, $"{key}: StoryComplete");
        }
    }
}
