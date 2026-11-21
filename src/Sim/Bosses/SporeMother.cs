using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Otchlan.Sim.Bosses;

/// <summary>
/// Matka Grzybni (Grzybnia, piętro 25): zakorzeniony grzyb. Zasypuje pokój wolnymi zarodnikami,
/// zakwita trującymi polami pod graczami, puszcza korzenie (łańcuch erupcji w stronę gracza)
/// i przywołuje gluty oraz bombowce. W 2. fazie zakopuje się i wyrasta w innym miejscu.
/// Stan wizualny: <see cref="Enemy.SquashX"/>/<see cref="Enemy.SquashY"/> (pulsowanie kapelusza),
/// <see cref="Enemy.Alpha"/> (zakopywanie), <see cref="Enemy.Charge"/> (nabieranie zarodników przed salwą).
/// </summary>
[EnemyType("spore_mother")]
public sealed class SporeMother : Boss
{
    public static readonly Color SporeColor = new("b07cff");
    public static readonly Color ToxicColor = new("c8ff46");
    public static readonly Color RootColor = new("c08a4a");

    private float _pulse;
    private const int MaxZones = 9;

    public override void Setup(RunSim g) => _pulse = 0;

    public override void ResetState()
    {
        base.ResetState();
        _pulse = 0;
        SquashX = SquashY = 1;
    }

    public override void Update(float dt, RunSim g)
    {
        base.Update(dt, g);
        _pulse = MathF.Max(0, _pulse - dt * 3);
        float breathe = 0.03f * MathF.Sin(T * 2.2f);
        SquashX = 1 + breathe + 0.16f * _pulse;
        SquashY = 1 - breathe - 0.14f * _pulse;
    }

    public override void Ai(float dt, RunSim g)
    {
        base.Ai(dt, g);
        if (Mv == null)
        {
            // zakorzeniona – nie chodzi, jedynie hamuje odrzut
            Vx *= 0.8f;
            Vy *= 0.8f;
        }
    }

    public override string[] AvailableAttacks() =>
        Phase >= 2 ? BDef.Attacks.Append("burrow").ToArray() : BDef.Attacks;

    protected override IEnumerator<float> Attack(string name, RunSim g) => name switch
    {
        "bloom" => Bloom(g),
        "roots" => Roots(g),
        "brood" => Brood(g),
        "burrow" => Burrow(g),
        _ => Spores(g),
    };

    private int ZoneCount(RunSim g) => g.Effects.Count(e => e is HazardZone && e.Alive);

    private static Vector2 Clamp(float x, float y, float m) =>
        new(Math.Clamp(x, C.RX0 + m, C.RX1 - m), Math.Clamp(y, C.RY0 + m, C.RY1 - m));

    // ---------------------------------------------------------------- zarodniki
    /// <summary>Pierścienie dużych, hamujących pocisków – pokój wypełnia się unoszącą się chmurą zarodników.</summary>
    private IEnumerator<float> Spores(RunSim g)
    {
        float t = 0;
        while (t < 0.45f)
        {
            t += Dt;
            Charge = t / 0.45f;
            yield return 0;
        }
        Charge = 0;
        int volleys = Phase == 1 ? 3 : 4;
        float off = g.R.Range(0, C.Tau);
        for (int k = 0; k < volleys; k++)
        {
            _pulse = 1;
            int n = 12 + Extra * 2;
            float turn = Phase >= 2 ? (k % 2 == 0 ? 0.45f : -0.45f) : 0;
            for (int i = 0; i < n; i++)
            {
                var b = Fire(g, off + i * C.Tau / n, 270, 11, SporeColor, accel: -230, turn: turn, life: 5.5f);
                if (b != null) b.MinSpeed = 50;
            }
            off += C.Tau / n / 2;
            g.Fx.Sound("poison", 0.5f);
            g.Fx.Burst(X, Y - R * 0.4f, SporeColor, 10, 160, 5, 0.5f, grav: -60);
            yield return 0.6f;
        }
        if (Phase >= 2)
        {
            FireSpread(g, AimAt(g, 240), 5, 0.16f, 240, 8, ToxicColor);
            g.Fx.Sound("enemy_shoot");
            yield return 0.3f;
        }
    }

