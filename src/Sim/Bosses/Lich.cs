using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Otchlan.Sim.Bosses;

/// <summary>
/// Licz (Katakumby): spirale pocisków, teleportacja z salwami, przywoływanie duchów; w fazie 2 dodatkowo "cross".
/// Stan wizualny dla widoku:
///   Fade (0..1) – przezroczystość podczas teleportu (lustrzane w <see cref="Enemy.Alpha"/>),
///   Cast (0..1) – blask laski podczas rzucania zaklęcia (lustrzane w <see cref="Enemy.Charge"/>).
/// </summary>
[EnemyType("lich")]
public sealed class Lich : Boss
{
    public float Fade = 1, Cast;

    public override void Setup(RunSim g)
    {
        Fade = 1;
        Cast = 0;
    }

    public override string[] AvailableAttacks() =>
        Phase >= 2 ? new[] { "spiral", "teleport", "summon", "cross" } : BDef.Attacks;

    public override void ResetState()
    {
        base.ResetState();
        Fade = 1;
        Cast = 0;
    }

    public override void Update(float dt, RunSim g)
    {
        base.Update(dt, g);
        Alpha = Fade;
        Charge = Cast;
    }

    protected override IEnumerator<float> Attack(string name, RunSim g) => name switch
    {
        "spiral" => Spiral(g),
        "cross" => Cross(g),
        "teleport" => Teleport(g),
        _ => Summon(g),
    };

    private IEnumerator<float> Spiral(RunSim g)
    {
        int arms = Phase == 1 ? 3 : 5;
        float ang = g.R.Range(0, C.Tau);
        int d = g.R.RangeInt(0, 1) == 0 ? -1 : 1;
        float t = 0;
        Cast = 1;
        while (t < 3.2f)
        {
            for (int i = 0; i < arms; i++)
                Fire(g, ang + i * C.Tau / arms, 165, 7, C.EnemyBullet3);
            ang += 0.21f * d;
            g.Fx.Sound("enemy_shoot", 0.25f, 0.08f);
            yield return 0.1f;
            t += 0.1f;
        }
        Cast = 0;
    }

    private IEnumerator<float> Cross(RunSim g)
    {
        float ang = 0, t = 0;
        while (t < 3.6f)
        {
            for (int i = 0; i < 4; i++)
            {
                Fire(g, ang + i * C.Tau / 4, 180, 7, C.EnemyBullet3);
                Fire(g, -ang + i * C.Tau / 4 + C.Tau / 8, 140, 7, C.EnemyBullet);
            }
            ang += 0.16f;
            yield return 0.13f;
            t += 0.13f;
        }
    }

    private IEnumerator<float> Teleport(RunSim g)
    {
        int n = Phase == 1 ? 2 : 3;
        for (int k = 0; k < n; k++)
        {
            float t = 0;
            while (t < 0.35f)
            {
                t += Dt;
                Fade = 1 - t / 0.35f;
                Invuln = Fade < 0.3f;
                yield return 0;
            }
            g.Fx.Burst(X, Y, new Color("b478ff"), 20, 250, 5, 0.5f);
            var p = g.RandomFreePos(260, fly: true, margin: 80);
            X = p.X;
            Y = p.Y;
            t = 0;
            while (t < 0.35f)
            {
                t += Dt;
                Fade = t / 0.35f;
                Invuln = Fade < 0.3f;
                yield return 0;
            }
            Fade = 1;
            Invuln = false;
            for (int b = 0; b < 3; b++)
            {
                FireSpread(g, AngleTo(g), 5 + Extra * 2, 0.18f, 230, r: 7);
                g.Fx.Sound("enemy_shoot", 0.6f);
                yield return 0.3f;
            }
        }
    }

    private IEnumerator<float> Summon(RunSim g)
    {
        Cast = 1;
        yield return 0.5f;
        int mine = g.Enemies.Count(e => e.Minion);
        for (int i = 0; i < Math.Min(3, 5 - mine); i++)
        {
            float a = g.R.Range(0, C.Tau);
            g.SpawnEnemy("ghost", X + MathF.Cos(a) * 70, Y + MathF.Sin(a) * 70, minion: true);
        }
        g.Fx.Sound("spawn");
        FireRing(g, 16, 120, r: 8, color: C.EnemyBullet3);
        g.Fx.Ring(X, Y, new Color("a078ff"), 120, 0.4f);
        Cast = 0;
        yield return 0.6f;
    }

    public override void Ai(float dt, RunSim g)
    {
        base.Ai(dt, g);
        if (Intro <= 0 && Mv == null && Fade >= 1)
        {
            float a = T * 0.6f;
            Vx = MathF.Cos(a) * 60;
            Vy = MathF.Sin(a * 1.3f) * 40;
        }
    }
}
