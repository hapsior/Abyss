using System;
using Godot;

namespace Otchlan.Sim.Enemies;

/// <summary>
/// Oko (wieża): nieruchome, strzela pierścieniem pocisków (4 na przemian z przesunięciem, elita 8).
/// Wersja 1.1: strzela tylko z linią strzału do gracza i z żetonem ataku; bez nich po prostu czeka.
/// </summary>
[EnemyType("turret")]
public sealed class Turret : Enemy
{
    private int _mode;

    public override void Setup(RunSim g)
    {
        Timer = g.R.Range(1.0f, 2.2f);
        _mode = g.R.RangeInt(0, 1);
    }

    public override void Ai(float dt, RunSim g)
    {
        Timer -= dt * Fr;
        Charge = Timer < 0.4f ? MathF.Max(0, 1 - Timer / 0.4f) : 0;
        if (Timer > 0) return;
        if (!HasLos(g) || !TakeToken(g, "shot"))
        {
            Timer = 0.3f; // czekaj na czystą linię / wolny żeton
            return;
        }
        Timer = 2.3f;
        Charge = 0;
        int n = Extra == 0 ? 4 : 8;
        float off = n == 4 ? MathF.PI / 4 * _mode : 0;
        FireRing(g, n, 175, off, color: C.EnemyBullet3);
        _mode ^= 1;
        g.Fx.Sound("enemy_shoot", 0.6f);
    }
}

/// <summary>
/// Snajper: stany "move"/"aim". Trzyma dystans 240–600, bez linii strzału wychyla się zza przeszkody
/// (SeekFiringSpot), do celowania potrzebuje żetonu "shot", przerywa celowanie po utracie linii na &gt;0.3 s
/// i tuż przed strzałem jeszcze raz sprawdza grubą linię strzału.
/// </summary>
[EnemyType("sniper")]
public sealed class Sniper : Enemy
{
    public const float MinD = 240, MaxD = 600;
    private float _aim, _lost;

    /// <summary>Laser jest „zablokowany” (ostatnie 0.3 s celowania) – widok może go rysować pełną linią.</summary>
    public bool Locked => State == "aim" && Timer <= 0.3f;

    public override void Setup(RunSim g)
    {
        State = "move";
        Timer = g.R.Range(0.6f, 1.2f);
    }

    public override void Ai(float dt, RunSim g)
    {
        Timer -= dt;
        bool los = HasLos(g);
        float d = DistTo(g);
        AimVisual = float.NaN;
        LaserEnd = null;
        Charge = 0;
        if (State == "move")
        {
            bool inRange = d > MinD * 0.8f && d < MaxD * 1.15f;
            if (los && inRange && Timer <= 0 && g.TakeToken(this, "shot", 1.7f))
            {
                State = "aim";
                Timer = 1.25f / Fr;
                _lost = 0;
                Path = null;
                _aim = AimAt(g, 640);
                g.Fx.Sound("laser", 0.35f, 0.2f);
            }
            else if (!los || !inRange)
                SeekFiringSpot(g, dt, MinD, MaxD, Spd);
            else if (Timer > 0)
            {
                // z czystą linią: krąży wokół gracza w oczekiwaniu na strzał
                float pa = AngleTo(g) + MathF.PI / 2;
                Vx = MathF.Cos(pa) * Spd * 0.5f;
                Vy = MathF.Sin(pa) * Spd * 0.5f;
            }
        }
        if (State == "aim")
        {
            if (!los)
            {
                _lost += dt;
                if (_lost > 0.3f)
                {
                    State = "move";
                    Timer = 0.15f;
                    Path = null;
                    ReleaseToken(g);
                    return;
                }
            }
            else
                _lost = 0;
            if (Timer > 0.3f)
                _aim = AimAt(g, 640);
            if (Timer <= 0)
            {
                var t = TargetPos(g);
                if (los && g.Room.LosThick(X, Y, t.X, t.Y))
                {
                    var yellow = Color.Color8(255, 240, 120);
                    Fire(g, _aim, 640, 6, yellow, 1.4f);
                    if (Extra > 0)
                        FireSpread(g, _aim, 2, 0.35f, 520, 6, yellow);
                    g.Fx.Sound("enemy_shoot", 0.9f);
                    g.Fx.Spark(X, Y, Color.Color8(255, 240, 150), 6, 300, _aim, 0.4f);
                }
                State = "move";
                Timer = g.R.Range(0.9f, 1.5f);
                Path = null;
                ReleaseToken(g);
                return;
            }
            AimVisual = _aim;
            LaserEnd = g.Room.Raycast(X, Y, _aim);
            Charge = 1 - MathF.Min(1, Timer / (1.25f / Fr));
        }
    }
}

/// <summary>
/// Nekromanta: przyzywa nietoperze (na piętrze 2 także gluty) jako sługi – maks. 6 naraz – i ucieka przed graczem.
/// Z bliska teleportuje się (znika i pojawia się w innym miejscu).
/// </summary>
[EnemyType("summoner")]
public sealed class Summoner : Enemy
{
    public const int MaxMinions = 6;
    private float _tpCd, _cast, _fade;
    private Vector2? _tpTarget;

    public float Cast => _cast;

    public override void Setup(RunSim g)
    {
        Timer = g.R.Range(1.5f, 2.5f);
        _tpCd = 2.0f;
    }

    public override void Ai(float dt, RunSim g)
    {
        float d = DistTo(g);
        _tpCd -= dt;
        if (_fade > 0)
        {
            _fade -= dt;
            if (_fade <= 0.25f && _tpTarget is { } tp)
            {
                var green = Color.Color8(120, 255, 160);
                g.Fx.Burst(X, Y, green, 14, 200, 4, 0.4f);
                X = tp.X;
                Y = tp.Y;
                _tpTarget = null;
                g.Fx.Burst(X, Y, green, 14, 200, 4, 0.4f);
            }
            Alpha = _fade > 0 ? MathF.Abs(_fade - 0.25f) / 0.25f : 1;
            return;
        }
        Alpha = 1;
        if (d < 170 && _tpCd <= 0)
        {
            _tpCd = 3.5f;
            _fade = 0.5f;
            _tpTarget = g.RandomFreePos(320);
            return;
        }
        if (d < 300)
            Flee(g, Spd);
        else if (d > 450)
            Chase(g, Spd * 0.8f, dt);
        Timer -= dt * Fr;
        if (Timer < 0.6f)
            _cast = 1 - MathF.Max(0, Timer) / 0.6f;
        if (Timer <= 0)
        {
            Timer = 4.5f;
            _cast = 0;
            int mine = 0;
            foreach (var e in g.Enemies)
                if (e.Summoner == this && e.Alive) mine++;
            int n = Math.Min(2 + (Elite ? 1 : 0), MaxMinions - mine);
            for (int i = 0; i < n; i++)
            {
                float a = g.R.Range(0, C.Tau);
                string kind = g.Depth != 2 ? "bat" : (g.R.Chance(0.5f) ? "bat" : "slime");
                var e = g.SpawnEnemy(kind, X + MathF.Cos(a) * 30, Y + MathF.Sin(a) * 30, minion: true);
                if (e != null) e.Summoner = this;
            }
            if (n > 0)
            {
                g.Fx.Sound("spawn", 0.8f);
                g.Fx.Ring(X, Y, Color.Color8(120, 255, 160), 50, 0.3f);
            }
            if (Extra > 1)
                FireRing(g, 8, 160, color: C.EnemyBullet3);
        }
    }
}
