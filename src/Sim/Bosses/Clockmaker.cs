using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Otchlan.Sim.Bosses;

/// <summary>
/// Zegarmistrz (Mechanizm, piętro 35): lewitujący mechaniczny zegar. Obraca dwie (w 2. fazie trzy)
/// strumieniowe wskazówki, wypuszcza odbijające się zębatki, zatrzymuje czas (pociski wiszą w miejscu,
/// po chwili ruszają dalej) i przywołuje wieżyczki. Między atakami unosi się powoli ósemką.
/// Stan wizualny: <see cref="Enemy.Charge"/> (świecenie mechanizmu przed atakiem),
/// <see cref="Enemy.LaserEnd"/> (zapowiedź kierunku wskazówki), <see cref="Enemy.Flash"/> (błysk przy STOP).
/// </summary>
[EnemyType("clockmaker")]
public sealed class Clockmaker : Boss
{
    public static readonly Color BrassColor = new("e6be50");
    public static readonly Color CopperColor = new("d2823c");
    public static readonly Color HandColor = new("7dffc8");

    private const int MaxTurrets = 4;

    public override void Setup(RunSim g) { }

    public override void Ai(float dt, RunSim g)
    {
        base.Ai(dt, g);
        if (Intro <= 0 && Mv == null)
        {
            // powolne unoszenie się ósemką
            Vx = MathF.Cos(T * 0.6f) * 45;
            Vy = MathF.Sin(T * 1.2f) * 32;
        }
    }

    protected override IEnumerator<float> Attack(string name, RunSim g) => name switch
    {
        "gears" => Gears(g),
        "stop" => Stop(g),
        "turrets" => Turrets(g),
        _ => Hands(g),
    };

    /// <summary>Leci do środka pokoju (max <paramref name="max"/> s).</summary>
    private IEnumerable<float> GoCenter(float max)
    {
        Mv = ("to", C.RCX, C.RCY, Spd * 1.6f);
        float t = 0;
        while (Mv != null && t < max)
        {
            t += Dt;
            yield return 0;
        }
        Mv = null;
        Vx = Vy = 0;
    }

    // ---------------------------------------------------------------- wskazówki
    /// <summary>Dwie obracające się w przeciwne strony wskazówki ze strumieni pocisków; w 2. fazie trzecia (sekundnik).</summary>
    private IEnumerator<float> Hands(RunSim g)
    {
        foreach (var w in GoCenter(1.2f)) yield return w;
        float aH = g.R.Range(0, C.Tau), aM = aH + C.Tau / 3, aS = aH + C.Tau * 2 / 3;
        // zapowiedź: czerwone linie w kierunku wskazówek
        float t = 0;
        while (t < 0.65f)
        {
            t += Dt;
            Charge = t / 0.65f;
            float len = 420;
            LaserEnd = new Vector2(X + MathF.Cos(aH) * len, Y + MathF.Sin(aH) * len);
            yield return 0;
        }
        LaserEnd = null;
        Charge = 0;
        g.Fx.Sound("charge", 0.6f);
        float dur = 3.5f, tick = 0, spd = 250;
        float wH = 0.6f, wM = -1.5f, wS = 0.9f;
        t = 0;
        while (t < dur)
        {
            t += Dt;
            tick -= Dt;
            aH += wH * Dt;
            aM += wM * Dt;
            aS += wS * Dt;
            if (tick <= 0)
            {
                tick += 0.08f;
                Fire(g, aH, spd, 8, BrassColor, 0.9f);
                Fire(g, aM, spd, 7, HandColor, 0.9f);
                if (Phase >= 2) Fire(g, aS, spd, 7, CopperColor, 0.9f);
                g.Fx.Sound("enemy_shoot", 0.25f);
            }
            Charge = 0.4f;
            yield return 0;
        }
        Charge = 0;
        yield return 0.3f;
    }

    // ---------------------------------------------------------------- zębatki
    /// <summary>Duże, odbijające się zębatki oraz drobne pierścienie od bossa.</summary>
    private IEnumerator<float> Gears(RunSim g)
    {
        Mv = ("chase", Spd * 0.5f, 0, 0);
        float t = 0;
        while (t < 0.5f)
        {
            t += Dt;
            Charge = t / 0.5f;
            yield return 0;
        }
        Charge = 0;
        int n = (Phase == 1 ? 3 : 5) + Extra / 2;
        float off = g.R.Range(0, C.Tau);
        for (int i = 0; i < n; i++)
        {
            var b = Fire(g, off + i * C.Tau / n, 200, 16, BrassColor, 1.2f, life: 7, style: "skull");
            if (b != null) b.Bounce = 3;
        }
        g.Fx.Sound("slam", 0.5f);
        g.Fx.Shake(4);
        g.Fx.Ring(X, Y, BrassColor, 90, 0.35f, 5);
        int rings = Phase == 1 ? 5 : 7;
        for (int k = 0; k < rings; k++)
        {
            yield return 0.8f;
            FireRing(g, 8, 170, g.R.Range(0, C.Tau), r: 7, color: CopperColor);
            g.Fx.Sound("enemy_shoot", 0.4f);
        }
        Mv = null;
        yield return 0.4f;
    }

