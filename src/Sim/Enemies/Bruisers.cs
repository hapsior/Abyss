using System;
using Godot;

namespace Otchlan.Sim.Enemies;

/// <summary>
/// Dzik: szarżuje na gracza (walk → windup → charge → stun). Wersja 1.1: szarżę zaczyna tylko z linią strzału
/// (HasLos) i z żetonem "charge"; żeton oddaje po zakończeniu szarży.
/// </summary>
[EnemyType("charger")]
public sealed class Charger : Enemy
{
    /// <summary>Kierunek szarży (widok rysuje z niego linię ostrzegawczą w stanie "windup").</summary>
    public Vector2 Dir = new(1, 0);

    public override void Setup(RunSim g)
    {
        State = "walk";
        Timer = g.R.Range(1.0f, 2.0f);
    }

    public override void Ai(float dt, RunSim g)
    {
        Timer -= dt;
        Charge = State == "windup" ? 1 - MathF.Max(0, Timer) / 0.6f : 0;
        switch (State)
        {
            case "walk":
                Chase(g, Spd, dt);
                if (Timer <= 0 && DistTo(g) < 460 && HasLos(g) && g.TakeToken(this, "charge", 1.5f))
                {
                    State = "windup";
                    Timer = 0.6f;
                    float a = AngleTo(g);
                    Dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
                    g.Fx.Sound("charge", 0.5f);
                }
                break;
            case "windup":
            {
                float a = AngleTo(g);
                if (Timer > 0.2f)
                    Dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
                if (Timer <= 0)
                {
                    State = "charge";
                    Timer = 0.75f;
                }
                break;
            }
            case "charge":
            {
                float sp = 560 * Bspd;
                Vx = Dir.X * sp;
                Vy = Dir.Y * sp;
                float nx = X + Vx * dt * 1.5f, ny = Y + Vy * dt * 1.5f;
                var ob = g.Room.TileAt(nx + Dir.X * R, ny + Dir.Y * R);
                bool wall = !(C.RX0 + R < nx && nx < C.RX1 - R && C.RY0 + R < ny && ny < C.RY1 - R);
                if (ob is { Solid: true } || wall)
                {
                    State = "stun";
                    Timer = 0.7f;
                    ReleaseToken(g);
                    g.Fx.Shake(4);
                    g.Fx.Sound("thunk");
                    g.Fx.Burst(X + Dir.X * R, Y + Dir.Y * R, Color.Color8(200, 180, 160), 10, 200, 4, 0.4f);
                    if (ob != null && ob.Kind is ObstacleKind.Crate or ObstacleKind.Barrel)
                        g.DamageObstacle(ob, 999);
                    Vx = Vy = 0;
                }
                else if (Timer <= 0)
                {
                    State = "walk";
                    Timer = g.R.Range(1.2f, 2.0f);
                    ReleaseToken(g);
                }
                if (g.Cos.NextDouble() < dt * 30)
                    g.Fx.Burst(X, Y + R, Color.Color8(170, 150, 130), 1, 60, 4, 0.4f, kind: "smoke");
                break;
            }
            case "stun":
                if (Timer <= 0)
                {
                    State = "walk";
                    Timer = g.R.Range(1.0f, 1.8f);
                }
                break;
        }
    }
}

/// <summary>Golem: wolno idzie, co kilka sekund unosi pięści i uderza – pierścień pocisków (elita: 18).</summary>
[EnemyType("golem")]
public sealed class Golem : Enemy
{
    private float _lift;

    public override void Setup(RunSim g)
    {
        Timer = g.R.Range(2.0f, 3.5f);
        State = "walk";
    }

    public override void Ai(float dt, RunSim g)
    {
        Timer -= dt * Fr;
        if (State == "walk")
        {
            Chase(g, Spd, dt);
            Charge = 0;
            if (Timer <= 0 && DistTo(g) < 430)
            {
                State = "windup";
                Timer = 0.7f;
            }
        }
        else if (State == "windup")
        {
            _lift = MathF.Min(1.0f, _lift + dt * 2);
            Lift = _lift * 12;
            Charge = 1 - MathF.Max(0, Timer) / 0.7f;
            if (Timer <= 0)
            {
                State = "walk";
                Timer = 3.4f;
                _lift = 0;
                Lift = 0;
                Charge = 0;
                int n = Extra == 0 ? 12 : 18;
                FireRing(g, n, 180, g.R.Range(0, 1), 8, C.EnemyBullet2);
                g.Fx.Shake(5);
                g.Fx.Sound("slam", 0.7f);
                g.Fx.Ring(X, Y + R * 0.5f, Color.Color8(200, 190, 170), 90, 0.35f, 6);
                g.Fx.Burst(X, Y + R * 0.6f, Color.Color8(160, 150, 140), 14, 220, 6, 0.5f, kind: "smoke");
            }
        }
    }
}

/// <summary>Wybuchacz: biegnie do gracza i detonuje się z bliska (lont 0.75 s). Zabity przez gracza wybucha słabiej, raniąc tylko wrogów.</summary>
[EnemyType("bomber")]
public sealed class Bomber : Enemy
{
    private float _fuse = -1;
    private bool _detonated;

    public override void Ai(float dt, RunSim g)
    {
        if (_fuse < 0)
        {
            Chase(g, Spd, dt);
            if (DistTo(g) < 78)
            {
                _fuse = 0.75f;
                g.Fx.Sound("charge", 0.4f);
            }
            return;
        }
        _fuse -= dt;
        float s = 1 + (0.75f - MathF.Max(0, _fuse)) * 0.4f;
        SquashX = SquashY = s;
        Charge = 1 - MathF.Max(0, _fuse) / 0.75f;
        if (_fuse <= 0)
        {
            _detonated = true;
            Alive = false; // żeby własny wybuch nie zranił samego wybuchacza
            Hp = 0;
            g.Explode(X, Y, 88, 22 * DmgScale, hurtPlayer: true, hurtEnemies: true);
            g.KillEnemy(this, drops: false);
        }
    }

    public override void OnDeath(RunSim g)
    {
        if (_detonated) return;
        g.Explode(X, Y, 70, 30, hurtPlayer: false, hurtEnemies: true, small: true);
    }
}

/// <summary>Kolczak: odbija się od ścian i przeszkód po przekątnych, nie goni gracza.</summary>
[EnemyType("spinner")]
public sealed class Spinner : Enemy
{
    private Vector2 _dir;

    public override void Setup(RunSim g)
    {
        float a = (0.25f + 0.5f * g.R.RangeInt(0, 3)) * MathF.PI + g.R.Range(-0.2f, 0.2f);
        _dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
    }

    public override void Ai(float dt, RunSim g)
    {
        float sp = Spd;
        float nx = X + _dir.X * sp * dt, ny = Y + _dir.Y * sp * dt;
        if (nx < C.RX0 + R || nx > C.RX1 - R) _dir.X *= -1;
        if (ny < C.RY0 + R || ny > C.RY1 - R) _dir.Y *= -1;
        if (g.Room.TileAt(nx + _dir.X * R, Y) is { Solid: true }) _dir.X *= -1;
        if (g.Room.TileAt(X, ny + _dir.Y * R) is { Solid: true }) _dir.Y *= -1;
        Vx = _dir.X * sp;
        Vy = _dir.Y * sp;
    }
}
