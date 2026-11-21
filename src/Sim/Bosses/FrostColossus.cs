using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Otchlan.Sim.Bosses;

/// <summary>
/// Szronowy Kolos (Lodowa Krypta, piętro 30): ciężki, powolny lodowy golem. Motyw: spowolnienie –
/// większość pocisków ma <see cref="Bullet.Chill"/>, a lód na ziemi to strefy bez obrażeń, które tylko spowalniają.
/// Stan wizualny: <see cref="Enemy.SquashY"/>/<see cref="Enemy.SquashX"/> (przysiad przed uderzeniem, oddech),
/// <see cref="Enemy.Charge"/> (świecenie przed atakiem), <see cref="Enemy.LaserEnd"/> (linia zapowiedzi
/// oddechu i ślizgu), <see cref="Enemy.Tilt"/> (pochylenie podczas ślizgu).
/// </summary>
[EnemyType("frost_colossus")]
public sealed class FrostColossus : Boss
{
    public static readonly Color IceColor = new("9fd8ff");
    public static readonly Color FrostColor = new("d8f4ff");
    public static readonly Color DarkIce = new("5aa8e6");

    private const int MaxZones = 12;
    private float _squat;

    public override void Setup(RunSim g)
    {
        _squat = 0;
        Mv = ("chase", Spd * 0.7f, 0, 0);
    }

    public override void ResetState()
    {
        base.ResetState();
        _squat = 0;
        SquashX = SquashY = 1;
        Tilt = 0;
    }

    public override void Update(float dt, RunSim g)
    {
        base.Update(dt, g);
        float breathe = 0.02f * MathF.Sin(T * 1.6f);
        SquashX = 1 + breathe + 0.12f * _squat;
        SquashY = 1 - breathe - 0.16f * _squat;
    }

    protected override IEnumerator<float> Attack(string name, RunSim g) => name switch
    {
        "slam" => Slam(g),
        "icicles" => Icicles(g),
        "breath" => Breath(g),
        "slide" => Slide(g),
        _ => Slam(g),
    };

    // ---------------------------------------------------------------- pomocnicze
    private static int ZoneCount(RunSim g) => g.Effects.Count(e => e is HazardZone && e.Alive);

    private static Vector2 Clamp(float x, float y, float m) =>
        new(Math.Clamp(x, C.RX0 + m, C.RX1 - m), Math.Clamp(y, C.RY0 + m, C.RY1 - m));

    private void FrostZone(RunSim g, float x, float y, float r, float time, float slow = 0.6f)
    {
        if (ZoneCount(g) >= MaxZones) return;
        g.Effects.Add(new HazardZone(x, y, r, 0, time, FrostColor, warm: 0, slow: slow));
    }

    private Bullet Ice(RunSim g, float a, float speed, float r = 8, float chill = 1f, float accel = 0, string style = "orb",
        float? x = null, float? y = null, Color? color = null)
    {
        var b = Fire(g, a, speed, r, color ?? IceColor, accel: accel, style: style, x: x, y: y);
        if (b != null) b.Chill = chill;
        return b;
    }

    private void IceRing(RunSim g, int n, float speed, float off, float accel = 0, float r = 9)
    {
        for (int i = 0; i < n; i++)
            Ice(g, off + i * C.Tau / n, speed, r, accel: accel);
    }

    private IEnumerator<float> Wind(RunSim g, float time)
    {
        float t = 0;
        while (t < time)
        {
            t += Dt;
            Charge = t / time;
            _squat = Math.Min(1, t / time);
            yield return 0;
        }
        Charge = 0;
    }

