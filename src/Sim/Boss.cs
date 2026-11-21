using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Otchlan.Sim;

/// <summary>
/// Bazowy boss. Ataki to koroutyny (iteratory) jak generatory atk_* w wersji 1.x:
///   yield return 0     – kontynuuj w następnej klatce,
///   yield return 0.3f  – odczekaj 0.3 s.
/// Bieżący czas klatki jest w <see cref="Dt"/>. Fazy zmieniają się przy progach HP (Phases),
/// zmiana fazy przerywa atak i resetuje stan (<see cref="ResetState"/>).
///
/// Nowy boss: klasa z [EnemyType("klucz")] dziedzicząca po Boss, nadpisz <see cref="Attack"/>
/// (switch po nazwach z data/bosses.json → attacks) i ewentualnie <see cref="AvailableAttacks"/>.
/// </summary>
public abstract class Boss : Enemy
{
    public override bool IsBoss => true;

    public BossDef BDef;
    public string Title;
    public float Intro = 1.8f;
    public int Phase = 1;
    public float Dt;
    public float Z;
    public float Tempo = 1;
    public bool ContactOn = true;
    public string LastAttack;
    /// <summary>Ruch zlecony przez atak: ("chase", speed) / ("to", x, y, speed) / ("dir", vx, vy) / null.</summary>
    public (string kind, float a, float b, float c)? Mv;

    // limit DPS (model Gungeon)
    public readonly Queue<(float t, float dmg)> DmgLog = new();
    public float DmgRecent;

    private IEnumerator<float> _script;
    private float _wait;

    public void InitBoss(RunSim g, BossDef def, float x, float y)
    {
        BDef = def;
        G = g;
        Key = def.Key;
        Name = def.Name;
        Title = def.Title;
        Color = new Color(def.Color);
        Flying = def.Flying;
        KnockResist = 0.04f;
        X = x;
        Y = y;
        R = def.Radius;
        float hp = Balance.BossHp(g.Depth) * def.HpMult * g.Diff.EnemyHp * g.SealMod("boss_hp") * g.CoopBossMult();
        MaxHp = Hp = hp;
        DmgScale = Balance.EnemyDmgScale(g.Depth) * g.Diff.EnemyDmg * g.SealMod("enemy_dmg");
        ContactDmg = def.Contact * DmgScale;
        BulletDmg = 8 * DmgScale;
        Spd = def.Speed * Balance.EnemySpeedScale(g.Depth);
        Fr = g.Diff.FireRate;
        Bspd = g.Diff.BulletSpeed;
        Extra = g.Diff.ExtraBullets;
        Lead = g.Diff.Lead;
        Tempo = Balance.BossTempo(g.Depth) * g.SealMod("boss_tempo") * (g.Coop ? 1.08f : 1);
        T = g.CosRange(0, 10);
        Setup(g);
    }

    public float Frac => Hp / MaxHp;

    public virtual string[] AvailableAttacks() => BDef.Attacks;

    /// <summary>Zwraca koroutynę ataku o danej nazwie.</summary>
    protected abstract IEnumerator<float> Attack(string name, RunSim g);

    private IEnumerator<float> NextAttack(RunSim g)
    {
        var all = AvailableAttacks();
        var opts = all.Where(a => a != LastAttack).ToArray();
        if (opts.Length == 0) opts = all;
        var a = opts[g.R.RangeInt(0, opts.Length - 1)];
        LastAttack = a;
        return Attack(a, g);
    }

    public override void Ai(float dt, RunSim g)
    {
        Dt = dt;
        if (Intro > 0)
        {
            Intro -= dt;
            return;
        }
        int newPhase = 1 + BDef.Phases.Count(th => Frac <= th);
        if (newPhase > Phase)
        {
            Phase = newPhase;
            _script = PhaseChange(g);
            _wait = 0;
        }
        _wait -= dt * Tempo;
        int guard = 0;
        while (_wait <= 0 && guard++ < 50)
        {
            _script ??= NextAttack(g);
            if (!_script.MoveNext())
            {
                _script = null;
                _wait += g.R.Range(BDef.Rest[0], BDef.Rest[1]) / (1 + 0.15f * (Phase - 1));
                continue;
            }
            float w = _script.Current;
            if (w <= 0)
            {
                _wait = 0;
                break;
            }
            _wait += w;
        }
        if (Mv is { } mv)
        {
            switch (mv.kind)
            {
                case "chase":
                    Chase(g, mv.a, dt);
                    break;
                case "to":
                {
                    float dx = mv.a - X, dy = mv.b - Y, d = MathF.Sqrt(dx * dx + dy * dy);
                    if (d > 6) { Vx = dx / d * mv.c; Vy = dy / d * mv.c; }
                    else Mv = null;
                    break;
                }
                case "dir":
                    Vx = mv.a;
                    Vy = mv.b;
                    break;
            }
        }
    }

    /// <summary>Przywraca stan po przerwaniu ataku (np. przy zmianie fazy). Nadpisz, jeśli boss ma własne pola stanu.</summary>
    public virtual void ResetState()
    {
        Z = 0;
        Invuln = false;
        ContactOn = true;
        Mv = null;
        Alpha = 1;
        Lift = 0;
        Charge = 0;
        LaserEnd = null;
    }

    protected virtual IEnumerator<float> PhaseChange(RunSim g)
    {
        g.Fx.Sound("boss_roar", 0.8f);
        g.Fx.Shake(10);
        g.Fx.Ring(X, Y, new Color("ff5050"), 200, 0.5f, 8);
        ResetState();
        g.ClearEnemyBullets();
        float t = 0;
        while (t < 1)
        {
            t += Dt;
            Flash = (int)(t * 12) % 2 == 1 ? 0.05f : 0;
            yield return 0;
        }
        yield return 0.3f;
    }

    /// <summary>Czeka T sekund klatka po klatce (użycie: foreach (var w in WaitTime(1)) yield return w;).</summary>
    protected IEnumerable<float> WaitTime(float T)
    {
        float t = 0;
        while (t < T)
        {
            t += Dt;
            yield return 0;
        }
    }
}
