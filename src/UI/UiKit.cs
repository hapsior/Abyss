using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Otchlan.Core;
using Otchlan.Input;
using Otchlan.Sim;
using Otchlan.View;

namespace Otchlan.UI;

/// <summary>Wspólne klocki ekranów menu i nakładek: ikony statystyk, etykiety, przyciski, listy statystyk gracza.</summary>
public static class Ui
{
    /// <summary>Statystyka → ikona z art/ui.</summary>
    public static Texture2D StatTex(string stat)
    {
        string n = stat switch
        {
            "max_hp" => "stat_hp",
            "damage" or "dmg_mult" => "stat_dmg",
            "fire_rate" or "fire_mult" => "stat_fire_rate",
            "speed" => "stat_speed",
            "range" => "stat_range",
            "shot_speed" => "stat_shot_speed",
            "armor" => "stat_armor",
            "crit" => "stat_crit",
            "luck" => "stat_luck",
            "dodge" => "stat_dodge",
            "regen" => "stat_regen",
            "lifesteal" => "stat_lifesteal",
            "pickup" => "stat_pickup",
            "ability_cd_mult" => "stat_cooldown",
            _ => "star",
        };
        return Art.Ui(n);
    }

    public static TextureRect Icon(Texture2D tex, float size, Color? modulate = null)
    {
        var t = new TextureRect
        {
            Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(size, size),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        if (modulate.HasValue)
            t.Modulate = modulate.Value;
        return t;
    }

    /// <summary>Etykieta o zadanym rozmiarze (obrys skaluje się z rozmiarem, żeby małe napisy nie były „zalane”).</summary>
    public static Label Lbl(string text, int size = 26, Color? color = null, HorizontalAlignment align = HorizontalAlignment.Left,
        bool wrap = false, bool title = false)
    {
        var l = new Label { Text = text, HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeConstantOverride("outline_size", Math.Max(3, size / 5));
        if (title)
            l.AddThemeFontOverride("font", UiTheme.Title);
        if (color.HasValue)
            l.AddThemeColorOverride("font_color", color.Value);
        if (wrap)
        {
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }
        return l;
    }

    public static Button Btn(string text, float w = 340, float h = 64, Action onPress = null, int font = 30)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(w, h) };
        b.AddThemeFontSizeOverride("font_size", font);
        if (onPress != null)
            b.Pressed += onPress;
        return b;
    }

    /// <summary>Przycisk z ikoną monety (np. cena).</summary>
    public static void CoinIcon(Button b, int size = 34)
    {
        b.Icon = Art.Ui("coin");
        b.ExpandIcon = true;
        b.AddThemeConstantOverride("icon_max_width", size);
    }

    public static ColorRect Dim(float alpha = 0.72f) =>
        new() { Color = new Color(0.04f, 0.02f, 0.07f, alpha), MouseFilter = Control.MouseFilterEnum.Stop, AnchorRight = 1, AnchorBottom = 1 };

    /// <summary>Kolor gracza (P1 niebieski, P2 pomarańczowy).</summary>
    public static Color PlayerColor(int idx) => PlayerSim.Tags[Math.Clamp(idx, 0, 1)].color;

    public static CenterContainer Centered(Control child)
    {
        var c = new CenterContainer();
        UiTheme.Fill(c);
        c.AddChild(child);
        return c;
    }

    public static Control Spacer(float h) => new Control { CustomMinimumSize = new Vector2(0, h), MouseFilter = Control.MouseFilterEnum.Ignore };

    /// <summary>Panel-karta z paddingiem.</summary>
    public static PanelContainer Card(Color? border = null, Color? bg = null, int radius = 22, int border_w = 5)
    {
        var p = new PanelContainer { ThemeTypeVariation = "CardPanel" };
        if (border.HasValue || bg.HasValue)
            p.AddThemeStyleboxOverride("panel", UiTheme.Box(bg ?? UiTheme.PanelBg2, radius, border_w, 7, border));
        return p;
    }

    // ------------------------------------------------------------------ statystyki
    public readonly record struct StatLine(string Stat, string Label, string Value);

