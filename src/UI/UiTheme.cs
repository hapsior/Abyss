using Godot;

namespace Otchlan.UI;

/// <summary>Kreskówkowy motyw UI budowany w kodzie: grube ciemne obrysy, zaokrąglenia, „twardy” cień.</summary>
public static class UiTheme
{
    public static readonly Color Ink = new("1a0f1f");
    public static readonly Color PanelBg = new("2b2140");
    public static readonly Color PanelBg2 = new("3a2c57");
    public static readonly Color ButtonBg = new("4a3a6e");
    public static readonly Color ButtonHover = new("5d4a8a");
    public static readonly Color ButtonPressed = new("372a52");
    public static readonly Color Accent = new("ffd23f");
    public static readonly Color Text = new("fff6e8");
    public static readonly Color TextDim = new("b9aed0");
    public static readonly Color Good = new("7ee05a");
    public static readonly Color Bad = new("ff4d5e");

    public static FontFile Body { get; private set; }
    public static FontFile Bold { get; private set; }
    public static FontFile Title { get; private set; }

    public static StyleBoxFlat Box(Color bg, int radius = 18, int border = 5, int drop = 6, Color? borderColor = null)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = borderColor ?? Ink,
            CornerDetail = 10,
            AntiAliasing = true,
        };
        sb.SetBorderWidthAll(border);
        sb.SetCornerRadiusAll(radius);
        if (drop > 0)
        {
            sb.ShadowColor = Ink;
            sb.ShadowSize = 1;
            sb.ShadowOffset = new Vector2(0, drop);
        }
        sb.SetContentMarginAll(12);
        sb.ContentMarginLeft = sb.ContentMarginRight = 22;
        return sb;
    }

    private static FontFile LoadFont(string name) => GD.Load<FontFile>($"res://assets/fonts/{name}.ttf");

    public static Theme Build()
    {
        Body = LoadFont("Baloo2-Bold");
        Bold = LoadFont("Baloo2-ExtraBold");
        Title = LoadFont("TitanOne-Regular");

        var t = new Theme { DefaultFont = Bold, DefaultFontSize = 30 };

        // --- Label ---
        t.SetColor("font_color", "Label", Text);
        t.SetColor("font_outline_color", "Label", Ink);
        t.SetConstant("outline_size", "Label", 8);
        t.SetColor("font_shadow_color", "Label", new Color(Ink, 0.0f));

        Variation(t, "TitleLabel", "Label", Title, 150, 26, Text);
        Variation(t, "HeaderLabel", "Label", Title, 60, 14, Text);
        Variation(t, "DimLabel", "Label", Body, 24, 6, TextDim);
        Variation(t, "SmallLabel", "Label", Bold, 22, 6, Text);

        // --- Button ---
        t.SetStylebox("normal", "Button", Box(ButtonBg));
        t.SetStylebox("hover", "Button", Box(ButtonHover));
        t.SetStylebox("pressed", "Button", Box(ButtonPressed, drop: 2));
        t.SetStylebox("disabled", "Button", Box(new Color("2d2540"), drop: 3));
        var focus = Box(new Color(0, 0, 0, 0), border: 6, drop: 0, borderColor: Accent);
        focus.DrawCenter = false;
        focus.SetExpandMarginAll(4);
        t.SetStylebox("focus", "Button", focus);
        t.SetColor("font_color", "Button", Text);
        t.SetColor("font_hover_color", "Button", Text);
        t.SetColor("font_focus_color", "Button", Accent);
        t.SetColor("font_hover_pressed_color", "Button", Accent);
        t.SetColor("font_pressed_color", "Button", Accent);
        t.SetColor("font_disabled_color", "Button", new Color("776c8c"));
        t.SetColor("font_outline_color", "Button", Ink);
        t.SetConstant("outline_size", "Button", 7);
        t.SetFontSize("font_size", "Button", 34);

        // --- panele ---
        t.SetStylebox("panel", "PanelContainer", Box(PanelBg, radius: 28, border: 6, drop: 10));
        t.SetStylebox("panel", "Panel", Box(PanelBg, radius: 28, border: 6, drop: 10));
        t.SetTypeVariation("CardPanel", "PanelContainer");
        t.SetStylebox("panel", "CardPanel", Box(PanelBg2, radius: 22, border: 5, drop: 7));

        // --- RichTextLabel (podpowiedzi) ---
        t.SetColor("default_color", "RichTextLabel", Text);
        t.SetColor("font_outline_color", "RichTextLabel", Ink);
        t.SetConstant("outline_size", "RichTextLabel", 7);
        t.SetFont("normal_font", "RichTextLabel", Bold);
        t.SetFontSize("normal_font_size", "RichTextLabel", 28);
        return t;
    }

    private static void Variation(Theme t, string name, string baseType, Font font, int size, int outline, Color color)
    {
        t.SetTypeVariation(name, baseType);
        t.SetFont("font", name, font);
        t.SetFontSize("font_size", name, size);
        t.SetConstant("outline_size", name, outline);
        t.SetColor("font_color", name, color);
        t.SetColor("font_outline_color", name, Ink);
    }

    /// <summary>
    /// Kotwiczy kontrolkę w punkcie presetu (np. środek dołu) z przesunięciem względem tego punktu i stałym rozmiarem.
    /// Działa niezależnie od tego, czy rodzic ma już rozmiar (ważne przy proporcjach 21:9 / 4:3).
    /// </summary>
    public static T Place<T>(T c, Control.LayoutPreset preset, Vector2 offset, Vector2 size) where T : Control
    {
        c.SetAnchorsPreset(preset);
        c.OffsetLeft = offset.X;
        c.OffsetTop = offset.Y;
        c.OffsetRight = offset.X + size.X;
        c.OffsetBottom = offset.Y + size.Y;
        return c;
    }

    public static T Fill<T>(T c) where T : Control
    {
        c.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return c;
    }

    /// <summary>Etykieta z wariantem motywu.</summary>
    public static Label MakeLabel(string text, string variation = null, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { Text = text, HorizontalAlignment = align };
        if (variation != null)
            l.ThemeTypeVariation = variation;
        return l;
    }
}
