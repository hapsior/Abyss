using System;
using System.Linq;
using Godot;
using Otchlan.Core;
using Otchlan.Input;
using Otchlan.Save;
using Otchlan.Sim;
using Otchlan.View;

namespace Otchlan.UI;

/// <summary>Wybór trudności (4 karty), Pieczęci Otchłani (0..SealMax) i start wyprawy.</summary>
public partial class DiffSelect : MenuScreen
{
    private readonly NewRunFlow _flow;
    private int _diff, _seal;
    private Button[] _cards;
    private Button _sealBtn, _start;
    private VBoxContainer _sealList;

    public DiffSelect(NewRunFlow flow)
    {
        _flow = flow;
        _diff = Math.Clamp(Profile.P.LastDifficulty, 0, Content.Db.Difficulties.Count - 1);
        _seal = 0;
    }

    protected override string Header => "Trudność";
    protected override string Sub => string.Join(" + ", _flow.Chars.Select(k => Content.Db.CharByKey[k].Name));

    protected override void OnBack() =>
        Game.I.ShowScreen(new CharSelect(_flow, _flow.Coop ? 1 : 0));

    protected override (string, string)[] Prompts => new[] { (Act.Accept, "Wybierz"), ("ui_left", "‹ ›"), (Act.Cancel, "Wróć") };

    protected override void Build(VBoxContainer body)
    {
        var diffs = Content.Db.Difficulties;
        var row = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 22);
        body.AddChild(row);
        _cards = new Button[diffs.Count];
        for (int i = 0; i < diffs.Count; i++)
        {
            int idx = i;
            var d = diffs[i];
            var col = new VBoxContainer();
            col.AddThemeConstantOverride("separation", 10);
            var color = new Color(d.Color);
            col.AddChild(Ui.Lbl(d.Name, 40, color, HorizontalAlignment.Center, title: true));
            col.AddChild(Ui.Spacer(4));
            foreach (var line in d.Desc)
                col.AddChild(Ui.Lbl(line, 25, UiTheme.Text, HorizontalAlignment.Center, wrap: true));
            var b = CardButton(new Vector2(300, 250), col, UiTheme.Box(UiTheme.PanelBg2, 22, 6, 7, color.Darkened(0.4f)), UiTheme.Box(UiTheme.ButtonHover, 22, 6, 7, color));
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            b.FocusEntered += () => Select(idx);
            b.Pressed += () =>
            {
                Select(idx);
                (_sealBtn ?? _start).GrabFocus();
            };
            _cards[i] = b;
            row.AddChild(b);
        }

        // Pieczęcie Otchłani
        int max = Math.Min(Profile.P.SealMax, Content.Db.Seals.Count);
        var sealCard = Ui.Card();
        sealCard.SizeFlagsVertical = SizeFlags.ExpandFill;
        var sc = new VBoxContainer();
        sc.AddThemeConstantOverride("separation", 8);
        sealCard.AddChild(sc);
        if (max > 0)
        {
            _sealBtn = new SealRow(max, _seal, v =>
            {
                _seal = v;
                RefreshSeals();
            });
            sc.AddChild(_sealBtn);
        }
        else
        {
            sc.AddChild(Ui.Lbl("Pieczęcie Otchłani", 30, UiTheme.Accent));
            var r = new HBoxContainer();
            r.AddThemeConstantOverride("separation", 10);
            r.AddChild(Ui.Icon(Art.Ui("lock"), 36));
            r.AddChild(Ui.Lbl("Odblokujesz je po pokonaniu Serca Otchłani – każda dodaje wyzwanie.", 24, UiTheme.TextDim, wrap: true));
            sc.AddChild(r);
        }
        _sealList = new VBoxContainer();
        _sealList.AddThemeConstantOverride("separation", 0);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FocusMode = Control.FocusModeEnum.None };
        scroll.AddChild(_sealList);
        _sealList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (max > 0)
            sc.AddChild(scroll);
        body.AddChild(sealCard);
        sealCard.SizeFlagsStretchRatio = 1;

        _start = Ui.Btn("Rozpocznij wyprawę", 560, 76, Begin, 38);
        _start.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        body.AddChild(_start);

        RefreshSeals();
        Select(_diff);
        FocusTarget = _cards[_diff];
    }

    private void Select(int i)
    {
        _diff = i;
        var diffs = Content.Db.Difficulties;
        for (int k = 0; k < _cards.Length; k++)
        {
            bool on = k == i;
            var color = new Color(diffs[k].Color);
            _cards[k].AddThemeStyleboxOverride("normal", UiTheme.Box(on ? UiTheme.ButtonBg : UiTheme.PanelBg2, 22, on ? 9 : 6, 7, on ? color : color.Darkened(0.5f)));
            _cards[k].Modulate = on ? Colors.White : new Color(0.82f, 0.82f, 0.88f);
        }
    }

    private void RefreshSeals()
    {
        foreach (var c in _sealList.GetChildren())
            c.QueueFree();
        if (_seal == 0)
        {
            _sealList.AddChild(Ui.Lbl("Brak aktywnych pieczęci.", 22, UiTheme.TextDim));
            return;
        }
        var seals = Content.Db.Seals;
        for (int i = 0; i < _seal && i < seals.Count; i++)
        {
            var r = new HBoxContainer();
            r.AddThemeConstantOverride("separation", 12);
            r.AddChild(Ui.Lbl($"{i + 1}. {seals[i].Name}", 22, UiTheme.Accent));
            r.AddChild(Ui.Lbl(seals[i].Desc, 22, UiTheme.Text, wrap: true));
            _sealList.AddChild(r);
        }
    }

    private void Begin()
    {
        var prof = Profile.P;
        prof.LastDifficulty = _diff;
        prof.Save();
        RunScene.Start(new RunSetup { Chars = _flow.Chars.ToList(), Difficulty = _diff, Seal = _seal, Slot = _flow.Slot });
    }
}

/// <summary>Selektor 0..max Pieczęci Otchłani: lewo/prawo zmienia, zatwierdzenie przełącza dalej.</summary>
public partial class SealRow : Button
{
    private readonly int _max;
    private readonly Action<int> _on;
    public int Value { get; private set; }

    public SealRow(int max, int value, Action<int> on)
    {
        _max = max;
        _on = on;
        Value = Math.Clamp(value, 0, max);
        Alignment = HorizontalAlignment.Left;
        CustomMinimumSize = new Vector2(0, 58);
        AddThemeFontSizeOverride("font_size", 28);
        Pressed += () => Step(1);
        UpdateText();
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e.IsActionPressed("ui_left", true)) { Step(-1); AcceptEvent(); }
        else if (e.IsActionPressed("ui_right", true)) { Step(1); AcceptEvent(); }
    }

    private void Step(int d)
    {
        Value = (Value + d + _max + 1) % (_max + 1);
        UpdateText();
        _on(Value);
    }

    private void UpdateText() => Text = $"Pieczęcie Otchłani:   ‹ {Value} / {_max} ›";
}
