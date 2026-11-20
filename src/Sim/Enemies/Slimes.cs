using System;
using Godot;

namespace Otchlan.Sim.Enemies;

// WZORZEC dla portu pozostałych wrogów (enemies.py → C#):
// - klasa z atrybutem [EnemyType("klucz z data/enemies.json")],
// - stan inicjalizowany w Setup, zachowanie w Ai(dt, g),
// - wygląd: ustaw Lift / SquashX / SquashY / Tilt / Charge / Alpha (widok rysuje sprite z art/enemies/<klucz>.svg),
// - losowość rozgrywki: g.R (deterministyczna), kosmetyka: g.Cos / g.CosRange,
// - strzały: Fire / FireSpread / FireRing / AimAt; przed strzałem: HasLos(g) i TakeToken(g, "shot").

/// <summary>Glut: skacze w stronę gracza cyklami (0.38 cyklu ruch, reszta odpoczynek).</summary>
[EnemyType("slime")]
public class Slime : Enemy
{
    protected float Cycle;

    public override void Setup(RunSim g) => Cycle = g.CosRange(0, 1);

    public override void Ai(float dt, RunSim g)
    {
        Cycle += dt / 0.9f;
        float ph = Cycle % 1f;
        if (ph < 0.38f)
        {
            Chase(g, Spd * 2.4f, dt);
            float s = MathF.Sin(ph / 0.38f * MathF.PI);
            SquashX = 1 - 0.12f * s;
            SquashY = 1 + 0.15f * s;
            Lift = s * 10;
        }
        else
        {
            float k = MathF.Max(0, 1 - (ph - 0.38f) / 0.2f);
            SquashX = 1 + 0.18f * k;
            SquashY = 1 - 0.18f * k;
            Lift = 0;
        }
    }
}

/// <summary>Wielki Glut: po śmierci rozpada się na dwa gluty.</summary>
[EnemyType("big_slime")]
public sealed class BigSlime : Slime
{
    public override void OnDeath(RunSim g)
    {
        for (int i = 0; i < 2; i++)
        {
            float a = g.R.Range(0, C.Tau);
            var e = g.SpawnEnemy("slime", X + MathF.Cos(a) * 14, Y + MathF.Sin(a) * 14);
            e?.Knock(MathF.Cos(a) * 300, MathF.Sin(a) * 300);
        }
    }
}

/// <summary>
/// Kultysta: trzyma dystans 210–360 i strzela serią. Wersja 1.1: strzela tylko z czystą linią strzału
/// (gdy jej nie ma – idzie na pozycję, z której widać gracza) i tylko z żetonem ataku.
/// </summary>
[EnemyType("shooter")]
public sealed class Shooter : Enemy
{
    private int _strafe;

    public override void Setup(RunSim g)
    {
        Timer = g.R.Range(1.0f, 2.0f);
        _strafe = g.R.Chance(0.5f) ? 1 : -1;
    }

    public override void Ai(float dt, RunSim g)
    {
        float d = DistTo(g), a = AngleTo(g);
        bool los = HasLos(g);
        if (!los)
            SeekFiringSpot(g, dt, 200, 380, Spd);
        else if (d < 210)
            Flee(g, Spd);
        else if (d > 360)
            Chase(g, Spd, dt);
        else
        {
            float pa = a + MathF.PI / 2 * _strafe;
            // nie wchodź za przeszkodę bokiem
            var nx = X + MathF.Cos(pa) * 30;
            var ny = Y + MathF.Sin(pa) * 30;
            var t = TargetPos(g);
            if (!g.Room.LosThick(nx, ny, t.X, t.Y))
                _strafe = -_strafe;
            Vx = MathF.Cos(pa) * Spd * 0.6f;
            Vy = MathF.Sin(pa) * Spd * 0.6f;
            if (g.R.NextFloat() < dt * 0.4f) _strafe = -_strafe;
        }
        Timer -= dt * Fr;
        Charge = Timer < 0.35f ? 1 - MathF.Max(0, Timer) / 0.35f : 0;
        if (Timer <= 0)
        {
            if (!los || !TakeToken(g, "shot"))
            {
                Timer = 0.25f; // spróbuj za chwilę
                return;
            }
            Timer = g.R.Range(1.6f, 2.2f);
            Charge = 0;
            int n = 1 + Extra * 2;
            FireSpread(g, AimAt(g, 235), n, 0.22f, 235);
            g.Fx.Sound("enemy_shoot", 0.6f);
        }
    }
}