    /// <summary>Główne statystyki gracza (do pauzy, sklepu, wyniku).</summary>
    public static List<StatLine> Lines(PlayerSim p, bool full = true)
    {
        var l = new List<StatLine>
        {
            new("max_hp", "Zdrowie", $"{p.Hp:0}/{p.MaxHp:0}"),
            new("damage", "Obrażenia", $"{p.Damage * p.DmgMult:0.#}"),
            new("fire_rate", "Szybkostrzelność", $"{p.FireRate * p.FireMult:0.0}/s"),
            new("speed", "Szybkość", $"{p.Speed:0}"),
            new("range", "Zasięg", $"{p.Range:0}"),
            new("armor", "Pancerz", $"{p.Armor * 100:0}%"),
            new("crit", "Krytyk", $"{p.Crit * 100:0}%"),
            new("luck", "Szczęście", $"{p.Luck:0.#}"),
        };
        if (full)
        {
            l.Add(new("shot_speed", "Prędkość pocisku", $"{p.ShotSpeed:0}"));
            l.Add(new("regen", "Regeneracja", $"{p.Regen:0.0}/s"));
            l.Add(new("lifesteal", "Kradzież życia", $"{p.Lifesteal * 100:0}%"));
            l.Add(new("dodge", "Unik", $"{p.Dodge * 100:0}%"));
            l.Add(new("pickup", "Zasięg zbierania", $"{p.Pickup:0}"));
            l.Add(new("ability_cd_mult", "Odnowa umiejętności", $"{p.AbilityCdMult * 100:0}%"));
        }
        return l;
    }

    /// <summary>Wiersz statystyki: ikona, nazwa, wartość po prawej.</summary>
    public static Control StatRow(StatLine s, int size = 24)
    {
        var h = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        h.AddThemeConstantOverride("separation", 10);
        h.AddChild(Icon(StatTex(s.Stat), size + 8));
        var name = Lbl(s.Label, size, UiTheme.TextDim);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.ClipText = true;
        h.AddChild(name);
        h.AddChild(Lbl(s.Value, size, UiTheme.Text, HorizontalAlignment.Right));
        return h;
    }

    /// <summary>Prosta tekstura-zapas dla brakującej grafiki postaci: ciemna sylwetka.</summary>
    public static Control Portrait(string charKey, float size, bool dark = false)
    {
        // brak grafiki postaci (np. nowa postać w drodze) → ciemna sylwetka zamiast cudzego portretu
        bool missing = !ResourceLoader.Exists($"res://art/characters/{charKey}.svg");
        var tex = Art.Character(charKey);
        return Icon(tex, size, dark || missing ? new Color(0.12f, 0.1f, 0.16f, 1f) : null);
    }

    public static string DepthText(int depth) =>
        depth > C.StoryFloors ? $"Głębia {depth - C.StoryFloors}" : $"Piętro {depth}";

    public static string TimeText(float t) => $"{(int)t / 60:00}:{(int)t % 60:00}";
}

/// <summary>Ikona przedmiotu: dymek z nazwą i opisem (myszą), podgląd przy fokusie (padem), licznik stosu.</summary>
public partial class ItemIcon : Control
{
    private readonly ItemDef _def;
    private readonly int _count;
    private readonly float _size;
    public Action<ItemDef> Focused;

    public ItemIcon(ItemDef def, int count, float size)
    {
        _def = def;
        _count = count;
        _size = size;
        CustomMinimumSize = new Vector2(size, size);
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = $"{def.Name}\n{def.Desc}";
        FocusEntered += () => Focused?.Invoke(_def);
        MouseEntered += () => Focused?.Invoke(_def);
    }

    public override void _Draw()
    {
        var col = C.RarityColors[Math.Clamp(_def.Rarity, 0, 3)];
        var rect = new Rect2(Vector2.Zero, CustomMinimumSize);
        DrawStyleBox(UiTheme.Box(new Color("231a33"), 10, 3, 0, col), rect);
        var tex = Art.Item(_def.Key);
        if (tex != null)
            DrawTextureRect(tex, rect.Grow(-6), false);
        if (_count > 1)
        {
            var f = UiTheme.Bold;
            int fs = (int)(_size * 0.36f);
            var s = $"x{_count}";
            var pos = new Vector2(rect.End.X - f.GetStringSize(s, HorizontalAlignment.Left, -1, fs).X - 4, rect.End.Y - 5);
            DrawStringOutline(f, pos, s, HorizontalAlignment.Left, -1, fs, 8, UiTheme.Ink);
            DrawString(f, pos, s, HorizontalAlignment.Left, -1, fs, UiTheme.Accent);
        }
        if (HasFocus())
        {
            var fb = UiTheme.Box(new Color(0, 0, 0, 0), 10, 5, 0, UiTheme.Accent);
            fb.DrawCenter = false;
            DrawStyleBox(fb, rect.Grow(3));
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationFocusEnter || what == NotificationFocusExit)
            QueueRedraw();
    }

