using System;
using System.Collections.Generic;
using Godot;
using Otchlan.Sim;
using Otchlan.UI;

namespace Otchlan.View;

/// <summary>
/// Efekty wizualne zgłaszane przez symulację (cząsteczki, pierścienie, liczby obrażeń, pioruny).
/// Rysowane w jednym _Draw – wydajne przy setkach cząsteczek. Współrzędne = jednostki symulacji.
/// </summary>
public partial class FxLayer : Node2D, IFx
{
    private struct Particle
    {
        public Vector2 P, V;
        public float Life, Max, Size, Grav;
        public Color Color;
        public bool Glow;
        public byte Kind; // 0 kropka, 1 dym, 2 kwadrat
    }

    private struct RingFx
    {
        public Vector2 P;
        public float R, Life, Max, Width;
        public Color Color;
    }

    private struct FloatText
    {
        public Vector2 P, V;
        public string Text;
        public Color Color;
        public float Size, Life, Max;
    }

    private struct BoltFx
    {
        public Vector2[] Pts;
        public float Life;
    }

    private const int MaxParticles = 1200;
    private readonly List<Particle> _parts = new();
    private readonly List<RingFx> _rings = new();
    private readonly List<FloatText> _texts = new();
    private readonly List<BoltFx> _bolts = new();
    private readonly Random _r = new();

    public float ShakeAmt;
    public float HurtFlashT;
    public Action<string, string, Color, bool> OnBanner;
    public Action<float, float, Color, float> OnStain;
    public Action<string, float, float> OnSound;
    public float ShakeSetting = 1f;

    private float Rf(float a, float b) => a + (float)_r.NextDouble() * (b - a);

    public void Burst(float x, float y, Color color, int n, float speed, float size, float life, float grav = 0, bool glow = false, string kind = "dot")
    {
        byte k = kind switch { "smoke" => 1, "square" => 2, _ => 0 };
        for (int i = 0; i < n && _parts.Count < MaxParticles; i++)
        {
            float a = Rf(0, Mathf.Tau), s = Rf(0.2f, 1f) * speed;
            float l = life * Rf(0.6f, 1.2f);
            _parts.Add(new Particle
            {
                P = new Vector2(x, y), V = new Vector2(MathF.Cos(a), MathF.Sin(a)) * s, Life = l, Max = l,
                Size = size * Rf(0.6f, 1.2f), Grav = grav, Color = color, Glow = glow, Kind = k,
            });
        }
    }

    public void Ring(float x, float y, Color color, float radius, float life, float width = 4) =>
        _rings.Add(new RingFx { P = new Vector2(x, y), R = radius, Life = life, Max = life, Width = width, Color = color });

    public void Text(float x, float y, string text, Color color, float size, float life = 0.8f)
    {
        if (_texts.Count > 80) _texts.RemoveAt(0);
        _texts.Add(new FloatText { P = new Vector2(x, y), V = new Vector2(Rf(-15, 15), -60), Text = text, Color = color, Size = size, Life = life, Max = life });
    }

    public void Spark(float x, float y, Color color, int n, float speed, float angle, float spread = 0.6f, float life = 0.15f)
    {
        for (int i = 0; i < n && _parts.Count < MaxParticles; i++)
        {
            float a = angle + Rf(-spread, spread), s = speed * Rf(0.5f, 1.2f);
            _parts.Add(new Particle { P = new Vector2(x, y), V = new Vector2(MathF.Cos(a), MathF.Sin(a)) * s, Life = life, Max = life, Size = 3, Color = color, Glow = true });
        }
    }

