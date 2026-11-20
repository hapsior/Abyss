using System;
using Godot;

namespace Otchlan.Sim.Enemies;

/// <summary>Nietoperz: leci falującym torem w stronę gracza.</summary>
[EnemyType("bat")]
public sealed class Bat : Enemy
{
    private float _wob;

    public override void Setup(RunSim g) => _wob = g.R.Range(0, C.Tau);

    public override void Ai(float dt, RunSim g)
    {
        _wob += dt * 5;
        float a = AngleTo(g) + MathF.Sin(_wob) * 0.9f;
        Vx = MathF.Cos(a) * Spd;
        Vy = MathF.Sin(a) * Spd;
    }
}

/// <summary>Upiór: przenika przeszkody, kołysze się w locie i co kilka sekund robi szarżę (elita: pierścień pocisków).</summary>
[EnemyType("ghost")]
public sealed class Ghost : Enemy
{
    private float _dash, _wob;
    private Vector2 _dir;

    public override void Setup(RunSim g)
    {
        Timer = g.R.Range(2.0f, 3.5f);
        _wob = g.R.Range(0, C.Tau);
    }

    public override void Ai(float dt, RunSim g)
    {
        Timer -= dt;
        _wob += dt * 3;
        if (_dash > 0)
        {
            _dash -= dt;
            Vx = _dir.X * 330 * Bspd;
            Vy = _dir.Y * 330 * Bspd;
            if (g.Cos.NextDouble() < dt * 40)
                g.Fx.Burst(X, Y, Color.Color8(180, 210, 255), 1, 10, 6, 0.4f);
            return;
        }
        float a = AngleTo(g) + MathF.Sin(_wob) * 0.6f;
        Vx = MathF.Cos(a) * Spd;
        Vy = MathF.Sin(a) * Spd;
        if (Timer <= 0)
        {
            Timer = g.R.Range(2.6f, 3.6f);
            _dash = 0.5f;
            float da = AngleTo(g);
            _dir = new Vector2(MathF.Cos(da), MathF.Sin(da));
            if (Extra > 1)
                FireRing(g, 6, 140, color: C.EnemyBullet3);
        }
    }
}

/// <summary>
/// Chochlik: krąży wokół gracza, strzela wachlarzem przyspieszających pocisków i teleportuje się.
/// Wersja 1.1: strzela tylko z żetonem ataku; bez linii strzału nie strzela, tylko teleportuje się
/// w miejsce, z którego widzi cel (RandomFreePos z needLos, maxDist 520).
/// </summary>
[EnemyType("imp")]
public sealed class Imp : Enemy
{
    private float _fade;

    public override void Setup(RunSim g)
    {
        Timer = g.R.Range(0.8f, 1.6f);
        State = "idle";
        _fade = 0;
    }

    public override void Ai(float dt, RunSim g)
    {
        Timer -= dt * Fr;
        switch (State)
        {
            case "idle":
            {
                float a = AngleTo(g) + MathF.PI / 2;
                Vx = MathF.Cos(a) * Spd * 0.5f;
                Vy = MathF.Sin(a) * Spd * 0.5f;
                Charge = Timer is > 0 and < 0.4f ? 1 - Timer / 0.4f : 0;
                if (Timer > 0) break;
                if (!HasLos(g))
                {
                    // brak linii strzału: bez strzału, od razu zmiana pozycji
                    Timer = 0;
                    State = "out";
                    _fade = 0.4f;
                }
                else if (!g.TakeToken(this, "shot", 0.6f))
                    Timer = 0.3f;
                else
                {
                    int n = 5 + Extra * 2;
                    float aim = AimAt(g, 130);
                    for (int i = 0; i < n; i++)
                        Fire(g, aim + (i - (n - 1) / 2f) * 0.2f, 130, color: C.EnemyBullet2, accel: 220, maxSpeed: 330);
                    g.Fx.Sound("enemy_shoot", 0.7f);
                    State = "out";
                    _fade = 0.4f;
                    Timer = 2.0f;
                    Charge = 0;
                    if (g.R.NextFloat() < 0.35f)
                        g.Effects.Add(new FirePool(X, Y + 6, 36, 10 * DmgScale, 3.0f));
                }
                break;
            }
            case "out":
                _fade -= dt;
                Alpha = MathF.Max(0, _fade) / 0.4f;
                Invuln = _fade < 0.2f;
                if (_fade <= 0)
                {
                    var p = g.RandomFreePos(220, fly: true, needLos: this, maxDist: 520);
                    X = p.X;
                    Y = p.Y;
                    State = "in";
                    _fade = 0.4f;
                    g.Fx.Burst(X, Y, Color.Color8(255, 120, 50), 10, 150, 4, 0.4f);
                }
                break;
            case "in":
                _fade -= dt;
                Alpha = 1 - MathF.Max(0, _fade) / 0.4f;
                Invuln = _fade > 0.2f;
                if (_fade <= 0)
                {
                    State = "idle";
                    Alpha = 1;
                    Invuln = false;
                    Timer = g.R.Range(0.7f, 1.3f);
                    ReleaseToken(g);
                    LosT = 0; // od razu odśwież linię strzału po teleporcie
                }
                break;
        }
    }
}
