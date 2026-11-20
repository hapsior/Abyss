using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Otchlan.Sim.Bosses;

/// <summary>
/// Władca Kuźni (Kuźnia): uderzenie młotem z pierścieniami, strumienie płomieni, zrywy; w fazie 2 dodatkowo "meteors".
/// Stan wizualny dla widoku:
///   <see cref="Enemy.Lift"/> – zamach młota 0..1 (rysuj czerwony okrąg o promieniu 60 + 100*Lift i unieś postać o Lift*20 px),
///   TeleDir – kierunek zrywu (null = brak); gdy Tele &gt; 0 rysuj linię na 400 px (lustrzane w <see cref="Enemy.LaserEnd"/>),
///   Tele – pozostały czas zapowiedzi zrywu (0.45 → 0; <see cref="Enemy.Charge"/> = 1 - Tele/0.45).
/// </summary>
[EnemyType("forge")]
public sealed class ForgeLord : Boss
{
    public Vector2? TeleDir;
    public float Tele;

    public override void Setup(RunSim g)
    {
        Lift = 0;
        TeleDir = null;
        Tele = 0;
    }

    public override void ResetState()
    {
        base.ResetState();
        Lift = 0;
        TeleDir = null;
        Tele = 0;
    }

    public override string[] AvailableAttacks() =>
        Phase >= 2 ? new[] { "slam", "flame", "dash", "meteors" } : BDef.Attacks;

    public override void Update(float dt, RunSim g)
    {
        base.Update(dt, g);
        if (TeleDir is { } td && Tele > 0)
        {
            LaserEnd = new Vector2(X + td.X * 400, Y + td.Y * 400);
            Charge = 1 - Tele / 0.45f;
        }
        else
        {
            LaserEnd = null;
            Charge = 0;
        }
    }

    protected override IEnumerator<float> Attack(string name, RunSim g) => name switch
    {
        "slam" => Slam(g),
        "flame" => Flame(g),
        "dash" => Dash(g),
        _ => Meteors(g),
    };

    private IEnumerator<float> Slam(RunSim g)
    {
        Mv = null;
        float t = 0;
        g.Fx.Sound("charge", 0.6f);
        while (t < 0.8f)
        {
            t += Dt;
            Lift = t / 0.8f;
            yield return 0;
        }
        Lift = 0;
        g.Fx.Sound("slam");
        g.Fx.Shake(12);
        g.Fx.Ring(X, Y, new Color("ff963c"), 160, 0.4f, 8);
        int rings = Phase == 1 ? 3 : 4;
        for (int k = 0; k < rings; k++)
        {
            FireRing(g, 22, 200, (k % 2) * C.Tau / 44, r: 8, color: C.EnemyBullet2);
            yield return 0.25f;
        }
        if (Phase >= 2)
            for (int i = 0; i < 3; i++)
            {
                var p = g.RandomFreePos(0, fly: true, margin: 60);
                g.Effects.Add(new FirePool(p.X, p.Y, 50, 12 * DmgScale, 4.5f));
            }
        yield return 0.4f;
    }

    private IEnumerator<float> Flame(RunSim g)
    {
        Mv = ("chase", Spd * 0.4f, 0, 0);
        int streams = Phase == 1 ? 3 : 4;
        float ang = g.R.Range(0, C.Tau);
        int d = g.R.RangeInt(0, 1) == 0 ? -1 : 1;
        float t = 0;
        while (t < 4.0f)
        {
            for (int i = 0; i < streams; i++)
                Fire(g, ang + i * C.Tau / streams, 230, 7, new Color("ff8c32"));
            ang += d * 0.11f;
            yield return 0.075f;
            t += 0.075f;
        }
        Mv = null;
    }

    private IEnumerator<float> Dash(RunSim g)
    {
        for (int k = 0; k < 3; k++)
        {
            float a = AngleTo(g);
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            TeleDir = dir;
            float t = 0;
            while (t < 0.45f)
            {
                t += Dt;
                Tele = 0.45f - t;
                yield return 0;
            }
            Tele = 0;
            float sp = 680 * Bspd;
            t = 0;
            while (t < 0.5f)
            {
                t += Dt;
                Mv = ("dir", dir.X * sp, dir.Y * sp, 0);
                yield return 0;
            }
            Mv = null;
            FireRing(g, 8 + Extra * 4, 190, g.R.Range(0, 1), r: 8, color: C.EnemyBullet2);
            g.Fx.Shake(5);
            g.Fx.Sound("thunk", 0.8f);
            yield return 0.25f;
        }
        TeleDir = null;
    }

    private IEnumerator<float> Meteors(RunSim g)
    {
        Mv = null;
        var tp = TargetPos(g);
        for (int i = 0; i < 7; i++)
        {
            float x = Math.Min(C.RX1 - 50, Math.Max(C.RX0 + 50, tp.X + g.R.Range(-220, 220)));
            float y = Math.Min(C.RY1 - 50, Math.Max(C.RY0 + 50, tp.Y + g.R.Range(-160, 160)));
            if (i == 0)
            {
                x = tp.X;
                y = tp.Y;
            }
            float mx = x, my = y, dmg = DmgScale;
            g.Effects.Add(new Telegraph(mx, my, 70, 1.1f + i * 0.12f, g2 =>
            {
                g2.Explode(mx, my, 70, 18 * dmg, hurtPlayer: true, hurtEnemies: false);
                g2.Effects.Add(new FirePool(mx, my, 42, 10 * dmg, 3.0f));
            }, new Color("ff7828")));
        }
        yield return 1.8f;
    }
}
