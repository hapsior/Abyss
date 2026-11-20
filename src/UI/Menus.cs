using System;
using System.Collections.Generic;
using Godot;
using Otchlan.Core;
using Otchlan.Input;

namespace Otchlan.UI;

/// <summary>Tło menu: kafelki Otchłani, winieta i podskakujący bohaterowie.</summary>
public partial class MenuBackground : Control
{
    private Texture2D _floor;
    private readonly List<(Texture2D tex, float x, float phase)> _heroes = new();
    private float _t;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        UiTheme.Fill(this);
        const string floorPath = "res://art/tiles/otchlan_floor.svg";
        _floor = ResourceLoader.Exists(floorPath) ? GD.Load<Texture2D>(floorPath) : null;
        string[] keys = { "lowczyni", "rycerz", "mag", "berserker", "zlodziej", "alchemik", "upadly" };
        for (int i = 0; i < keys.Length; i++)
        {
            var p = $"res://art/characters/{keys[i]}.svg";
            if (ResourceLoader.Exists(p))
                _heroes.Add((GD.Load<Texture2D>(p), (i + 0.5f) / keys.Length, i * 0.9f));
        }
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color("140d1d"));
        if (_floor != null)
        {
            float ts = 160;
            float off = (_t * 12) % ts;
            for (float y = -ts; y < size.Y + ts; y += ts)
                for (float x = -ts; x < size.X + ts; x += ts)
                    DrawTextureRect(_floor, new Rect2(x + off, y + off * 0.5f, ts, ts), false, new Color(1, 1, 1, 0.55f));
        }
        // winieta
        for (int i = 0; i < 12; i++)
        {
            float f = i / 12f;
            var c = new Color(0.04f, 0.02f, 0.07f, 0.09f);
            float m = f * 0.22f;
            DrawRect(new Rect2(0, 0, size.X, size.Y * m), c);
            DrawRect(new Rect2(0, size.Y * (1 - m), size.X, size.Y * m), c);
            DrawRect(new Rect2(0, 0, size.X * m * 0.6f, size.Y), c);
            DrawRect(new Rect2(size.X * (1 - m * 0.6f), 0, size.X * m * 0.6f, size.Y), c);
        }
        // bohaterowie
        float hs = Mathf.Min(150, size.X / (_heroes.Count + 1));
        foreach (var (tex, fx, ph) in _heroes)
        {
            float bob = Mathf.Abs(Mathf.Sin(_t * 3.1f + ph));
            float sq = 1 + 0.06f * Mathf.Sin(_t * 6.2f + ph * 2);
            var c = new Vector2(size.X * (0.08f + 0.84f * fx), size.Y - hs * 0.62f - bob * 18);
            var s = new Vector2(hs * (2 - sq), hs * sq);
            DrawSetTransform(Vector2.Zero);
            DrawTextureRect(tex, new Rect2(c - new Vector2(s.X / 2, s.Y), s), false);
        }
    }
}

/// <summary>Ekran tytułowy.</summary>
public partial class MainMenu : Control
{
    private Button _first;

    public override void _Ready()
    {
        AddChild(new MenuBackground());

        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        UiTheme.Fill(col);
        col.AddThemeConstantOverride("separation", 18);
        AddChild(col);

        var title = UiTheme.MakeLabel("OTCHŁAŃ", "TitleLabel", HorizontalAlignment.Center);
        col.AddChild(title);
        var sub = UiTheme.MakeLabel("Zejdź na samo dno. Jeśli zdołasz.", "DimLabel", HorizontalAlignment.Center);
        col.AddChild(sub);
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 26) });

        var buttons = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        buttons.AddThemeConstantOverride("separation", 16);
        col.AddChild(buttons);

        _first = AddButton(buttons, "Graj", () => View.RunScene.Start(new View.RunSetup()));
        AddButton(buttons, "Opcje", () => Game.I.ShowScreen(new OptionsMenu(() => Game.I.ShowScreen(new MainMenu()))));
        var ach = AddButton(buttons, "Osiągnięcia", null);
        ach.Disabled = true;
        AddButton(buttons, "Wyjdź", () => Game.I.Quit());

        var ver = UiTheme.MakeLabel($"v{ProjectSettings.GetSetting("application/config/version")}", "DimLabel");
        UiTheme.Place(ver, LayoutPreset.BottomRight, new Vector2(-230, -56), new Vector2(210, 40));
        ver.HorizontalAlignment = HorizontalAlignment.Right;
        AddChild(ver);

        var bar = InputPrompt.Bar(40, (Act.Accept, "Wybierz"), (Act.Cancel, "Wróć"));
        UiTheme.Place(bar, LayoutPreset.CenterBottom, new Vector2(-400, -76), new Vector2(800, 50));
        AddChild(bar);

        _first.CallDeferred(Control.MethodName.GrabFocus);
    }

    private static Button AddButton(Container parent, string text, Action onPress)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(420, 78) };
        if (onPress != null)
            b.Pressed += onPress;
        parent.AddChild(b);
        return b;
    }
}