    public void Bolt(float x1, float y1, float x2, float y2)
    {
        int n = 8;
        var pts = new Vector2[n + 1];
        var a = new Vector2(x1, y1);
        var b = new Vector2(x2, y2);
        var perp = (b - a).Orthogonal().Normalized();
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n;
            pts[i] = a.Lerp(b, t) + (i == 0 || i == n ? Vector2.Zero : perp * Rf(-14, 14));
        }
        _bolts.Add(new BoltFx { Pts = pts, Life = 0.18f });
    }

    public void Shake(float amount) => ShakeAmt = MathF.Min(22, MathF.Max(ShakeAmt, amount * ShakeSetting));
    public void Sound(string name, float volume = 1f, float pitchVar = 0f) => OnSound?.Invoke(name, volume, pitchVar);
    public void Banner(string title, string sub, Color color, bool priority = false) => OnBanner?.Invoke(title, sub, color, priority);
    public void Stain(float x, float y, Color color, float r) => OnStain?.Invoke(x, y, color, r);
    public void HurtFlash() => HurtFlashT = 0.4f;

    public void Clear()
    {
        _parts.Clear();
        _rings.Clear();
        _texts.Clear();
        _bolts.Clear();
    }

    public void Step(float dt)
    {
        ShakeAmt = MathF.Max(0, ShakeAmt - dt * 30);
        HurtFlashT = MathF.Max(0, HurtFlashT - dt);
        for (int i = _parts.Count - 1; i >= 0; i--)
        {
            var p = _parts[i];
            p.Life -= dt;
            if (p.Life <= 0) { _parts.RemoveAt(i); continue; }
            p.V *= MathF.Max(0, 1 - (p.Kind == 1 ? 2.5f : 3.5f) * dt);
            p.V.Y += p.Grav * dt;
            p.P += p.V * dt;
            _parts[i] = p;
        }
        for (int i = _rings.Count - 1; i >= 0; i--)
        {
            var r = _rings[i];
            r.Life -= dt;
            if (r.Life <= 0) _rings.RemoveAt(i);
            else _rings[i] = r;
        }
        for (int i = _texts.Count - 1; i >= 0; i--)
        {
            var t = _texts[i];
            t.Life -= dt;
            if (t.Life <= 0) { _texts.RemoveAt(i); continue; }
            t.P += t.V * dt;
            t.V *= MathF.Max(0, 1 - 3 * dt);
            _texts[i] = t;
        }
        for (int i = _bolts.Count - 1; i >= 0; i--)
        {
            var b = _bolts[i];
            b.Life -= dt;
            if (b.Life <= 0) _bolts.RemoveAt(i);
            else _bolts[i] = b;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var ink = UiTheme.Ink;
        foreach (var p in _parts)
        {
            float f = p.Life / p.Max;
            var c = p.Color;
            switch (p.Kind)
            {
                case 1:
                    c.A = 0.45f * f;
                    DrawCircle(p.P, p.Size * (1.6f - 0.6f * f), c);
                    break;
                case 2:
                    c.A = MathF.Min(1, f * 2);
                    float s = p.Size;
                    DrawRect(new Rect2(p.P - new Vector2(s, s) / 2, new Vector2(s, s)), c);
                    break;
                default:
                    if (p.Glow)
                    {
                        var g = c;
                        g.A = 0.3f * f;
                        DrawCircle(p.P, p.Size * 1.9f, g);
                    }
                    c.A = MathF.Min(1, f * 1.6f);
                    DrawCircle(p.P, MathF.Max(0.6f, p.Size * (0.4f + 0.6f * f)), c);
                    break;
            }
        }
        foreach (var r in _rings)
        {
            float f = 1 - r.Life / r.Max;
            var c = r.Color;
            c.A = 1 - f;
            DrawArc(r.P, MathF.Max(1, r.R * (0.3f + 0.7f * MathF.Sqrt(f))), 0, Mathf.Tau, 48, c, MathF.Max(1, r.Width * (1 - f * 0.6f)));
        }
        foreach (var b in _bolts)
        {
            DrawPolyline(b.Pts, new Color(0.6f, 0.8f, 1f, 0.5f), 7);
            DrawPolyline(b.Pts, new Color(0.95f, 0.98f, 1f), 3);
        }
        var font = UiTheme.Bold;
        foreach (var t in _texts)
        {
            float f = t.Life / t.Max;
            int fs = (int)(t.Size * (f > 0.85f ? 1 + (f - 0.85f) * 2 : 1));
            var size = font.GetStringSize(t.Text, HorizontalAlignment.Left, -1, fs);
            var pos = t.P - new Vector2(size.X / 2, -fs * 0.35f);
            var c = t.Color;
            c.A = MathF.Min(1, f * 3);
            var o = ink;
            o.A = c.A;
            DrawStringOutline(font, pos, t.Text, HorizontalAlignment.Left, -1, fs, 5, o);
            DrawString(font, pos, t.Text, HorizontalAlignment.Left, -1, fs, c);
        }
    }
}