    // ---------------------------------------------------------------- uderzenie
    /// <summary>Zamach, potem pierścień pocisków z chłodem i strefa szronu pod stopami; w fazach 2–3 kolejne uderzenia.</summary>
    private IEnumerator<float> Slam(RunSim g)
    {
        Mv = null;
        int hits = Phase == 2 ? 2 : 1;
        for (int k = 0; k < hits; k++)
        {
            var wind = Wind(g, 0.6f);
            while (wind.MoveNext()) yield return wind.Current;
            _squat = -0.6f;
            g.Fx.Shake(9);
            g.Fx.Sound("slam", 0.8f);
            g.Fx.Ring(X, Y + R * 0.5f, FrostColor, R + 120, 0.4f, 7);
            g.Fx.Burst(X, Y + R * 0.6f, FrostColor, 22, 260, 6, 0.6f);
            int n = 14 + Extra * 3;
            float off = g.R.Range(0, C.Tau);
            IceRing(g, n, 190, off);
            if (Phase >= 3)
                IceRing(g, n, 120, off + C.Tau / n / 2, accel: 70);
            var z = Clamp(X, Y, 60);
            FrostZone(g, z.X, z.Y, R + 60, 3f, 0.6f);
            yield return 0.15f;
            _squat = 0;
            yield return Phase == 2 && k == 0 ? 0.55f : 0.5f;
        }
        Mv = ("chase", Spd * 0.7f, 0, 0);
    }

    // ---------------------------------------------------------------- sople
    /// <summary>Sople z sufitu: zapowiedź pod graczami i w losowych miejscach, potem rozprysk odłamków.</summary>
    private IEnumerator<float> Icicles(RunSim g)
    {
        Mv = ("chase", Spd * 0.5f, 0, 0);
        int n = Phase == 1 ? 5 : Phase == 2 ? 7 : 9;
        var spots = new List<Vector2>();
        foreach (var p in g.ActivePlayers())
            spots.Add(new Vector2(p.X, p.Y));
        int guard = 0;
        while (spots.Count < n && guard++ < 30)
        {
            var s = g.RandomFreePos(minDist: 100, margin: 70);
            if (spots.All(o => o.DistanceTo(s) > 90)) spots.Add(s);
        }
        g.Fx.Sound("charge", 0.5f);
        for (int i = 0; i < Math.Min(n, spots.Count); i++)
        {
            var c = Clamp(spots[i].X, spots[i].Y, 50);
            // pierwszy sopel pod graczem – celuje w pozycję z chwili rzucenia, gracz ma 1 s na ruch
            g.Effects.Add(new Telegraph(c.X, c.Y, 44, 1.0f, g2 =>
            {
                g2.Fx.Sound("break", 0.5f);
                g2.Fx.Shake(2);
                g2.Fx.Ring(c.X, c.Y, FrostColor, 70, 0.3f, 5);
                g2.Fx.Burst(c.X, c.Y, FrostColor, 12, 220, 5, 0.5f);
                float off = g2.R.Range(0, C.Tau);
                for (int k = 0; k < 6; k++)
                    Ice(g2, off + k * C.Tau / 6, 200, 6, style: "needle", x: c.X, y: c.Y);
                FrostZone(g2, c.X, c.Y, 46, 2.5f, 0.5f);
            }, IceColor));
            yield return 0.12f;
        }
        yield return 1.2f;
    }

    // ---------------------------------------------------------------- oddech
    /// <summary>Boss staje, linia zapowiedzi, potem stożek lodowych pocisków omiatający przed bossem.</summary>
    private IEnumerator<float> Breath(RunSim g)
    {
        Mv = null;
        Vx = Vy = 0;
        float a0 = AngleTo(g);
        g.Fx.Sound("charge", 0.7f);
        float t = 0;
        while (t < 0.5f)
        {
            t += Dt;
            Vx *= 0.8f; Vy *= 0.8f;
            Charge = t / 0.5f;
            LaserEnd = new Vector2(X + MathF.Cos(a0) * 1400, Y + MathF.Sin(a0) * 1400);
            yield return 0;
        }
        LaserEnd = null;
        Charge = 0;
        g.Fx.Sound("laser", 0.6f);
        float dur = 1.8f, amp = 0.5f;
        t = 0;
        float shot = 0;
        while (t < dur)
        {
            t += Dt;
            shot += Dt;
            float p = t / dur;
            float sweep = Phase >= 3
                ? MathF.Sin(p * C.Tau)            // tam i z powrotem
                : -1 + 2 * p;                    // jednokierunkowo
            float a = a0 + sweep * amp;
            Charge = 0.4f;
            _squat = 0.3f;
            while (shot >= 0.06f)
            {
                shot -= 0.06f;
                for (int i = 0; i < 2; i++)
                    Ice(g, a + g.R.Range(-0.12f, 0.12f), g.R.Range(220, 300), 7, chill: 1.2f, color: i == 0 ? FrostColor : IceColor);
            }
            yield return 0;
        }
        Charge = 0;
        _squat = 0;
        g.Fx.Burst(X, Y, FrostColor, 10, 160, 5, 0.4f);
        Mv = ("chase", Spd * 0.7f, 0, 0);
        yield return 0.5f;
    }

