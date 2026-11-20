using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Otchlan.Sim.Bosses;

/// <summary>
/// Matka Roju (Groty): przywołuje nietoperze, strzela śrutem, szarżuje, wypuszcza fale pocisków z lukami.
/// Stan wizualny dla widoku:
///   ChargeDir – kierunek szarży (null = brak); gdy <see cref="Tele"/> &gt; 0 widok rysuje czerwoną linię
///               od bossa na 1400 px (lustrzane w <see cref="Enemy.LaserEnd"/>),
///   Tele – pozostały czas zapowiedzi szarży (0.6 → 0; <see cref="Enemy.Charge"/> = 1 - Tele/0.6).
/// </summary>
[EnemyType("hive")]
public sealed class HiveMother : Boss
{
    public Vector2? ChargeDir;
    public float Tele;

    public override void Setup(RunSim g)
    {
        ChargeDir = null;
        Tele = 0;
    }

    public override void ResetState()
    {
        base.ResetState();
        ChargeDir = null;
        Tele = 0;
    }

    public override void Update(float dt, RunSim g)
    {
        base.Update(dt, g);
        if (ChargeDir is { } cd && Tele > 0)
        {
            LaserEnd = new Vector2(X + cd.X * 1400, Y + cd.Y * 1400);
            Charge = 1 - Tele / 0.6f;
        }
        else
        {
            LaserEnd = null;
            Charge = 0;
        }
    }

    protected override IEnumerator<float> Attack(string name, RunSim g) => name switch
    {
        "brood" => Brood(g),
        "shotgun" => Shotgun(g),
        "charge" => ChargeAttack(g),
        _ => Wave(g),
    };

    private IEnumerator<float> Brood(RunSim g)
    {
        Mv = null;
        yield return 0.4f;
        int mine = g.Enemies.Count(e => e.Minion);
        for (int i = 0; i < Math.Min(4, 8 - mine); i++)
        {
            float a = g.R.Range(0, C.Tau);
            g.SpawnEnemy("bat", X + MathF.Cos(a) * 60, Y + MathF.Sin(a) * 60, minion: true);
        }
        g.Fx.Sound("spawn");
        g.Fx.Shake(4);
        if (Phase >= 2)
            FireRing(g, 14, 150, r: 8);
        yield return 0.5f;
    }

    private IEnumerator<float> Shotgun(RunSim g)
    {
        Mv = ("chase", Spd * 0.6f, 0, 0);
        for (int k = 0; k < (Phase == 1 ? 3 : 4); k++)
        {
            float a = AngleTo(g);
            int n = 12 + Extra * 3;
            for (int i = 0; i < n; i++)
                Fire(g, a + g.R.Range(-0.6f, 0.6f), g.R.Range(180, 290), 7, C.EnemyBullet2);
            g.Fx.Sound("enemy_shoot");
            g.Fx.Shake(3);
            yield return 0.7f;
        }
        Mv = null;
    }

    private IEnumerator<float> ChargeAttack(RunSim g)
    {
        Mv = null;
        for (int k = 0; k < (Phase == 1 ? 2 : 3); k++)
        {
            float a = AngleTo(g);
            ChargeDir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            Tele = 0.6f;
            g.Fx.Sound("charge", 0.7f);
            float t = 0;
            while (t < 0.6f)
            {
                t += Dt;
                a = AngleTo(g);
                if (t < 0.4f)
                    ChargeDir = new Vector2(MathF.Cos(a), MathF.Sin(a));
                Tele = 0.6f - t;
                yield return 0;
            }
            Tele = 0;
            float sp = 620 * Bspd;
            t = 0;
            var dir = ChargeDir.Value;
            while (t < 1.6f)
            {
                t += Dt;
                Vx = dir.X * sp;
                Vy = dir.Y * sp;
                Mv = ("dir", Vx, Vy, 0);
                float nx = X + Vx * Dt * 2, ny = Y + Vy * Dt * 2;
                if (!(C.RX0 + R < nx && nx < C.RX1 - R && C.RY0 + R < ny && ny < C.RY1 - R))
                    break;
                yield return 0;
            }
            Mv = null;
            g.Fx.Shake(8);
            g.Fx.Sound("slam", 0.6f);
            FireRing(g, 10 + Extra * 4, 170, g.R.Range(0, 1), r: 8);
            yield return 0.4f;
        }
        ChargeDir = null;
    }

    private IEnumerator<float> Wave(RunSim g)
    {
        Mv = null;
        for (int k = 0; k < 3; k++)
        {
            const int n = 32;
            int gapCenter = g.R.RangeInt(0, n - 1);
            int gap = Phase == 1 ? 5 : 4;
            for (int i = 0; i < n; i++)
            {
                int d1 = ((i - gapCenter) % n + n) % n, d2 = ((gapCenter - i) % n + n) % n;
                if (Math.Min(d1, d2) <= gap / 2)
                    continue;
                Fire(g, i * C.Tau / n, 150, 8, C.EnemyBullet3);
            }
            g.Fx.Sound("enemy_shoot");
            yield return 0.9f;
        }
    }

    public override void Ai(float dt, RunSim g)
    {
        base.Ai(dt, g);
        if (Intro <= 0 && Mv == null && ChargeDir == null)
        {
            Vx = MathF.Cos(T * 0.7f) * 50;
            Vy = MathF.Sin(T * 0.9f) * 40;
        }
    }
}