    public override Control _MakeCustomTooltip(string forText)
    {
        var p = new PanelContainer { Theme = Game.I.Theme };
        p.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.PanelBg, 14, 4, 4, C.RarityColors[Math.Clamp(_def.Rarity, 0, 3)]));
        var v = new VBoxContainer();
        v.AddChild(Ui.Lbl(_def.Name, 26, C.RarityColors[Math.Clamp(_def.Rarity, 0, 3)]));
        var d = Ui.Lbl(_def.Desc, 22, UiTheme.Text, wrap: true);
        d.CustomMinimumSize = new Vector2(320, 0);
        v.AddChild(d);
        p.AddChild(v);
        return p;
    }

    /// <summary>Grupuje przedmioty gracza (stosy) w kolejności pierwszego pojawienia się.</summary>
    public static List<(ItemDef def, int count)> Group(IEnumerable<string> keys)
    {
        var res = new List<(ItemDef, int)>();
        foreach (var g in keys.GroupBy(k => k))
            if (Content.Db.ItemByKey.TryGetValue(g.Key, out var d))
                res.Add((d, g.Count()));
        return res;
    }

    /// <summary>Wiersz ikon przedmiotów; zbyt wiele → „+N”. Zwraca kontener zawijany.</summary>
    public static Control Strip(IEnumerable<string> keys, float size, int maxIcons, Action<ItemDef> focused = null, bool focusable = true)
    {
        var flow = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        flow.AddThemeConstantOverride("h_separation", 8);
        flow.AddThemeConstantOverride("v_separation", 8);
        var groups = Group(keys);
        for (int i = 0; i < Math.Min(maxIcons, groups.Count); i++)
        {
            var ic = new ItemIcon(groups[i].def, groups[i].count, size) { Focused = focused };
            if (!focusable)
                ic.FocusMode = FocusModeEnum.None;
            flow.AddChild(ic);
        }
        if (groups.Count > maxIcons)
        {
            int more = groups.Skip(maxIcons).Sum(g => g.count);
            var l = Ui.Lbl($"+{more}", (int)(size * 0.55f), UiTheme.Accent);
            l.VerticalAlignment = VerticalAlignment.Center;
            l.CustomMinimumSize = new Vector2(size, size);
            flow.AddChild(l);
        }
        if (groups.Count == 0)
            flow.AddChild(Ui.Lbl("Brak przedmiotów", 22, UiTheme.TextDim));
        return flow;
    }
}

/// <summary>
/// Okno potwierdzenia (Tak/Nie) nakładane na ekran. Blokuje fokus kontrolek pod spodem,
/// Cancel = anuluj, po zamknięciu oddaje fokus.
/// </summary>
public partial class ConfirmDialog : Control
{
    private readonly Node _host;
    private readonly string _text, _yes;
    private readonly Action _onYes;
    private readonly List<(Control c, FocusModeEnum m)> _locked = new();
    private Control _prevFocus;

    private ConfirmDialog(Node host, string text, string yes, Action onYes)
    {
        _host = host;
        _text = text;
        _yes = yes;
        _onYes = onYes;
    }

    public static void Open(Control host, string text, string yes, Action onYes) =>
        host.AddChild(new ConfirmDialog(host, text, yes, onYes));

    public override void _Ready()
    {
        UiTheme.Fill(this);
        _prevFocus = GetViewport().GuiGetFocusOwner();
        foreach (var n in _host.FindChildren("*", "Control", true, false))
            if (n is Control c && c != this && !IsAncestorOf(c) && c.FocusMode != FocusModeEnum.None)
            {
                _locked.Add((c, c.FocusMode));
                c.FocusMode = FocusModeEnum.None;
            }
        AddChild(Ui.Dim(0.6f));
        var panel = new PanelContainer();
        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        col.AddThemeConstantOverride("separation", 20);
        panel.AddChild(col);
        var msg = Ui.Lbl(_text, 32, UiTheme.Text, HorizontalAlignment.Center, wrap: true);
        msg.CustomMinimumSize = new Vector2(700, 0);
        col.AddChild(msg);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 24);
        col.AddChild(row);
        var no = Ui.Btn("Anuluj", 280, 66, Close);
        var yes = Ui.Btn(_yes, 280, 66, () =>
        {
            Close();
            _onYes();
        });
        row.AddChild(yes);
        row.AddChild(no);
        AddChild(Ui.Centered(panel));
        // fokus zostaje wewnątrz okna
        yes.FocusNeighborLeft = yes.GetPath();
        no.FocusNeighborRight = no.GetPath();
        yes.FocusNeighborRight = no.GetPath();
        no.FocusNeighborLeft = yes.GetPath();
        foreach (var b in new[] { yes, no })
        {
            b.FocusNeighborTop = b.GetPath();
            b.FocusNeighborBottom = b.GetPath();
        }
        no.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void Close()
    {
        foreach (var (c, m) in _locked)
            if (IsInstanceValid(c))
                c.FocusMode = m;
        if (_prevFocus != null && IsInstanceValid(_prevFocus))
            _prevFocus.CallDeferred(Control.MethodName.GrabFocus);
        QueueFree();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed(Act.Cancel))
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }
}

