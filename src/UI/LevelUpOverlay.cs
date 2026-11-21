using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Otchlan.Core;
using Otchlan.Input;
using Otchlan.Sim;
using Otchlan.View;

namespace Otchlan.UI;

/// <summary>
/// Awans: 3 duże karty ulepszeń (klawisze 1/2/3, klik, pad), losowanie ponowne za monety i odrzucanie
/// ulepszeń do końca wyprawy. W co-opie nagłówek pokazuje, który gracz wybiera.
/// </summary>
public partial class LevelUpOverlay : Control
{
    private readonly RunScene _scene;
    private readonly PlayerSim _p;
    private List<Upgrade> _ups;
    private int _rerolls, _level, _focusIdx;
    private float _age;
    private HBoxContainer _row;
    private Button _rerollBtn, _banishBtn;
    private Label _coins;
    private readonly List<Button> _cards = new();

    public LevelUpOverlay(RunScene s, PlayerSim p)
    {
        _scene = s;
        _p = p;
    }

    private RunSim Sim => _scene.Sim;
    public int RerollCost => 4 + 3 * _rerolls + Sim.Depth / 3;

    public override void _Ready()
    {
        _level = _p.Level - _p.PendingLevelUps + 1;
        _ups = Items.RollUpgrades(Sim.R, _p.Luck, _level, 3, _p.Banished);
        AddChild(Ui.Dim(0.78f));

        var root = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        UiTheme.Fill(root);
        root.OffsetTop = 96; // HUD zajmuje górne 96 px
        root.AddThemeConstantOverride("separation", 18);
        AddChild(root);

        string who = Sim.Coop ? $"{PlayerSim.Tags[_p.Idx].tag} · " : "";
        var head = UiTheme.MakeLabel($"{who}AWANS! Poziom {_level}", "HeaderLabel", HorizontalAlignment.Center);
        head.AddThemeFontSizeOverride("font_size", 64);
        if (Sim.Coop)
            head.AddThemeColorOverride("font_color", Ui.PlayerColor(_p.Idx));
        root.AddChild(head);
        root.AddChild(Ui.Lbl(Sim.Coop ? $"{_p.Def.Name} wybiera ulepszenie" : "Wybierz ulepszenie", 28, UiTheme.TextDim, HorizontalAlignment.Center));

        _row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _row.AddThemeConstantOverride("separation", 28);
        root.AddChild(_row);

        var tools = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        tools.AddThemeConstantOverride("separation", 28);
        root.AddChild(tools);
        _rerollBtn = Ui.Btn("", 500, 70, Reroll, 28);
        Ui.CoinIcon(_rerollBtn);
        tools.AddChild(_rerollBtn);
        _banishBtn = Ui.Btn("", 500, 70, Banish, 28);
        tools.AddChild(_banishBtn);

        _coins = Ui.Lbl("", 34, UiTheme.Accent, HorizontalAlignment.Center);
        root.AddChild(_coins);
        var bar = InputPrompt.Bar(40, (Act.Accept, "Wybierz"), ("ui_left", "Karta (1/2/3)"), (Act.Reroll, "Losuj"), (Act.Banish, "Odrzuć"));
        bar.CustomMinimumSize = new Vector2(0, 52);
        root.AddChild(bar);

        BuildCards(0);
        Refresh();
    }

    public override void _Process(double delta) => _age += (float)delta;

    private void BuildCards(int focus)
    {
        foreach (var c in _row.GetChildren())
            c.QueueFree();
        _cards.Clear();
        for (int i = 0; i < _ups.Count; i++)
        {
            var b = MakeCard(i);
            _row.AddChild(b);
            _cards.Add(b);
        }
        _focusIdx = Math.Clamp(focus, 0, Math.Max(0, _cards.Count - 1));
        if (_cards.Count > 0)
            _cards[_focusIdx].CallDeferred(Control.MethodName.GrabFocus);
        // nawigacja w poziomie między kartami i pionowo do przycisków
        for (int i = 0; i < _cards.Count; i++)
            _cards[i].FocusNeighborBottom = (i < 1 ? _rerollBtn : _banishBtn).GetPath();
    }