    // ---------------------------------------------------------------- STOP
    /// <summary>Pierścienie z lukami; po „STOP” pociski zastygają na ~1 s, potem ruszają dalej (w 2. fazie część na gracza).</summary>
    private IEnumerator<float> Stop(RunSim g)
    {
        foreach (var w in GoCenter(0.8f)) yield return w;
        var frozen = new List<(Bullet b, float vx, float vy)>();
        int rings = Phase == 1 ? 2 : 3;
        int n = 30 + Extra * 2;
        float off = g.R.Range(0, C.Tau);
        for (int k = 0; k < rings; k++)
        {
            float gap = g.R.Range(0, C.Tau);
            Charge = 0.6f;
            for (int i = 0; i < n; i++)
            {
                float a = off + i * C.Tau / n;
                if (MathF.Abs(Mathf.Wrap(a - gap, -Mathf.Pi, Mathf.Pi)) < 0.28f) continue; // luka
                var b = Fire(g, a, 190, 8, k % 2 == 0 ? BrassColor : HandColor, life: 8);
                if (b != null) frozen.Add((b, b.Vx, b.Vy));
            }
            g.Fx.Sound("enemy_shoot", 0.5f);
            off += C.Tau / n / 2;
            yield return 0.3f;
        }
        yield return 0.3f;
        Charge = 0;
        // STOP
        g.Fx.Sound("thunk", 0.8f);
        g.Fx.Shake(7);
        g.Fx.Ring(X, Y, HandColor, 320, 0.5f, 6);
        Flash = 0.15f;
        foreach (var (b, _, _) in frozen)
            if (b.Alive) { b.Vx = b.Vy = 0; b.Life += 2f; }
        yield return 1.0f;
        // wznowienie
        g.Fx.Sound("dash", 0.6f);
        int idx = 0;
        foreach (var (b, vx, vy) in frozen)
        {
            if (!b.Alive) continue;
            float sp = MathF.Sqrt(vx * vx + vy * vy);
            if (Phase >= 2 && idx++ % 2 == 0)
            {
                float a = MathF.Atan2(TargetPos(g).Y - b.Y, TargetPos(g).X - b.X);
                b.Vx = MathF.Cos(a) * sp;
                b.Vy = MathF.Sin(a) * sp;
            }
            else
            {
                b.Vx = vx;
                b.Vy = vy;
            }
        }
        yield return 0.5f;
    }

    // ---------------------------------------------------------------- wieżyczki
    private IEnumerator<float> Turrets(RunSim g)
    {
        int n = Phase == 1 ? 2 : 3;
        Charge = 0.5f;
        for (int i = 0; i < n; i++)
        {
            var p = g.RandomFreePos(minDist: 200, margin: 80);
            g.Effects.Add(new Telegraph(p.X, p.Y, 34, 1.0f + i * 0.15f, g2 =>
            {
                if (g2.Enemies.Count(e => e.Minion && e.Alive && e.Key == "turret") >= MaxTurrets) return;
                g2.SpawnEnemy("turret", p.X, p.Y, minion: true);
                g2.Fx.Sound("spawn", 0.6f);
                g2.Fx.Burst(p.X, p.Y, BrassColor, 12, 180, 5, 0.5f);
            }, BrassColor));
        }
        g.Fx.Sound("charge", 0.5f);
        yield return 0.8f;
        Charge = 0;
        FireRing(g, 10, 150, g.R.Range(0, 1), r: 7, color: CopperColor);
        yield return 0.8f;
    }

    // ---------------------------------------------------------------- zmiana fazy
    /// <summary>„Wybicie godziny”: 12 zapowiedzi jak cyfry tarczy, każda wypuszcza po 3 pociski (do środka lub na zewnątrz).</summary>
    protected override IEnumerator<float> PhaseChange(RunSim g)
    {
        var it = base.PhaseChange(g);
        while (it.MoveNext()) yield return it.Current;
        for (int h = 0; h < 12; h++)
        {
            float a = h * C.Tau / 12 - Mathf.Pi / 2;
            float x = C.RCX + MathF.Cos(a) * 470, y = C.RCY + MathF.Sin(a) * 240;
            bool inward = h % 2 == 0;
            float dir = inward ? MathF.Atan2(C.RCY - y, C.RCX - x) : a;
            g.Effects.Add(new Telegraph(x, y, 28, 0.9f + h * 0.06f, g2 =>
            {
                for (int k = -1; k <= 1; k++)
                    Fire(g2, dir + k * 0.18f, 190, 8, BrassColor, x: x, y: y);
                g2.Fx.Sound("thunk", 0.3f);
                g2.Fx.Ring(x, y, BrassColor, 50, 0.3f, 4);
            }, BrassColor));
        }
        g.Fx.Sound("charge", 0.7f);
        yield return 1.8f;
    }
}
