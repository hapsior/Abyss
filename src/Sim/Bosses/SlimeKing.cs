using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Otchlan.Sim.Bosses;

// WZORZEC dla portu pozostałych bossów (bosses.py → C#): generator atk_* → metoda IEnumerator<float>,
// „yield 0” → yield return 0, „yield 0.3” → yield return 0.3f, self.dt → Dt,
// random.* → g.R (rozgrywka). Wygląd: Z (wysokość skoku), SquashX/Y, Charge, Alpha.

/// <summary>Król Glutów (Piwnice): skoki z pierścieniami pocisków, wielki skok z przywołaniem glutów, plucie.</summary>
[EnemyType("slime_king")]
public sealed class SlimeKing : Boss
{
    private float _squash;

    public override void Update(float dt, RunSim g)
    {
        base.Update(dt, g);
        _squash = MathF.Max(0, _squash - dt * 4);
        SquashX = 1 + 0.18f * _squash;
        SquashY = 1 - 0.18f * _squash;
        Lift = Z;
    }

    private void Land(RunSim g, int n, float speed = 170, float offset = 0)
    {
        FireRing(g, n, speed, offset, r: 9);
        g.Fx.Shake(6);
        g.Fx.Sound("slam", 0.6f);
        g.Fx.Burst(X, Y + R * 0.7f, new Color("5aaa50"), 16, 260, 6, 0.5f);
        _squash = 1;
    }

    protected override IEnumerator<float> Attack(string name, RunSim g) => name switch
    {
        "hops" => Hops(g),
        "jump" => Jump(g),
        _ => Spit(g),
    };

    private IEnumerator<float> Hops(RunSim g)
    {
        int hops = Phase == 1 ? 3 : 4;
        for (int k = 0; k < hops; k++)
        {
            var tp = TargetPos(g);
            float sx = X, sy = Y, T = Phase == 1 ? 0.55f : 0.45f, t = 0;
            while (t < T)
            {
                t += Dt;
                float f = MathF.Min(1, t / T);
                X = sx + (tp.X - sx) * f * 0.7f;
                Y = sy + (tp.Y - sy) * f * 0.7f;
                Z = MathF.Sin(f * MathF.PI) * 70;
                Invuln = Z > 40;
                yield return 0;
            }
            Z = 0;
            Invuln = false;
            Land(g, Phase == 1 ? 10 : 14, offset: g.R.NextFloat());
            yield return 0.3f;
        }
    }

    private IEnumerator<float> Jump(RunSim g)
    {
        g.Fx.Sound("charge", 0.6f);
        float t = 0;
        while (t < 0.5f)
        {
            t += Dt;
            Z = (t / 0.5f) * (t / 0.5f) * 700;
            Invuln = Z > 40;
            ContactOn = false;
            yield return 0;
        }
        t = 0;
        while (t < 1.1f)
        {
            t += Dt;
            var tp = TargetPos(g);
            X += (tp.X - X) * MathF.Min(1, Dt * 3);
            Y += (tp.Y - Y) * MathF.Min(1, Dt * 3);
            yield return 0;
        }
        t = 0;
        while (t < 0.35f)
        {
            t += Dt;
            float k = 1 - t / 0.35f;
            Z = 700 * k * k;
            yield return 0;
        }
        Z = 0;
        Invuln = false;
        ContactOn = true;
        Land(g, 20, 190);
        g.Fx.Shake(12);
        if (Phase >= 2)
        {
            yield return 0.2f;
            FireRing(g, 20, 130, C.Tau / 40, r: 9, color: C.EnemyBullet2);
        }
        if (g.Enemies.Count(e => e.Key == "slime") < 4)
            for (int i = 0; i < 2; i++)
            {
                float a = g.R.Range(0, C.Tau);
                var e = g.SpawnEnemy("slime", X + MathF.Cos(a) * 60, Y + MathF.Sin(a) * 60, minion: true);
                e?.Knock(MathF.Cos(a) * 400, MathF.Sin(a) * 400);
            }
        yield return 0.5f;
    }

    private IEnumerator<float> Spit(RunSim g)
    {
        for (int k = 0; k < (Phase == 1 ? 3 : 5); k++)
        {
            _squash = 0.6f;
            FireSpread(g, AngleTo(g), 5, 0.2f, 210, r: 11, color: C.EnemyBullet2);
            g.Fx.Sound("enemy_shoot");
            yield return 0.45f;
        }
    }
}
