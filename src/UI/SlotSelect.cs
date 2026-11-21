using System.Collections.Generic;
using System.Linq;
using Godot;
using Otchlan.Core;
using Otchlan.Input;
using Otchlan.Save;
using Otchlan.Sim;
using Otchlan.View;

namespace Otchlan.UI;

/// <summary>Stan kreatora nowej wyprawy przekazywany między ekranami: slot, tryb, wybrane postacie.</summary>
public sealed class NewRunFlow
{
    public int Slot = 1;
    public bool Coop;
    public List<string> Chars = new();
}

/// <summary>Wybór slotu zapisu: 3 karty. Zajęty slot: Kontynuuj / Usuń, pusty: nowa wyprawa.</summary>
public partial class SlotSelect : MenuScreen
{
    private HBoxContainer _row;
    private Label _error;

    protected override string Header => "Wybierz zapis";
    protected override string Sub => "Każdy slot to osobna wyprawa. Śmierć kasuje zapis.";
    protected override void OnBack() => Game.I.ShowScreen(new MainMenu());

    protected override void Build(VBoxContainer body)
    {
        _row = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _row.AddThemeConstantOverride("separation", 28);
        body.AddChild(_row);
        _error = Ui.Lbl("", 26, UiTheme.Bad, HorizontalAlignment.Center);
        body.AddChild(_error);
        Fill();
    }

    private void Fill(int focusSlot = 1)
    {
        foreach (var c in _row.GetChildren())
            c.QueueFree();
        Button focus = null;
        foreach (var info in SaveSystem.AllSlots())
        {
            var card = MakeCard(info, out var first);
            _row.AddChild(card);
            if (info.Slot == focusSlot || focus == null && info.Slot == 1)
                focus = first;
        }
        focus?.CallDeferred(Control.MethodName.GrabFocus);
        FocusTarget ??= focus;
    }

    private Control MakeCard(SlotInfo info, out Button first)
    {
        var card = Ui.Card();
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.SizeFlagsVertical = SizeFlags.ExpandFill;
        card.CustomMinimumSize = new Vector2(420, 0);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        card.AddChild(col);
        col.AddChild(Ui.Lbl($"Slot {info.Slot}", 30, UiTheme.Accent, HorizontalAlignment.Center, title: true));

        var portraits = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, SizeFlagsVertical = SizeFlags.ExpandFill };
        portraits.AddThemeConstantOverride("separation", 4);
        if (info.Empty)
        {
            var star = Ui.Icon(Art.Ui("star"), 140, new Color(1, 1, 1, 0.35f));
            star.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            portraits.AddChild(star);
        }
        else
        {
            float s = info.Chars.Count > 1 ? 190 : 250;
            foreach (var k in info.Chars)
            {
                var p = Ui.Portrait(k, s);
                p.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                portraits.AddChild(p);
            }
        }
        col.AddChild(portraits);

        col.AddChild(Ui.Lbl(info.Title, 36, info.Empty ? UiTheme.TextDim : UiTheme.Text, HorizontalAlignment.Center, wrap: true));
        var sub = Ui.Lbl(info.Subtitle, 22, UiTheme.TextDim, HorizontalAlignment.Center, wrap: true);
        sub.CustomMinimumSize = new Vector2(0, 84);
        col.AddChild(sub);

        if (info.Empty)
        {
            first = Ui.Btn("Nowa wyprawa", 300, 64, () => NewRun(info.Slot));
            first.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            col.AddChild(first);
            var pad = Ui.Spacer(64);
            col.AddChild(pad);
        }
        else
        {
            first = Ui.Btn("Kontynuuj", 300, 64, () => Continue(info.Slot));
            first.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            col.AddChild(first);
            var del = Ui.Btn("Usuń", 300, 64, () =>
                ConfirmDialog.Open(this, $"Usunąć zapis z slotu {info.Slot}? Tej operacji nie można cofnąć.", "Usuń", () =>
                {
                    SaveSystem.Delete(info.Slot);
                    Fill(info.Slot);
                }));
            del.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            col.AddChild(del);
        }
        return card;
    }

    private void NewRun(int slot) => Game.I.ShowScreen(new ModeSelect(new NewRunFlow { Slot = slot }));

    private void Continue(int slot)
    {
        var sim = SaveSystem.Load(slot);
        if (sim == null)
        {
            _error.Text = "Nie udało się wczytać zapisu.";
            return;
        }
        var setup = new RunSetup { Slot = slot, Chars = sim.Players.Select(p => p.Key).ToList(), Difficulty = sim.DiffIdx, Seal = sim.Seal };
        RunScene.Start(setup, sim);
    }
}

/// <summary>Solo albo co-op na jednym ekranie.</summary>
public partial class ModeSelect : MenuScreen
{
    private readonly NewRunFlow _flow;

    public ModeSelect(NewRunFlow flow) => _flow = flow;

    protected override string Header => "Tryb gry";
    protected override void OnBack() => Game.I.ShowScreen(new SlotSelect());

    protected override void Build(VBoxContainer body)
    {
        var center = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, SizeFlagsVertical = SizeFlags.ExpandFill };
        center.AddThemeConstantOverride("separation", 40);
        body.AddChild(center);
        var p = Save.Profile.P;
        var solo = ModeCard("Solo", "Jeden bohater, pełna kontrola. Klawiatura i mysz albo pad.", new[] { p.LastChar }, false);
        var coop = ModeCard("Co-op", "Na jednym ekranie: dwóch graczy, wspólna wyprawa. Można się nawzajem ratować.", new[] { p.LastChar, p.LastChar2 }, true);
        center.AddChild(solo);
        center.AddChild(coop);
        FocusTarget = _flow.Coop ? coop : solo;
    }

    private Button ModeCard(string name, string desc, string[] chars, bool coop)
    {
        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        col.AddThemeConstantOverride("separation", 8);
        var heroes = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, SizeFlagsVertical = SizeFlags.ExpandFill };
        heroes.AddThemeConstantOverride("separation", 0);
        foreach (var k in chars)
            heroes.AddChild(Ui.Portrait(k, coop ? 200 : 260));
        col.AddChild(heroes);
        col.AddChild(Ui.Lbl(name, 48, UiTheme.Accent, HorizontalAlignment.Center, title: true));
        col.AddChild(Ui.Lbl(desc, 26, UiTheme.Text, HorizontalAlignment.Center, wrap: true));
        var b = CardButton(new Vector2(560, 520), col, UiTheme.Box(UiTheme.PanelBg2, 26, 6, 8), UiTheme.Box(UiTheme.ButtonHover, 26, 6, 8));
        b.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        b.Pressed += () =>
        {
            _flow.Coop = coop;
            _flow.Chars = new List<string>();
            Game.I.ShowScreen(new CharSelect(_flow, 0));
        };
        return b;
    }
}
