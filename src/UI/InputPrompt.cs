using Godot;
using Otchlan.Core;
using Otchlan.Input;

namespace Otchlan.UI;

/// <summary>
/// Ikona przycisku dla akcji: przy grze padem rysuje przycisk Xbox, przy klawiaturze klawisz z napisem
/// albo przycisk myszy. Sama przełącza się, gdy gracz zmieni urządzenie.
/// </summary>
public partial class GlyphIcon : Control
{
    public string Action { get; set; }
    public float IconHeight { get; set; } = 40;

    /// <summary>Wymuszone urządzenie (np. gracz 2 zawsze z padem). null = ostatnio używane.</summary>
    public InputDevice? ForceDevice { get; set; }

    private InputManager.Glyph _glyph;
    private Texture2D _tex;
    private static Texture2D _keyTex, _keyWideTex;

    public GlyphIcon() { }

    public GlyphIcon(string action, float height = 40)
    {
        Action = action;
        IconHeight = height;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        if (Game.I?.InputMgr != null)
            Game.I.InputMgr.DeviceChanged += OnDeviceChanged;
        Refresh();
    }

    public override void _ExitTree()
    {
        if (Game.I?.InputMgr != null)
            Game.I.InputMgr.DeviceChanged -= OnDeviceChanged;
    }

    private void OnDeviceChanged(InputDevice d) => Refresh();

    public void Refresh()
    {
        var device = ForceDevice ?? Game.I?.InputMgr?.LastDevice ?? InputDevice.KeyboardMouse;
        _glyph = InputManager.GlyphFor(Action, device);
        _tex = _glyph.Texture != null && ResourceLoader.Exists(_glyph.Texture) ? GD.Load<Texture2D>(_glyph.Texture) : null;
        float w;
        if (_glyph.IsKey)
        {
            var font = UiTheme.Bold;
            float textW = font?.GetStringSize(_glyph.KeyLabel, HorizontalAlignment.Left, -1, (int)(IconHeight * 0.5f)).X ?? 20;
            w = Mathf.Max(IconHeight, textW + IconHeight * 0.55f);
        }
        else if (_tex != null)
            w = IconHeight * _tex.GetWidth() / (float)_tex.GetHeight();
        else
            w = IconHeight;
        CustomMinimumSize = new Vector2(w, IconHeight);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = CustomMinimumSize;
        var rect = new Rect2(Vector2.Zero, size);
        if (_glyph.IsKey)
        {
            _keyTex ??= LoadOrNull("res://art/glyphs/key_blank.svg");
            _keyWideTex ??= LoadOrNull("res://art/glyphs/key_blank_wide.svg");
            var tex = size.X > size.Y * 1.3f ? _keyWideTex : _keyTex;
            if (tex != null)
                DrawTextureRect(tex, rect, false);
            else
            {
                DrawStyleBox(UiTheme.Box(new Color("e9e6f0"), radius: (int)(size.Y * 0.22f), border: 3, drop: 4), rect.Grow(-2));
            }
            var font = UiTheme.Bold;
            int fs = (int)(size.Y * 0.5f);
            var ts = font.GetStringSize(_glyph.KeyLabel, HorizontalAlignment.Left, -1, fs);
            var pos = new Vector2((size.X - ts.X) / 2f, size.Y * 0.47f + fs * 0.36f);
            DrawString(font, pos, _glyph.KeyLabel, HorizontalAlignment.Left, -1, fs, UiTheme.Ink);
        }
        else if (_tex != null)
            DrawTextureRect(_tex, rect, false);
        else
        {
            // zapas, gdy brak grafiki: kółko z literą
            DrawCircle(size / 2, size.Y * 0.48f, UiTheme.Ink);
            DrawCircle(size / 2, size.Y * 0.42f, new Color("3a3550"));
        }
    }

    private static Texture2D LoadOrNull(string path) => ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
}

/// <summary>Podpowiedź „[przycisk] opis”, np. „[E] Weź”. Przełącza glif razem z urządzeniem.</summary>
public partial class InputPrompt : HBoxContainer
{
    public GlyphIcon Icon { get; }
    public Label Text { get; }

    public InputPrompt(string action, string text, float height = 40, int fontSize = 28)
    {
        AddThemeConstantOverride("separation", 10);
        Alignment = AlignmentMode.Center;
        MouseFilter = MouseFilterEnum.Ignore;
        Icon = new GlyphIcon(action, height) { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        Text = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center };
        Text.AddThemeFontSizeOverride("font_size", fontSize);
        AddChild(Icon);
        AddChild(Text);
    }

    /// <summary>Pasek kilku podpowiedzi obok siebie.</summary>
    public static HBoxContainer Bar(float height, params (string action, string text)[] items)
    {
        var bar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        bar.AddThemeConstantOverride("separation", 44);
        foreach (var (a, t) in items)
            bar.AddChild(new InputPrompt(a, t, height));
        return bar;
    }
}

/// <summary>Licznik klatek w rogu ekranu (Opcje → Licznik FPS).</summary>
public partial class FpsOverlay : Label
{
    private double _t;

    public override void _Ready()
    {
        UiTheme.Place(this, LayoutPreset.TopRight, new Vector2(-170, 8), new Vector2(160, 40));
        HorizontalAlignment = HorizontalAlignment.Right;
        AddThemeColorOverride("font_color", new Color("7ee05a"));
        AddThemeColorOverride("font_outline_color", UiTheme.Ink);
        AddThemeConstantOverride("outline_size", 6);
        AddThemeFontSizeOverride("font_size", 24);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_t < 0.25)
            return;
        _t = 0;
        Text = $"FPS {Engine.GetFramesPerSecond():0}";
    }
}
