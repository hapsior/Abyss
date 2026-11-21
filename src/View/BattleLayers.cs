using System;
using Godot;
using Otchlan.Sim;
using Otchlan.UI;

namespace Otchlan.View;

/// <summary>Pociski graczy (grafiki SVG) i wrogów (kreskówkowe kule rysowane w kodzie, dowolny kolor).</summary>
public partial class BulletView : Node2D
{
    private RunSim _g;

    public void Bind(RunSim g)
    {
        _g = g;
        ZIndex = 5;
    }

    public override void _Draw()
    {
        var ink = UiTheme.Ink;
        foreach (var b in _g.Bullets)
        {
            if (!b.Alive) continue;
            var pos = new Vector2(b.X, b.Y);
            if (!b.Friendly)
            {
                var glow = b.Color;
                glow.A = 0.25f;
                DrawCircle(pos, b.R * 2.1f, glow);
                DrawCircle(pos, b.R + 2.5f, ink);
                DrawCircle(pos, b.R, b.Color);
                DrawCircle(pos, MathF.Max(1.5f, b.R * 0.5f), new Color(1, 0.96f, 0.96f));
                continue;
            }
            var tex = Art.Projectile("p_" + (b.Style ?? "orb"));
            bool elongated = b.Style is "arrow" or "dagger";
            float size = b.R * (elongated ? 3.6f : 2.6f);
            if (b.Crit)
            {
                var cg = new Color(1, 0.86f, 0.3f, 0.35f);
                DrawCircle(pos, size * 0.8f, cg);
            }
            if (tex == null)
            {
                DrawCircle(pos, b.R + 2, ink);
                DrawCircle(pos, b.R, b.Color);
                continue;
            }
            float ang = elongated || b.Style is "disc" or "magic" ? (elongated ? b.Angle : _g.Time * 12) : 0;
            var half = elongated ? new Vector2(size, size / 2) : new Vector2(size, size);
            DrawSetTransform(pos, ang, Vector2.One);
            DrawTextureRect(tex, new Rect2(-half / 2 * 1, half), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }
}

/// <summary>Efekty na ziemi (chmury trucizny, ogień, czarne dziury, ostrzeżenia), lasery, znaczniki pojawiania się, orbity, duszki.</summary>
public partial class GroundFxView : Node2D
{
    private RunSim _g;

    public void Bind(RunSim g)
    {
        _g = g;
        ZIndex = -2;
    }

    public override void _Draw()
    {
        var ink = UiTheme.Ink;
        float t = _g.Time;
        foreach (var ef in _g.Effects)
        {
            switch (ef)
            {
                case PoisonCloud pc:
                {
                    float f = MathF.Min(1, MathF.Min(pc.T / 0.5f, (pc.Dur - pc.T) / 0.25f + 0.2f));
                    float rr = pc.R * (0.9f + 0.1f * MathF.Sin(t * 3 + pc.X));
                    var c = pc.Color;
                    DrawCircle(new Vector2(pc.X, pc.Y), rr, new Color(c, 0.27f * f));
                    DrawArc(new Vector2(pc.X, pc.Y), rr, 0, Mathf.Tau, 40, new Color(c, 0.55f * f), 3);
                    break;
                }
                case FirePool fp:
                {
                    float f = MathF.Min(1, fp.T / 0.4f);
                    DrawCircle(new Vector2(fp.X, fp.Y), fp.R, new Color(1, 0.43f, 0.12f, (fp.Warm > 0 ? 0.2f : 0.43f) * f));
                    DrawArc(new Vector2(fp.X, fp.Y), fp.R, 0, Mathf.Tau, 40, new Color(1, 0.78f, 0.24f, 0.67f * f), 3);
                    break;
                }
                case BlackHole bh:
                {
                    float f = MathF.Min(1, MathF.Min(bh.T / 0.3f, (bh.Dur - bh.T) / 0.2f));
                    float r = 34 * f + MathF.Sin(t * 12) * 2;
                    DrawCircle(new Vector2(bh.X, bh.Y), r * 2.2f, new Color(0.47f, 0.2f, 0.78f, 0.25f));
                    DrawCircle(new Vector2(bh.X, bh.Y), MathF.Max(2, r), new Color("0a0014"));
                    DrawArc(new Vector2(bh.X, bh.Y), MathF.Max(3, r), 0, Mathf.Tau, 32, new Color("c882ff"), 3);
                    break;
                }
                case HazardZone hz:
                {
                    float f = MathF.Min(1, MathF.Min(hz.T / 0.4f, (hz.Dur - hz.T) / 0.3f + 0.2f));
                    float warm = hz.Warm > 0 ? 0.45f : 1f;
                    DrawCircle(new Vector2(hz.X, hz.Y), hz.R, new Color(hz.Color, 0.3f * f * warm));
                    DrawArc(new Vector2(hz.X, hz.Y), hz.R, 0, Mathf.Tau, 40, new Color(hz.Color, 0.7f * f), hz.Warm > 0 ? 2 : 3);
                    break;
                }
                case Telegraph tg:
                {
                    float f = 1 - tg.T / tg.Dur;
                    DrawCircle(new Vector2(tg.X, tg.Y), tg.R * f, new Color(tg.Color, 0.16f + 0.24f * f));
                    DrawArc(new Vector2(tg.X, tg.Y), tg.R, 0, Mathf.Tau, 40, new Color(tg.Color, 0.8f), 3);
                    break;
                }
            }
        }
        // znaczniki pojawiania się wrogów
        foreach (var s in _g.Spawns)
        {
            float f = 1 - s.T / MathF.Max(0.01f, s.Dur);
            var c = s.Elite ? new Color("ffd246") : new Color("ff5a78");
            DrawArc(new Vector2(s.X, s.Y), 18 * (1.4f - 0.4f * f), 0, Mathf.Tau, 24, new Color(c, 0.4f + 0.5f * f), 3);
            DrawCircle(new Vector2(s.X, s.Y), 14 * f, new Color(c, 0.25f));
        }
        // lasery (snajper itp.)
        foreach (var e in _g.Enemies)
            if (e.LaserEnd is { } le)
            {
                var a = new Vector2(e.X, e.Y);
                float pulse = 0.5f + 0.5f * MathF.Sin(t * 30);
                DrawLine(a, le, new Color(1, 0.3f, 0.3f, 0.25f + 0.25f * pulse), 4);
                DrawLine(a, le, new Color(1, 0.85f, 0.5f, 0.8f), 1.5f);
            }
    }
}

/// <summary>Rzeczy nad postaciami: orbity, duszki, kolby w locie, cienie po zrywie.</summary>
public partial class TopFxView : Node2D
{
    private RunSim _g;

    public void Bind(RunSim g)
    {
        _g = g;
        ZIndex = 6;
    }

    public override void _Draw()
    {
        var ink = UiTheme.Ink;
        foreach (var p in _g.Players)
        {
            if (p.Dead) continue;
            foreach (var gh in p.Ghosts)
                DrawCircle(new Vector2(gh.x, gh.y), p.R * 1.1f, new Color(p.Def.BodyColor, 0.35f * gh.life / 0.25f));
            foreach (var o in p.OrbitalPositions())
            {
                DrawCircle(o, 10, ink);
                DrawCircle(o, 7, new Color("5ae6e6"));
                DrawCircle(o + new Vector2(-2, -2), 2.5f, Colors.White);
            }
            foreach (var f in p.Fams)
            {
                var pos = new Vector2(f.X, f.Y + MathF.Sin(f.T * 4) * 4);
                var tex = Art.Item("duszek");
                if (tex != null)
                    DrawTextureRect(tex, new Rect2(pos - new Vector2(13, 13), new Vector2(26, 26)), false);
            }
        }
        foreach (var ef in _g.Effects)
            if (ef is Flask fl)
            {
                float f = fl.Progress;
                var ground = new Vector2(fl.X0 + (fl.X1 - fl.X0) * f, fl.Y0 + (fl.Y1 - fl.Y0) * f);
                var pos = ground - new Vector2(0, MathF.Sin(f * MathF.PI) * 90);
                DrawCircle(ground, 7, new Color(0, 0, 0, 0.25f));
                var tex = Art.Item("jad");
                if (tex != null)
                    DrawTextureRect(tex, new Rect2(pos - new Vector2(11, 11), new Vector2(22, 22)), false);
            }
    }
}