    // ---------------------------------------------------------------- kwitnienie
    /// <summary>Ostrzeżenia pod graczami (i w losowych miejscach), po chwili zamieniają się w trujące, spowalniające pola.</summary>
    private IEnumerator<float> Bloom(RunSim g)
    {
        _pulse = 0.7f;
        int n = Phase == 1 ? 3 : 5;
        var spots = g.ActivePlayers().Select(p => new Vector2(p.X, p.Y)).ToList();
        int guard = 0;
        while (spots.Count < n && guard++ < 20)
        {
            var s = g.RandomFreePos(minDist: 120, margin: 80);
            if (spots.All(o => o.DistanceTo(s) > 130)) spots.Add(s);
        }
        float dmg = BulletDmg * 0.7f;
        int room = MaxZones - ZoneCount(g);
        for (int i = 0; i < Math.Min(spots.Count, Math.Max(1, room)); i++)
        {
            var c = Clamp(spots[i].X, spots[i].Y, 70);
            g.Effects.Add(new Telegraph(c.X, c.Y, 74, 1.05f + i * 0.12f, g2 =>
            {
                g2.Effects.Add(new HazardZone(c.X, c.Y, 74, dmg, Phase >= 2 ? 6f : 5f, ToxicColor, warm: 0, slow: 0.35f));
                g2.Fx.Sound("poison", 0.6f);
                g2.Fx.Ring(c.X, c.Y, ToxicColor, 90, 0.35f, 5);
                g2.Fx.Burst(c.X, c.Y, ToxicColor, 14, 200, 5, 0.6f, grav: -80);
                for (int k = 0; k < 6; k++)
                    Fire(g2, k * C.Tau / 6 + 0.3f, 150, 7, ToxicColor, x: c.X, y: c.Y);
            }, ToxicColor));
        }
        g.Fx.Sound("charge", 0.5f);
        yield return 1.3f;
    }

    // ---------------------------------------------------------------- korzenie
    /// <summary>Łańcuch erupcji od bossa w stronę gracza; w 2. fazie trzy korzenie wachlarzem.</summary>
    private IEnumerator<float> Roots(RunSim g)
    {
        int lines = Phase == 1 ? 1 : 3;
        float dmg = BulletDmg;
        for (int v = 0; v < 2; v++)
        {
            _pulse = 1;
            g.Fx.Shake(4);
            g.Fx.Sound("thunk", 0.7f);
            float baseA = AngleTo(g);
            for (int l = 0; l < lines; l++)
            {
                float a = baseA + (l - (lines - 1) / 2f) * 0.42f;
                for (int s = 1; s <= 11; s++)
                {
                    float d = R + 10 + s * 56;
                    float x = X + MathF.Cos(a) * d, y = Y + MathF.Sin(a) * d;
                    if (x < C.RX0 + 20 || x > C.RX1 - 20 || y < C.RY0 + 20 || y > C.RY1 - 20) break;
                    bool thorns = s % 3 == 0;
                    g.Effects.Add(new Telegraph(x, y, 30, 0.45f + s * 0.08f, g2 =>
                    {
                        g2.Effects.Add(new HazardZone(x, y, 30, dmg, 0.9f, RootColor, warm: 0, slow: 0.5f, kind: "roots"));
                        g2.Fx.Burst(x, y, RootColor, 6, 170, 5, 0.4f);
                        g2.Fx.Sound("break", 0.25f);
                        if (thorns)
                            for (int k = 0; k < 4; k++)
                                Fire(g2, a + C.Tau / 8 + k * C.Tau / 4, 170, 7, RootColor, x: x, y: y, style: "needle");
                    }, RootColor));
                }
            }
            yield return Phase == 1 ? 1.2f : 1.0f;
        }
    }