/// <summary>
/// Podstawa pełnoekranowych ekranów menu: tło, nagłówek, treść, pasek podpowiedzi, Cancel = wstecz.
/// Treść ma ograniczoną szerokość (ładnie wygląda także w 21:9) i marginesy zależne od rozmiaru okna.
/// </summary>
public abstract partial class MenuScreen : Control
{
    protected const float MaxWidth = 1780;
    protected VBoxContainer Body;
    protected Label HeaderLabel, SubLabel;
    private MarginContainer _margin;
    protected Control FocusTarget;

    protected abstract string Header { get; }
    protected virtual string Sub => null;
    protected virtual Color? HeaderColor => null;
    protected virtual (string action, string text)[] Prompts => new[] { (Act.Accept, "Wybierz"), (Act.Cancel, "Wróć") };
    protected abstract void Build(VBoxContainer body);
    protected abstract void OnBack();
    /// <summary>Czy tło ma pochód bohaterów (ekrany z wysoką siatką go wyłączają).</summary>
    protected virtual bool Parade => true;

    public override void _Ready()
    {
        AddChild(new MenuBackground { Parade = Parade });
        _margin = new MarginContainer();
        UiTheme.Fill(_margin);
        AddChild(_margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 10);
        _margin.AddChild(root);

        HeaderLabel = UiTheme.MakeLabel(Header, "HeaderLabel", HorizontalAlignment.Center);
        HeaderLabel.AddThemeFontSizeOverride("font_size", 54);
        if (HeaderColor.HasValue)
            HeaderLabel.AddThemeColorOverride("font_color", HeaderColor.Value);
        root.AddChild(HeaderLabel);
        SubLabel = Ui.Lbl(Sub ?? "", 26, UiTheme.TextDim, HorizontalAlignment.Center);
        SubLabel.Visible = Sub != null;
        root.AddChild(SubLabel);

        Body = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        Body.AddThemeConstantOverride("separation", 16);
        root.AddChild(Body);
        Build(Body);

        var bar = InputPrompt.Bar(40, Prompts);
        bar.CustomMinimumSize = new Vector2(0, 52);
        root.AddChild(bar);

        Resized += UpdateMargins;
        UpdateMargins();
        FocusTarget?.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void UpdateMargins()
    {
        int side = (int)Mathf.Max(40, (Size.X - MaxWidth) / 2);
        _margin.AddThemeConstantOverride("margin_left", side);
        _margin.AddThemeConstantOverride("margin_right", side);
        _margin.AddThemeConstantOverride("margin_top", 28);
        _margin.AddThemeConstantOverride("margin_bottom", 22);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed(Act.Cancel))
        {
            GetViewport().SetInputAsHandled();
            OnBack();
        }
    }

    /// <summary>Przycisk-karta z dowolną zawartością (dzieci ignorują mysz); mysz najeżdżająca przenosi fokus.</summary>
    public static Button CardButton(Vector2 minSize, Control content, StyleBoxFlat normal = null, StyleBoxFlat hover = null)
    {
        var b = new Button { CustomMinimumSize = minSize, ClipContents = false };
        if (normal != null)
        {
            b.AddThemeStyleboxOverride("normal", normal);
            b.AddThemeStyleboxOverride("pressed", normal);
            b.AddThemeStyleboxOverride("hover", hover ?? normal);
            b.AddThemeStyleboxOverride("disabled", normal);
        }
        content.MouseFilter = MouseFilterEnum.Ignore;
        UiTheme.Fill(content);
        content.OffsetLeft = 16;
        content.OffsetRight = -16;
        content.OffsetTop = 12;
        content.OffsetBottom = -12;
        b.AddChild(content);
        b.MouseEntered += () =>
        {
            if (!b.Disabled && b.FocusMode != FocusModeEnum.None) b.GrabFocus();
        };
        return b;
    }
}
