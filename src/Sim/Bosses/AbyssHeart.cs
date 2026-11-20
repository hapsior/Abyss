using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Otchlan.Sim.Bosses;

/// <summary>
/// Serce Otchłani (finałowy boss, 3 fazy): kwiaty pocisków, celowane salwy, przywołania, strumienie, deszcz, piekło.
/// Stan wizualny dla widoku:
///   Beat – faza bicia serca (rośnie w czasie; puls = max(0, sin(Beat*2π))^6, promień ×(1 + puls*0.08)),
///   Home – punkt, wokół którego krąży serce.
/// </summary>
[EnemyType("abyss")]
public sealed class AbyssHeart : Boss
{
    public float Beat;
    public Vector2 Home;

    public override void Setup(RunSim g)
    {
        Beat = 0;
        Home = new Vector2(C.RCX, C.RCY - 60);
    }

    public override string[] AvailableAttacks() => Phase switch
    {
        1 => new[] { "flower", "aimed", "spawn" },
        2 => new[] { "flower", "aimed", "streams", "rain", "spawn" },
        _ => new[] { "streams", "rain", "hell", "aimed", "flower" },
    };

    protected override IEnumerator<float> Attack(string name, RunSim g) => name switch
    {
        "flower" => Flower(g),
        "aimed" => Aimed(g),
        "spawn" => SpawnMinions(g),
        "streams" => Streams(g),
        "rain" => Rain(g),
        _ => Hell(g),
    };

    private IEnumerator<float> Flower(RunSim g)
    {
        int rings = Phase < 3 ? 5 : 7;
        for (int k = 0; k < rings; k++)
        {
            FireRing(g, 18, 150 + Phase * 10, k * 0.09f, r: 8, color: C.EnemyBullet3);
            g.Fx.Sound("enemy_shoot", 0.5f);
            yield return 0.28f;
        }
    }

    private IEnumerator<float> Aimed(RunSim g)
    {
        int shots = 8 + Phase * 2;
        for (int k = 0; k < shots; k++)
        {
            float a = AngleTo(g) + g.R.Range(-0.1f, 0.1f);
            Fire(g, a, 270, 7);
            if (Phase >= 2)
            {
                Fire(g, a + 0.3f, 230, 7);
                Fire(g, a - 0.3f, 230, 7);
            }
            g.Fx.Sound("enemy_shoot", 0.5f);
            yield return 0.12f;
        }
        yield return 0.3f;
    }

    private IEnumerator<float> SpawnMinions(RunSim g)
    {
        int mine = g.Enemies.Count(e => e.Minion);
        for (int i = 0; i < Math.Min(2, 4 - mine); i++)
        {
            float a = g.R.Range(0, C.Tau);
            string key = g.R.RangeInt(0, 1) == 0 ? "imp" : "ghost";
            g.SpawnEnemy(key, X + MathF.Cos(a) * 90, Y + MathF.Sin(a) * 90, minion: true);
        }
        g.Fx.Sound("spawn");
        FireRing(g, 24, 120, r: 8, color: C.EnemyBullet);
        yield return 0.7f;
    }

    private IEnumerator<float> Streams(RunSim g)
    {
        int streams = Phase == 2 ? 4 : 5;
        float ang = g.R.Range(0, C.Tau);
        int d = g.R.RangeInt(0, 1) == 0 ? -1 : 1;
        float t = 0;
        while (t < 4.5f)
        {
            for (int i = 0; i < streams; i++)
                Fire(g, ang + i * C.Tau / streams, 120, 7, C.EnemyBullet2, accel: 140, maxSpeed: 320);
            ang += d * 0.12f;
            yield return 0.1f;
            t += 0.1f;
        }
    }

    private IEnumerator<float> Rain(RunSim g)
    {
        var tp = TargetPos(g);
        int count = Phase == 2 ? 6 : 9;
        for (int i = 0; i < count; i++)
        {
            float x = Math.Min(C.RX1 - 40, Math.Max(C.RX0 + 40, tp.X + g.R.Range(-300, 300)));
            float y = Math.Min(C.RY1 - 40, Math.Max(C.RY0 + 40, tp.Y + g.R.Range(-200, 200)));
            g.Effects.Add(new Telegraph(x, y, 40, 0.9f + i * 0.15f, g2 =>
            {
                for (int k = 0; k < 8; k++)
                    Fire(g2, k * C.Tau / 8 + 0.2f, 170, 7, C.EnemyBullet3, x: x, y: y);
                g2.Fx.Burst(x, y, new Color("c85aff"), 12, 200, 5, 0.4f);
                g2.Fx.Sound("enemy_shoot", 0.6f);
            }, new Color("c850ff")));
        }
        yield return 2.0f;
    }

    private IEnumerator<float> Hell(RunSim g)
    {
        float ang = 0, t = 0;
        while (t < 5.0f)
        {
            for (int i = 0; i < 3; i++)
            {
                Fire(g, ang + i * C.Tau / 3, 160, 7, C.EnemyBullet3);
                Fire(g, -ang * 1.3f + i * C.Tau / 3, 140, 7, C.EnemyBullet);
            }
            ang += 0.17f;
            if ((int)(t / 0.1f) % 12 == 0)
                Fire(g, AngleTo(g), 290, 8, C.EnemyBullet2);
            yield return 0.1f;
            t += 0.1f;
        }
    }

    protected override IEnumerator<float> PhaseChange(RunSim g)
    {
        var gen = base.PhaseChange(g);
        while (gen.MoveNext())
            yield return gen.Current;
        for (int i = 0; i < 2; i++)
        {
            float a = g.R.Range(0, C.Tau);
            g.SpawnEnemy("ghost", X + MathF.Cos(a) * 100, Y + MathF.Sin(a) * 100, minion: true);
        }
        FireRing(g, 36, 160, r: 8, color: C.EnemyBullet2);
        yield return 0.6f;
    }

    public override void Ai(float dt, RunSim g)
    {
        Beat += dt * (1.6f + Phase * 0.5f);
        base.Ai(dt, g);
        if (Intro <= 0 && Mv == null)
        {
            float hx = Home.X + MathF.Sin(T * 0.5f) * 260;
            float hy = Home.Y + MathF.Sin(T * 0.8f) * 70;
            Vx = (hx - X) * 1.2f;
            Vy = (hy - Y) * 1.2f;
        }
    }
}