    // ---------------------------------------------------------------- potomstwo
    private IEnumerator<float> Brood(RunSim g)
    {
        _pulse = 1;
        yield return 0.4f;
        int mine = g.Enemies.Count(e => e.Minion && e.Alive);
        int cap = Phase == 1 ? 5 : 7;
        string[] kinds = Phase == 1 ? new[] { "slime", "bomber" } : new[] { "slime", "bomber", "spinner" };
        for (int i = 0; i < Math.Min(3, cap - mine); i++)
        {
            float a = g.R.Range(0, C.Tau);
            var e = g.SpawnEnemy(kinds[g.R.RangeInt(0, kinds.Length - 1)], X + MathF.Cos(a) * (R + 20), Y + MathF.Sin(a) * (R + 20), minion: true);
            e?.Knock(MathF.Cos(a) * 380, MathF.Sin(a) * 380);
        }
        g.Fx.Sound("spawn");
        g.Fx.Burst(X, Y, SporeColor, 20, 240, 6, 0.6f);
        if (Phase >= 2)
            FireRing(g, 16 + Extra * 2, 150, g.R.Range(0, 1), r: 8, color: SporeColor);
        yield return 0.6f;
    }

    // ---------------------------------------------------------------- zakopanie (faza 2)
    private IEnumerator<float> Burrow(RunSim g)
    {
        ContactOn = false;
        g.Fx.Sound("thunk");
        g.Fx.Burst(X, Y + R * 0.6f, RootColor, 18, 220, 6, 0.6f);
        float t = 0;
        while (t < 0.5f)
        {
            t += Dt;
            Alpha = 1 - t / 0.5f;
            Invuln = t > 0.25f;
            yield return 0;
        }
        Alpha = 0;
        Invuln = true;
        var dest = g.RandomFreePos(minDist: 260, margin: R + 40);
        g.Effects.Add(new Telegraph(dest.X, dest.Y, R + 24, 0.8f, _ => { }, SporeColor));
        foreach (var w in WaitTime(0.8f)) yield return w;
        X = dest.X;
        Y = dest.Y;
        Vx = Vy = 0;
        t = 0;
        while (t < 0.3f)
        {
            t += Dt;
            Alpha = t / 0.3f;
            yield return 0;
        }
        Alpha = 1;
        Invuln = false;
        ContactOn = true;
        _pulse = 1;
        g.Fx.Shake(8);
        g.Fx.Sound("slam", 0.6f);
        g.Fx.Burst(X, Y, RootColor, 24, 280, 6, 0.6f);
        FireRing(g, 18 + Extra * 2, 190, g.R.Range(0, 1), r: 9, color: SporeColor);
        if (ZoneCount(g) < MaxZones)
            g.Effects.Add(new HazardZone(X, Y, R + 40, BulletDmg * 0.7f, 3.5f, ToxicColor, warm: 0.3f, slow: 0.35f));
        yield return 0.5f;
    }

    protected override IEnumerator<float> PhaseChange(RunSim g)
    {
        var it = base.PhaseChange(g);
        while (it.MoveNext()) yield return it.Current;
        // rozkwit: pierścień zarodników i pola wokół bossa
        _pulse = 1;
        FireRing(g, 24, 220, 0, r: 10, color: SporeColor);
        for (int i = 0; i < 4; i++)
        {
            float a = i * C.Tau / 4 + C.Tau / 8;
            var c = Clamp(X + MathF.Cos(a) * 190, Y + MathF.Sin(a) * 190, 70);
            g.Effects.Add(new HazardZone(c.X, c.Y, 62, BulletDmg * 0.7f, 4.5f, ToxicColor, warm: 0.6f, slow: 0.35f));
        }
        yield return 0.5f;
    }
}