    // ---------------------------------------------------------------- ślizg
    /// <summary>Zapowiedź linią, potem szybki ślizg brzuchem do ściany zostawiający ślad lodu.</summary>
    private IEnumerator<float> Slide(RunSim g)
    {
        Mv = null;
        int runs = Phase == 1 ? 1 : Phase == 2 ? 2 : 3;
        for (int k = 0; k < runs; k++)
        {
            float a = AngleTo(g);
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            g.Fx.Sound("charge", 0.7f);
            float t = 0;
            while (t < 0.6f)
            {
                t += Dt;
                Vx *= 0.8f; Vy *= 0.8f;
                if (t < 0.4f)
                {
                    a = AngleTo(g);
                    dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
                }
                LaserEnd = new Vector2(X + dir.X * 1400, Y + dir.Y * 1400);
                Charge = t / 0.6f;
                _squat = t / 0.6f * 0.6f;
                yield return 0;
            }
            LaserEnd = null;
            Charge = 0;
            _squat = 0.4f;
            Tilt = dir.X >= 0 ? 0.25f : -0.25f;
            g.Fx.Sound("dash", 0.7f);
            float sp = 520 * Bspd;
            float dist = 0;
            t = 0;
            bool wall = false;
            while (t < 1.6f)
            {
                t += Dt;
                Mv = ("dir", dir.X * sp, dir.Y * sp, 0);
                dist += sp * Dt;
                if (dist >= 70)
                {
                    dist = 0;
                    if (ZoneCount(g) < MaxZones)
                        g.Effects.Add(new HazardZone(X, Y, 40, 0, 4f, FrostColor, warm: 0, slow: 0.5f));
                }
                float nx = X + dir.X * sp * Dt * 2, ny = Y + dir.Y * sp * Dt * 2;
                if (!(C.RX0 + R < nx && nx < C.RX1 - R && C.RY0 + R < ny && ny < C.RY1 - R))
                {
                    wall = true;
                    break;
                }
                yield return 0;
            }
            Mv = null;
            Vx = Vy = 0;
            Tilt = 0;
            _squat = 0;
            if (wall)
            {
                g.Fx.Shake(9);
                g.Fx.Sound("slam", 0.7f);
                g.Fx.Burst(X, Y, FrostColor, 20, 260, 6, 0.6f);
                IceRing(g, 10, 200, g.R.Range(0, C.Tau), r: 8);
            }
            yield return 0.5f;
        }
        Mv = ("chase", Spd * 0.7f, 0, 0);
    }

    // ---------------------------------------------------------------- zmiana fazy
    protected override IEnumerator<float> PhaseChange(RunSim g)
    {
        var it = base.PhaseChange(g);
        while (it.MoveNext()) yield return it.Current;
        // mroźna nova: pierścień z chłodem i sople
        g.Fx.Ring(X, Y, FrostColor, 260, 0.6f, 9);
        IceRing(g, 28, 210, 0, r: 9);
        for (int i = 0; i < 3; i++)
        {
            var c = Clamp(X + MathF.Cos(i * C.Tau / 3 + 0.5f) * 220, Y + MathF.Sin(i * C.Tau / 3 + 0.5f) * 220, 60);
            g.Effects.Add(new Telegraph(c.X, c.Y, 44, 1.0f, g2 =>
            {
                g2.Fx.Sound("break", 0.5f);
                g2.Fx.Burst(c.X, c.Y, FrostColor, 12, 220, 5, 0.5f);
                for (int k = 0; k < 6; k++)
                    Ice(g2, k * C.Tau / 6, 200, 6, style: "needle", x: c.X, y: c.Y);
                FrostZone(g2, c.X, c.Y, 46, 2.5f, 0.5f);
            }, IceColor));
        }
        Mv = ("chase", Spd * 0.7f, 0, 0);
        yield return 0.5f;
    }
}
