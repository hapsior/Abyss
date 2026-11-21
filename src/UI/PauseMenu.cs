using System;
using System.Linq;
using Godot;
using Otchlan.Core;
using Otchlan.Input;
using Otchlan.Sim;
using Otchlan.View;

namespace Otchlan.UI;

/// <summary>Pauza: Wznów, Opcje (w nakładce), Statystyki (panel na gracza), Zapisz i wyjdź, Porzuć wyprawę.</summary>
public partial class PauseMenu : Control
{
    private enum Mode { Main, Options, Stats }

    private readonly RunScene _scene;
    private Control _holder;
    private Mode _mode;

    public PauseMenu(RunScene s) => _scene = s;

    public override void _Ready()
    {
        AddChild(Ui.Dim(0.78f));
        _holder = new Control { MouseFilter = MouseFilterEnum.Ignore };
        UiTheme.Fill(_holder);
        AddChild(_holder);
        ShowMain();
    }

    private void Clear()
    {
        foreach (var c in _holder.GetChildren())
        {
            _holder.RemoveChild(c);
            c.QueueFree();
        }
    }

    private VBoxContainer Column(string title, int sep = 16)
    {
        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        UiTheme.Fill(col);
        col.OffsetTop = 110; // HUD zajmuje górne 96 px
        col.OffsetBottom = -20;
        col.AddThemeConstantOverride("separation", sep);
        _holder.AddChild(col);
        var t = UiTheme.MakeLabel(title, "HeaderLabel", HorizontalAlignment.Center);
        t.AddThemeFontSizeOverride("font_size", 64);
        col.AddChild(t);
        return col;
    }

    private void ShowMain()
    {
        _mode = Mode.Main;
        Clear();
        var col = Column("Pauza");
        var sim = _scene.Sim;
        col.AddChild(Ui.Lbl($"{Ui.DepthText(sim.Depth)} · {sim.Floor?.Biome.Name} · {Ui.TimeText(sim.RunTime)}", 26, UiTheme.TextDim, HorizontalAlignment.Center));
        col.AddChild(Ui.Spacer(8));
        var resume = AddBtn(col, "Wznów", _scene.CloseOverlay);
        AddBtn(col, "Opcje", ShowOptions);
        AddBtn(col, "Statystyki", ShowStats);
        AddBtn(col, "Zapisz i wyjdź", _scene.SaveAndQuit);
        AddBtn(col, "Porzuć wyprawę", () =>
            ConfirmDialog.Open(this, "Porzucić wyprawę? Zapis z tego slotu zostanie skasowany.", "Porzuć", _scene.Abandon));
        col.AddChild(Ui.Spacer(10));
        var bar = InputPrompt.Bar(40, (Act.Accept, "Wybierz"), (Act.Pause, "Wznów"));
        bar.CustomMinimumSize = new Vector2(0, 52);
        col.AddChild(bar);
        resume.CallDeferred(Control.MethodName.GrabFocus);
    }

    private static Button AddBtn(Container parent, string text, Action on)
    {
        var b = Ui.Btn(text, 460, 74, on, 34);
        b.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        parent.AddChild(b);
        return b;
    }

    private void ShowOptions()
    {
        _mode = Mode.Options;
        Clear();
        var o = new OptionsMenu(ShowMain, inGame: true);
        UiTheme.Fill(o);
        _holder.AddChild(o);
    }

    private void ShowStats()
    {
        _mode = Mode.Stats;
        Clear();
        var col = Column("Statystyki", 12);
        var sim = _scene.Sim;
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, SizeFlagsVertical = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 28);
        col.AddChild(row);
        foreach (var p in sim.Players)
            row.AddChild(PlayerPanel(p, sim.Coop));
        var back = Ui.Btn("Wróć", 320, 62, ShowMain);
        back.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        col.AddChild(back);
        var bar = InputPrompt.Bar(40, (Act.Accept, "Wybierz"), (Act.Cancel, "Wróć"));
        bar.CustomMinimumSize = new Vector2(0, 52);
        col.AddChild(bar);
        back.CallDeferred(Control.MethodName.GrabFocus);
    }

    private static Control PlayerPanel(PlayerSim p, bool coop)
    {
        var color = coop ? Ui.PlayerColor(p.Idx) : UiTheme.Accent;
        var card = Ui.Card(color.Darkened(0.2f), UiTheme.PanelBg2);
        card.CustomMinimumSize = new Vector2(coop ? 780 : 940, 0);
        card.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        card.AddChild(col);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 14);
        head.AddChild(Ui.Portrait(p.Key, 84));
        var names = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        names.AddThemeConstantOverride("separation", 0);
        names.AddChild(Ui.Lbl(coop ? $"{PlayerSim.Tags[p.Idx].tag} · {p.Def.Name}" : p.Def.Name, 40, color, title: true));
        names.AddChild(Ui.Lbl($"Poziom {p.Level} · {p.Def.Title}", 22, UiTheme.TextDim));
        head.AddChild(names);
        col.AddChild(head);

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 36);
        grid.AddThemeConstantOverride("v_separation", 2);
        foreach (var l in Ui.Lines(p))
        {
            var r = Ui.StatRow(l, 23);
            r.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            grid.AddChild(r);
        }
        col.AddChild(grid);

        col.AddChild(Ui.Lbl($"Przedmioty ({p.Items.Count})", 26, UiTheme.Accent));
        var info = Ui.Lbl("Najedź lub zaznacz przedmiot, aby zobaczyć opis.", 22, UiTheme.TextDim, wrap: true);
        info.CustomMinimumSize = new Vector2(0, 62);
        col.AddChild(ItemIcon.Strip(p.Items, 58, coop ? 21 : 30, d =>
        {
            info.Text = $"{d.Name}: {d.Desc}";
            info.AddThemeColorOverride("font_color", C.RarityColors[Math.Clamp(d.Rarity, 0, 3)]);
        }));
        col.AddChild(info);
        return card;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!e.IsActionPressed(Act.Pause) && !e.IsActionPressed(Act.Cancel))
            return;
        GetViewport().SetInputAsHandled();
        if (_mode == Mode.Main)
            _scene.CloseOverlay();
        else
            ShowMain();
    }
}