    private Button MakeCard(int i)
    {
        var up = _ups[i];
        var color = C.RarityColors[Math.Clamp(up.Rarity, 0, 3)];
        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        col.AddThemeConstantOverride("separation", 12);

        var top = new HBoxContainer();
        var num = Ui.Lbl($"{i + 1}", 34, UiTheme.Ink, HorizontalAlignment.Center);
        num.AddThemeConstantOverride("outline_size", 0);
        num.CustomMinimumSize = new Vector2(50, 50);
        num.VerticalAlignment = VerticalAlignment.Center;
        num.AddThemeStyleboxOverride("normal", UiTheme.Box(color, 25, 3, 0));
        top.AddChild(num);
        col.AddChild(top);

        var icon = Ui.Icon(Ui.StatTex(up.Def.Stat), 150);
        icon.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        col.AddChild(icon);
        col.AddChild(Ui.Lbl(up.Name, 44, color, HorizontalAlignment.Center, wrap: true, title: true));
        var desc = Ui.Lbl(up.Desc, 30, UiTheme.Text, HorizontalAlignment.Center, wrap: true);
        desc.SizeFlagsVertical = SizeFlags.ExpandFill;
        col.AddChild(desc);
        col.AddChild(Ui.Lbl(C.RarityNames[up.Rarity], 30, color, HorizontalAlignment.Center));

        var normal = UiTheme.Box(UiTheme.PanelBg2, 26, 9, 8, color);
        var hover = UiTheme.Box(UiTheme.ButtonHover, 26, 9, 8, color);
        var b = MenuScreen.CardButton(new Vector2(420, 540), col, normal, hover);
        int idx = i;
        b.FocusEntered += () =>
        {
            _focusIdx = idx;
            Refresh();
        };
        b.Pressed += () => Pick(idx);
        return b;
    }

    private void Refresh()
    {
        int cost = RerollCost;
        _rerollBtn.Text = $"Losuj ponownie ({cost})";
        _rerollBtn.Disabled = Sim.Coins < cost;
        _banishBtn.Text = $"Odrzuć wybraną ({_p.BanishesLeft})";
        _banishBtn.Disabled = _p.BanishesLeft <= 0;
        _coins.Text = $"Monety: {Sim.Coins}";
    }

    public void Pick(int i)
    {
        if (_age < 0.35f || i < 0 || i >= _ups.Count)
            return; // ochrona przed przypadkowym kliknięciem tuż po otwarciu
        Items.ApplyUpgrade(_p, _ups[i]);
        _p.PendingLevelUps--;
        _scene.CloseOverlay();
    }

    public void Reroll()
    {
        int cost = RerollCost;
        if (Sim.Coins < cost)
            return;
        Sim.Coins -= cost;
        _rerolls++;
        _ups = Items.RollUpgrades(Sim.R, _p.Luck, _level, 3, _p.Banished);
        BuildCards(_focusIdx);
        Refresh();
    }

    public void Banish()
    {
        if (_p.BanishesLeft <= 0 || _ups.Count == 0)
            return;
        int i = Math.Clamp(_focusIdx, 0, _ups.Count - 1);
        _p.Banished.Add(_ups[i].Key);
        _p.BanishesLeft--;
        var repl = Items.RollUpgrades(Sim.R, _p.Luck, _level, 1, _p.Banished, _ups.Select(u => u.Key).ToList());
        if (repl.Count > 0)
            _ups[i] = repl[0];
        else
            _ups.RemoveAt(i);
        BuildCards(i);
        Refresh();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            int n = k.Keycode switch { Key.Key1 or Key.Kp1 => 0, Key.Key2 or Key.Kp2 => 1, Key.Key3 or Key.Kp3 => 2, _ => -1 };
            if (n >= 0)
            {
                GetViewport().SetInputAsHandled();
                Pick(n);
                return;
            }
        }
        if (e.IsActionPressed(Act.Reroll))
        {
            GetViewport().SetInputAsHandled();
            Reroll();
        }
        else if (e.IsActionPressed(Act.Banish))
        {
            GetViewport().SetInputAsHandled();
            Banish();
        }
    }
}
