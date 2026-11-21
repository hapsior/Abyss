using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Otchlan.Core;
using Otchlan.Input;
using Otchlan.Save;
using Otchlan.Sim;
using Otchlan.View;

namespace Otchlan.UI;

/// <summary>
/// Wybór bohatera: siatka kafelków (zawija się i przewija, dowolna liczba postaci) + panel szczegółów
/// fokusowanej postaci. W co-opie najpierw wybiera gracz 1, potem gracz 2.
/// </summary>
public partial class CharSelect : MenuScreen
{
    private const float TileW = 200, TileH = 250, Gap = 18;
    private readonly NewRunFlow _flow;
    private readonly int _player;
    private ScrollContainer _scroll;
    private GridContainer _grid;
    private VBoxContainer _detail;
    private readonly Dictionary<string, Button> _tiles = new();

    public CharSelect(NewRunFlow flow, int player)
    {
        _flow = flow;
        _player = player;
    }

    protected override string Header => _flow.Coop ? $"Gracz {_player + 1} – wybierz bohatera" : "Wybierz bohatera";
    protected override Color? HeaderColor => _flow.Coop ? Ui.PlayerColor(_player) : null;

    protected override void OnBack()
    {
        if (_flow.Coop && _player == 1)
            Game.I.ShowScreen(new CharSelect(_flow, 0));
        else
            Game.I.ShowScreen(new ModeSelect(_flow));
    }

    protected override void Build(VBoxContainer body)
    {
        var row = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 24);
        body.AddChild(row);

        _scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
        };
        _scroll.SizeFlagsStretchRatio = 1.5f;
        row.AddChild(_scroll);
        var holder = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            holder.AddThemeConstantOverride($"margin_{side}", 12);
        _scroll.AddChild(holder);
        _grid = new GridContainer { Columns = 4 };
        _grid.AddThemeConstantOverride("h_separation", (int)Gap);
        _grid.AddThemeConstantOverride("v_separation", (int)Gap);
        holder.AddChild(_grid);
        _scroll.Resized += () => _grid.Columns = Math.Max(2, (int)((_scroll.Size.X - 24 + Gap) / (TileW + Gap)));

        var card = Ui.Card();
        card.CustomMinimumSize = new Vector2(640, 0);
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.SizeFlagsStretchRatio = 1f;
        _detail = new VBoxContainer();
        _detail.AddThemeConstantOverride("separation", 8);
        card.AddChild(_detail);
        row.AddChild(card);

        var prof = Profile.P;
        string want = _player == 0 ? prof.LastChar : prof.LastChar2;
        Button focus = null;
        foreach (var def in Content.Db.Characters)
        {
            var b = MakeTile(def);
            _grid.AddChild(b);
            _tiles[def.Key] = b;
            if (def.Key == want && prof.IsUnlocked(def.Key))
                focus = b;
        }
        focus ??= _tiles.Values.FirstOrDefault();
        FocusTarget = focus;
        if (focus != null)
        {
            var def = Content.Db.Characters.First(c => _tiles[c.Key] == focus);
            ShowDetail(def);
        }
    }

    private Button MakeTile(CharacterDef def)
    {
        bool unlocked = Profile.P.IsUnlocked(def.Key);
        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        col.AddThemeConstantOverride("separation", 2);
        var holder = new Control { CustomMinimumSize = new Vector2(0, 150), SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        var por = Ui.Portrait(def.Key, 150, !unlocked);
        UiTheme.Fill(por);
        por.SizeFlagsVertical = SizeFlags.ExpandFill;
        holder.AddChild(por);
        if (!unlocked)
        {
            var lockIcon = Ui.Icon(Art.Ui("lock"), 64);
            UiTheme.Place(lockIcon, LayoutPreset.Center, new Vector2(-32, -32), new Vector2(64, 64));
            holder.AddChild(lockIcon);
        }
        col.AddChild(holder);
        col.AddChild(Ui.Lbl(def.Name, 28, unlocked ? UiTheme.Text : UiTheme.TextDim, HorizontalAlignment.Center));
        var bg = unlocked ? UiTheme.PanelBg2 : new Color("221a30");
        var hov = unlocked ? UiTheme.ButtonHover : new Color("2c2340");
        var b = CardButton(new Vector2(TileW, TileH), col, UiTheme.Box(bg, 20, 5, 6, unlocked ? new Color(def.Accent).Darkened(0.55f) : null), UiTheme.Box(hov, 20, 5, 6));
        b.FocusEntered += () => ShowDetail(def);
        b.Pressed += () => Pick(def);
        return b;
    }

    private void ShowDetail(CharacterDef def)
    {
        foreach (var c in _detail.GetChildren())
            c.QueueFree();
        var prof = Profile.P;
        bool unlocked = prof.IsUnlocked(def.Key);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 16);
        head.AddChild(Ui.Portrait(def.Key, 150, !unlocked));
        var names = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        names.AddChild(Ui.Lbl(def.Name, 46, def.BodyColor.Lightened(0.25f), title: true));
        names.AddChild(Ui.Lbl(def.Title, 26, UiTheme.TextDim));
        if (prof.BestDepthByChar.TryGetValue(def.Key, out var best))
            names.AddChild(Ui.Lbl($"Rekord: {Ui.DepthText(best)}", 22, UiTheme.Accent));
        head.AddChild(names);
        _detail.AddChild(head);

        if (!unlocked)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            row.AddChild(Ui.Icon(Art.Ui("lock"), 40));
            row.AddChild(Ui.Lbl(def.Unlock?.Text ?? "Zablokowana", 26, UiTheme.Accent, wrap: true));
            _detail.AddChild(row);
        }

        // kluczowe statystyki z bazowymi wartościami z danych (reszta z domyślnych wartości gracza)
        var tmp = new PlayerSim(def);
        var lines = Ui.Lines(tmp, false).Take(6).ToList();
        lines[0] = new Ui.StatLine("max_hp", "Zdrowie", $"{tmp.MaxHp:0}");
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 26);
        grid.AddThemeConstantOverride("v_separation", 2);
        foreach (var l in lines)
        {
            var r = Ui.StatRow(l, 24);
            r.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            grid.AddChild(r);
        }
        _detail.AddChild(grid);

        _detail.AddChild(Section("Pasywka"));
        _detail.AddChild(Ui.Lbl(def.Passive, 23, UiTheme.Text, wrap: true));
        _detail.AddChild(Section($"Umiejętność: {def.Ability}  ({def.AbilityCd:0.#} s)"));
        _detail.AddChild(Ui.Lbl(def.AbilityDesc, 23, UiTheme.Text, wrap: true));
    }

    private static Label Section(string text) => Ui.Lbl(text, 26, UiTheme.Accent);

    private void Pick(CharacterDef def)
    {
        var prof = Profile.P;
        if (!prof.IsUnlocked(def.Key))
        {
            SubLabel.Visible = true;
            SubLabel.Text = def.Unlock?.Text ?? "Ta postać jest zablokowana.";
            SubLabel.AddThemeColorOverride("font_color", UiTheme.Bad);
            return;
        }
        if (_player == 0) prof.LastChar = def.Key; else prof.LastChar2 = def.Key;
        prof.Save();
        _flow.Chars = _flow.Chars.Take(_player).Append(def.Key).ToList();
        if (_flow.Coop && _player == 0)
            Game.I.ShowScreen(new CharSelect(_flow, 1));
        else
            Game.I.ShowScreen(new DiffSelect(_flow));
    }
}
