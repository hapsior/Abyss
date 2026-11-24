using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Otchlan.Core;
using Otchlan.Input;
using Otchlan.Sim;
using Otchlan.UI;

namespace Otchlan.View;

/// <summary>
/// HUD wyprawy. Panele graczy w górnych rogach (P1 lewy, P2 prawy), monety i piętro na środku,
/// minimapa, pasek bossa na dole, komunikaty (bannery) na środku ekranu. Rysowany w _Draw –
/// kotwiczony do krawędzi, więc działa przy każdych proporcjach ekranu.
/// </summary>
public partial class RunHud : Control
{
    private readonly RunScene _scene;
    private RunSim G => _scene.Sim;
    private readonly List<(string title, string sub, Color color, float t, float dur)> _banners = new();
    private float _t;
    /// <summary>Kolejka toastów osiągnięć (wyświetlane po jednym, w lewym dolnym rogu).</summary>
    private readonly List<(AchievementDef def, float t)> _toasts = new();
    private const float ToastDur = 4.2f;

    public RunHud(RunScene scene)
    {
        _scene = scene;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void PushBanner(string title, string sub, Color color, bool priority)
    {
        var b = (title, sub, color, 0f, 2.4f);
        if (priority && _banners.Count > 0)
        {
            var first = _banners[0];
            first.dur = MathF.Min(first.dur, first.t + 0.25f);
            _banners[0] = first;
            _banners.Insert(1, b);
        }
        else
            _banners.Add(b);
    }

    /// <summary>Toast „Osiągnięcie odblokowane” (nie zasłania paska u góry, banera piętra ani paska bossa).</summary>
    public void PushAchievement(AchievementDef def)
    {
        _toasts.Add((def, 0f));
        Audio.Sfx.Play("levelup", 0.6f);
    }

    public void Step(float dt)
    {
        _t += dt;
        if (_scene.Overlay == null && _toasts.Count > 0)
        {
            var (def, t) = _toasts[0];
            t += dt * (_toasts.Count > 1 ? 1.5f : 1f);   // przy kolejce toasty zmieniają się szybciej
            if (t > ToastDur) _toasts.RemoveAt(0);
            else _toasts[0] = (def, t);
        }
        if (_scene.Overlay == null && _banners.Count > 0)
        {
            var b = _banners[0];
            b.t += dt;
            if (_banners.Count > 1 && b.t > 1.1f) b.dur = MathF.Min(b.dur, b.t + 0.35f);
            _banners[0] = b;
            if (b.t > b.dur) _banners.RemoveAt(0);
        }
        QueueRedraw();
    }

    private static readonly Color Ink = UiTheme.Ink;

    private void Bar(Rect2 r, float frac, Color fill, Color back, string text = null, int fs = 22)
    {
        DrawStyleBox(UiTheme.Box(back, (int)(r.Size.Y / 2), 4, 3), r);
        if (frac > 0)
        {
            var fr = new Rect2(r.Position + new Vector2(4, 4), new Vector2((r.Size.X - 8) * Mathf.Clamp(frac, 0, 1), r.Size.Y - 8));
            var sb = new StyleBoxFlat { BgColor = fill };
            sb.SetCornerRadiusAll((int)(fr.Size.Y / 2));
            DrawStyleBox(sb, fr);
            DrawRect(new Rect2(fr.Position + new Vector2(4, 2), new Vector2(MathF.Max(0, fr.Size.X - 8), 3)), new Color(1, 1, 1, 0.3f));
        }
        if (text != null) Text(text, r.GetCenter() + new Vector2(0, fs * 0.36f), fs, UiTheme.Text, HorizontalAlignment.Center);
    }

    private void Text(string s, Vector2 pos, int size, Color color, HorizontalAlignment align = HorizontalAlignment.Left, Font font = null, int outline = 6)
    {
        font ??= UiTheme.Bold;
        var w = font.GetStringSize(s, HorizontalAlignment.Left, -1, size).X;
        var p = align switch
        {
            HorizontalAlignment.Center => pos - new Vector2(w / 2, 0),
            HorizontalAlignment.Right => pos - new Vector2(w, 0),
            _ => pos,
        };
        DrawStringOutline(font, p, s, HorizontalAlignment.Left, -1, size, outline, Ink);
        DrawString(font, p, s, HorizontalAlignment.Left, -1, size, color);
    }

    public override void _Draw()
    {
        var size = Size;
        var g = G;
        // pasek u góry
        DrawRect(new Rect2(0, 0, size.X, 96), new Color(0.08f, 0.05f, 0.11f, 0.86f));
        DrawRect(new Rect2(0, 94, size.X, 4), Ink);
        for (int i = 0; i < g.Players.Count; i++)
            PlayerPanel(g.Players[i], i == 0 ? 20 : size.X - 20, i == 1);

        // środek: piętro, monety, czas
        var b = g.Floor.Biome;
        string floorTxt = g.Depth > C.StoryFloors ? $"Głębia {g.Depth - C.StoryFloors} · {b.Name}" : $"Piętro {g.Depth} · {b.Name}";
        float cx = size.X / 2;
        Text(floorTxt, new Vector2(cx, 36), 30, b.AccentColor, HorizontalAlignment.Center, UiTheme.Title, 8);
        var coin = Art.Ui("coin");
        if (coin != null) DrawTextureRect(coin, new Rect2(cx - 120, 52, 32, 32), false);
        Text($"{g.Coins}", new Vector2(cx - 82, 79), 28, new Color("ffd23f"));
        int m = (int)g.RunTime / 60, s = (int)g.RunTime % 60;
        var clock = Art.Ui("clock");
        if (clock != null) DrawTextureRect(clock, new Rect2(cx + 20, 54, 28, 28), false);
        Text($"{m:00}:{s:00}", new Vector2(cx + 54, 78), 24, g.Diff.Color != null ? new Color(g.Diff.Color) : UiTheme.Text);
        if (_scene.AutoAimToggled || Game.I.Settings.AutoAimP1)
        {
            bool manual = _scene.Sources.Count > 0 && _scene.Sources[0].ManualOverride;
            Text(manual ? "AUTO-CEL · celujesz sam" : "AUTO-CEL", new Vector2(cx, 120), 18,
                manual ? new Color("96dcff", 0.55f) : new Color("96dcff"), HorizontalAlignment.Center);
        }

        Minimap(new Vector2(size.X - (g.Coop ? 210 : 30), g.Coop ? 120 : 112), g.Coop);
        if (g.Boss is { } boss && boss.Alive) BossBar(boss, size);
        Banner(size);
        Toast(size);

        // przyciemnienie przy obrażeniach
        if (_scene.Fx.HurtFlashT > 0)
            DrawRect(new Rect2(Vector2.Zero, size), new Color(0.8f, 0, 0.1f, 0.18f * _scene.Fx.HurtFlashT / 0.4f));
        // przejście między pokojami / piętrami
        if (g.State == RunState.Descend)
        {
            float f = g.StateT < 0.6f ? g.StateT / 0.6f : 1 - (g.StateT - 0.6f) / 0.6f;
            DrawRect(new Rect2(Vector2.Zero, size), new Color(0, 0, 0, Mathf.Clamp(f, 0, 1)));
        }
        else if (g.State == RunState.Transition)
            DrawRect(new Rect2(Vector2.Zero, size), new Color(0, 0, 0, 0.5f * (1 - g.StateT / 0.3f)));
        else if (g.State == RunState.Dying)
            DrawRect(new Rect2(Vector2.Zero, size), new Color(0.2f, 0, 0.05f, MathF.Min(0.6f, g.StateT * 0.4f)));
    }

    private void PlayerPanel(PlayerSim p, float x0, bool mirror)
    {
        float bw = 300;
        float bx = mirror ? x0 - bw : x0 + 70;
        // portret z umiejętnością
        var ar = new Rect2(mirror ? x0 - bw - 76 : x0, 12, 62, 62);
        if (mirror) ar.Position = new Vector2(x0 - 62, 12);
        if (mirror) bx = ar.Position.X - 10 - bw;
        DrawStyleBox(UiTheme.Box(new Color("3a2c57"), 14, 4, 3), ar);
        var tex = Art.Character(p.Key);
        if (tex != null)
        {
            DrawSetTransform(ar.GetCenter() + new Vector2(0, 2), 0, new Vector2(mirror ? -1 : 1, 1));
            DrawTextureRect(tex, new Rect2(-27, -27, 54, 54), false, p.Downed ? new Color(0.5f, 0.5f, 0.5f) : Colors.White);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
        if (p.AbilityT > 0)
        {
            float f = p.AbilityT / MathF.Max(0.1f, p.AbilityCd * p.AbilityCdMult);
            DrawRect(new Rect2(ar.Position + new Vector2(4, 4), new Vector2(ar.Size.X - 8, (ar.Size.Y - 8) * f)), new Color(0, 0, 0, 0.6f));
            Text($"{MathF.Ceiling(p.AbilityT):0}", ar.GetCenter() + new Vector2(0, 10), 26, UiTheme.Text, HorizontalAlignment.Center);
        }
        else
            DrawRect(ar.Grow(1), new Color("ffd23f"), false, 3);
        // HP
        string hpTxt = p.Downed ? "POWALONY" : $"{MathF.Ceiling(p.Hp):0} / {p.MaxHp:0}";
        if (G.Coop) hpTxt = $"{PlayerSim.Tags[p.Idx].tag}  {hpTxt}";
        Bar(new Rect2(bx, 12, bw, 36), p.Downed ? 0 : p.Hp / p.MaxHp, new Color("e84a5a"), new Color("3c1820"), hpTxt, 22);
        // XP + poziom
        Bar(new Rect2(bx, 52, bw - 90, 22), p.Xp / p.XpNeeded(), new Color("7ee05a"), new Color("1d3320"));
        Text($"Poz. {p.Level}", new Vector2(bx + bw - 82, 72), 22, new Color("b4ffb4"));
        // zrywy
        for (int i = 0; i < (int)p.DashCharges; i++)
        {
            var c = new Vector2(bx + 10 + i * 22, 86);
            DrawCircle(c, 8, Ink);
            if (i < p.DashLeft) DrawCircle(c, 6, new Color("aadcff"));
            else if (i == p.DashLeft)
            {
                DrawCircle(c, 6, new Color("3c5064"));
                DrawCircle(c, 6 * p.DashRecharge / p.DashCd, new Color("78a0c8"));
            }
        }
        // Rewolwerowiec: naboje w bębenku
        if (p.Key == "rewolwerowiec")
            for (int i = 0; i < PlayerSim.DrumSize; i++)
            {
                var r = new Rect2(bx + bw - 14 - (PlayerSim.DrumSize - i) * 15, 78, 10, 17);
                DrawRect(r.Grow(2), Ink);
                DrawRect(r, i < p.Ammo ? (i == p.Ammo - 1 && p.Ammo == 1 ? new Color("ff8a3c") : new Color("ffd27a")) : new Color("4a3a2a"));
            }
    }

    private void Minimap(Vector2 topRight, bool below)
    {
        var f = G.Floor;
        const float cell = 18, gap = 4;
        var seen = f.Rooms.Values.Where(r => r.Seen).ToList();
        if (seen.Count == 0) return;
        int minX = seen.Min(r => r.Gx), maxX = seen.Max(r => r.Gx), minY = seen.Min(r => r.Gy), maxY = seen.Max(r => r.Gy);
        float w = (maxX - minX + 1) * (cell + gap), h = (maxY - minY + 1) * (cell * 0.7f + gap);
        var origin = new Vector2(topRight.X - w, topRight.Y);
        DrawStyleBox(UiTheme.Box(new Color(0.1f, 0.07f, 0.14f, 0.8f), 10, 3, 0), new Rect2(origin - new Vector2(10, 8), new Vector2(w + 16, h + 14)));
        foreach (var r in seen)
        {
            var p = origin + new Vector2((r.Gx - minX) * (cell + gap), (r.Gy - minY) * (cell * 0.7f + gap));
            var rr = new Rect2(p, new Vector2(cell, cell * 0.7f));
            var col = r == G.Room ? new Color("fff6e8") : r.Visited ? new Color("8e8aa6") : new Color("4a4560");
            DrawRect(rr, col);
            DrawRect(rr, Ink, false, 2);
            string icon = r.Kind switch
            {
                RoomKind.Boss => "skull",
                RoomKind.Treasure => "star",
                RoomKind.Shop => "coin",
                RoomKind.Shrine => "stat_lifesteal",
                RoomKind.Exit => "lock",
                _ => null,
            };
            if (icon != null && Art.Ui(icon) is { } t)
                DrawTextureRect(t, new Rect2(rr.GetCenter() - new Vector2(8, 8), new Vector2(16, 16)), false);
        }
    }

    private void BossBar(Boss boss, Vector2 size)
    {
        float w = MathF.Min(900, size.X * 0.6f);
        var r = new Rect2((size.X - w) / 2, size.Y - 70, w, 34);
        Text(boss.Name, new Vector2(size.X / 2, r.Position.Y - 10), 30, new Color("ff6a6a"), HorizontalAlignment.Center, UiTheme.Title, 8);
        Bar(r, boss.Hp / boss.MaxHp, new Color("e84a5a"), new Color("3c1820"));
        foreach (var th in boss.BDef.Phases)
        {
            float x = r.Position.X + 4 + (r.Size.X - 8) * th;
            DrawLine(new Vector2(x, r.Position.Y + 2), new Vector2(x, r.End.Y - 2), Ink, 3);
        }
    }

    private void Toast(Vector2 size)
    {
        if (_toasts.Count == 0 || _scene.Overlay != null) return;
        var (def, t) = _toasts[0];
        float slide = Mathf.Clamp(t / 0.35f, 0, 1), fade = Mathf.Clamp((ToastDur - t) / 0.4f, 0, 1);
        slide = 1 - (1 - slide) * (1 - slide);
        float a = MathF.Min(fade, Mathf.Clamp(t / 0.2f, 0, 1));
        var tint = AchievementIcons.Tint(def);
        const float w = 560, h = 104;
        var r = new Rect2(new Vector2(28 - (1 - slide) * (w + 40), size.Y - 232), new Vector2(w, h));
        DrawStyleBox(UiTheme.Box(new Color(0.12f, 0.08f, 0.18f, 0.94f * a), 18, 5, 5, new Color(tint.R, tint.G, tint.B, a)), r);
        var icon = new Rect2(r.Position + new Vector2(14, 12), new Vector2(80, 80));
        DrawRect(icon, new Color(tint.R * 0.35f, tint.G * 0.35f, tint.B * 0.35f, a));
        var tex = AchievementIcons.Texture(def);
        if (tex != null) DrawTextureRect(tex, icon.Grow(-6), false, new Color(1, 1, 1, a));
        float x = r.Position.X + 110;
        Text("OSIĄGNIĘCIE ODBLOKOWANE", new Vector2(x, r.Position.Y + 30), 19, new Color(UiTheme.Accent, a), outline: 4);
        Text(def.Name, new Vector2(x, r.Position.Y + 62), 32, new Color(UiTheme.Text, a), HorizontalAlignment.Left, null, 8);
        string d = def.Desc.Length > 52 ? def.Desc[..49] + "…" : def.Desc;
        Text(d, new Vector2(x, r.Position.Y + 88), 18, new Color(UiTheme.TextDim, a), outline: 4);
    }

    private void Banner(Vector2 size)
    {
        if (_banners.Count == 0 || _scene.Overlay != null) return;
        var b = _banners[0];
        float a = MathF.Min(1, MathF.Min(b.t / 0.25f, (b.dur - b.t) / 0.35f));
        if (a <= 0) return;
        float y = size.Y * 0.24f;
        var bg = new Color(0.08f, 0.05f, 0.11f, 0.75f * a);
        DrawRect(new Rect2(0, y - 56, size.X, 96), bg);
        var c = b.color;
        c.A = a;
        Text(b.title, new Vector2(size.X / 2, y), 54, c, HorizontalAlignment.Center, UiTheme.Title, 12);
        if (!string.IsNullOrEmpty(b.sub))
            Text(b.sub, new Vector2(size.X / 2, y + 32), 24, new Color(UiTheme.TextDim, a), HorizontalAlignment.Center);
    }
}