/// <summary>
/// Przycisk-przełącznik opcji: „Nazwa: wartość”. Lewo/prawo (strzałki, D-pad, gałka) zmienia wartość,
/// zatwierdzenie przełącza na następną. Działa tak samo myszą, klawiaturą i padem.
/// </summary>
public partial class OptionRow : Button
{
    private readonly string _name;
    private readonly string[] _labels;
    private readonly Action<int> _onChange;
    public int Index { get; private set; }

    public OptionRow(string name, string[] labels, int index, Action<int> onChange)
    {
        _name = name;
        _labels = labels;
        _onChange = onChange;
        Index = Math.Clamp(index, 0, labels.Length - 1);
        Alignment = HorizontalAlignment.Left;
        CustomMinimumSize = new Vector2(900, 52);
        AddThemeFontSizeOverride("font_size", 26);
        Pressed += () => Step(1);
        UpdateText();
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e.IsActionPressed("ui_left", true))
        {
            Step(-1);
            AcceptEvent();
        }
        else if (e.IsActionPressed("ui_right", true))
        {
            Step(1);
            AcceptEvent();
        }
    }

    private void Step(int d)
    {
        Index = (Index + d + _labels.Length) % _labels.Length;
        UpdateText();
        _onChange(Index);
    }

    private void UpdateText() => Text = $"{_name}:   ‹ {_labels[Index]} ›";
}

/// <summary>Opcje: ekran, dźwięk, rozgrywka. Zmiany działają od razu i są zapisywane.</summary>
public partial class OptionsMenu : Control
{
    private readonly Action _back;

    public OptionsMenu(Action back) => _back = back;

    public override void _Ready()
    {
        AddChild(new MenuBackground());
        var s = Game.I.Settings;

        var center = new CenterContainer();
        UiTheme.Fill(center);
        center.OffsetBottom = -80;
        AddChild(center);
        var panel = new PanelContainer();
        center.AddChild(panel);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 6);
        panel.AddChild(col);
        var header = UiTheme.MakeLabel("Opcje", "HeaderLabel", HorizontalAlignment.Center);
        header.AddThemeFontSizeOverride("font_size", 48);
        col.AddChild(header);

        string[] onOff = { "Wył.", "Wł." };
        string[] pct = { "0%", "10%", "20%", "30%", "40%", "50%", "60%", "70%", "80%", "90%", "100%" };
        var rows = new List<Control>
        {
            Section("Ekran"),
            new OptionRow("Tryb okna", new[] { "Okno", "Pełny ekran", "Pełny ekran (wyłączny)" }, (int)s.WindowMode,
                i => { s.WindowMode = (WindowModeSetting)i; Changed(); }),
            new OptionRow("Synchronizacja pionowa (V-Sync)", onOff, s.VSync ? 1 : 0, i => { s.VSync = i == 1; Changed(); }),
            new OptionRow("Limit klatek", Array.ConvertAll(Settings.FpsLimits, Settings.FpsLabel),
                Array.IndexOf(Settings.FpsLimits, s.MaxFps), i => { s.MaxFps = Settings.FpsLimits[i]; Changed(); }),
            new OptionRow("Licznik FPS", onOff, s.ShowFps ? 1 : 0, i => { s.ShowFps = i == 1; Changed(); }),
            new OptionRow("Wstrząsy ekranu", new[] { "0%", "25%", "50%", "75%", "100%" }, (int)Mathf.Round(s.ScreenShake * 4),
                i => { s.ScreenShake = i / 4f; Changed(); }),
            Section("Dźwięk"),
            new OptionRow("Muzyka", pct, (int)Mathf.Round(s.MusicVolume * 10), i => { s.MusicVolume = i / 10f; Changed(); }),
            new OptionRow("Efekty", pct, (int)Mathf.Round(s.SfxVolume * 10), i => { s.SfxVolume = i / 10f; Changed(); }),
            Section("Rozgrywka"),
            new OptionRow("Auto-celowanie (gracz 1)", onOff, s.AutoAimP1 ? 1 : 0, i => { s.AutoAimP1 = i == 1; Changed(); }),
            new OptionRow("Liczby obrażeń", onOff, s.DamageNumbers ? 1 : 0, i => { s.DamageNumbers = i == 1; Changed(); }),
        };
        foreach (var r in rows)
            col.AddChild(r);
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        var back = new Button { Text = "Powrót", CustomMinimumSize = new Vector2(300, 60), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        back.Pressed += _back;
        col.AddChild(back);

        var bar = InputPrompt.Bar(40, (Act.Accept, "Zmień"), ("ui_left", "‹ ›"), (Act.Cancel, "Wróć"));
        UiTheme.Place(bar, LayoutPreset.CenterBottom, new Vector2(-450, -70), new Vector2(900, 50));
        AddChild(bar);

        foreach (var r in rows)
            if (r is OptionRow o)
            {
                o.CallDeferred(Control.MethodName.GrabFocus);
                break;
            }
    }

    private static Label Section(string text)
    {
        var l = UiTheme.MakeLabel(text, "SmallLabel");
        l.AddThemeColorOverride("font_color", UiTheme.Accent);
        return l;
    }

    private static void Changed() => Game.I.OnSettingsChanged();

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed(Act.Cancel))
        {
            GetViewport().SetInputAsHandled();
            _back();
        }
    }
}
