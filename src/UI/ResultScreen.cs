using System;
using System.Linq;
using Godot;
using Otchlan.Core;
using Otchlan.Input;
using Otchlan.Sim;
using Otchlan.View;

namespace Otchlan.UI;

/// <summary>Ekran końca wyprawy: wyniki, przedmioty i poziomy graczy, „Menu główne” / „Nowa wyprawa”.</summary>
public partial class ResultScreen : Control
{
    private readonly RunScene _scene;

    public ResultScreen(RunScene s) => _scene = s;

    public override void _Ready()
    {
        var g = _scene.Sim;
        AddChild(Ui.Dim(0.88f));
        var root = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        UiTheme.Fill(root);
        root.OffsetTop = 24;
        root.OffsetBottom = -20;
        root.AddThemeConstantOverride("separation", 14);
        AddChild(root);

        var head = UiTheme.MakeLabel("Koniec wyprawy", "HeaderLabel", HorizontalAlignment.Center);
        head.AddThemeFontSizeOverride("font_size", 72);
        root.AddChild(head);
        root.AddChild(Ui.Lbl(Ui.DepthText(g.Depth), 54, UiTheme.Accent, HorizontalAlignment.Center, title: true));

        // kafelki z wynikami
        var tiles = new GridContainer { Columns = 4, SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        tiles.AddThemeConstantOverride("h_separation", 16);
        tiles.AddThemeConstantOverride("v_separation", 12);
        root.AddChild(tiles);
        var diffColor = new Color(g.Diff.Color);
        Tile(tiles, null, "Kraina", g.Floor?.Biome.Name ?? "-", UiTheme.Text);
        Tile(tiles, null, "Trudność", g.Diff.Name, diffColor);
        Tile(tiles, null, "Pieczęcie", g.Seal > 0 ? $"{g.Seal}" : "brak", UiTheme.Text);
        Tile(tiles, "clock", "Czas", Ui.TimeText(g.RunTime), UiTheme.Text);
        Tile(tiles, "skull", "Pokonani wrogowie", $"{g.Stats.Kills}", UiTheme.Text);
        Tile(tiles, "stat_dmg", "Zadane obrażenia", $"{g.Stats.DamageDealt:0}", UiTheme.Text);
        Tile(tiles, "star", "Bossowie", $"{g.Stats.Bosses}", UiTheme.Text);
        Tile(tiles, "coin", "Zebrane monety", $"{g.Stats.Coins}", UiTheme.Accent);

        // gracze
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 24);
        root.AddChild(row);
        foreach (var p in g.Players)
            row.AddChild(PlayerCard(p, g.Coop));

        var btns = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        btns.AddThemeConstantOverride("separation", 28);
        root.AddChild(btns);
        var menu = Ui.Btn("Menu główne", 400, 70, _scene.QuitToMenu, 34);
        var again = Ui.Btn("Nowa wyprawa", 400, 70, NewRun, 34);
        btns.AddChild(again);
        btns.AddChild(menu);
        var bar = InputPrompt.Bar(40, (Act.Accept, "Wybierz"));
        bar.CustomMinimumSize = new Vector2(0, 52);
        root.AddChild(bar);
        again.CallDeferred(Control.MethodName.GrabFocus);
    }

    private static void Tile(GridContainer parent, string icon, string label, string value, Color color)
    {
        var card = Ui.Card(null, UiTheme.PanelBg2, 16, 4);
        card.CustomMinimumSize = new Vector2(390, 0);
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 12);
        card.AddChild(h);
        var ic = icon != null ? Ui.Icon(Art.Ui(icon), 46) : Ui.Spacer(0);
        if (icon != null)
            h.AddChild(ic);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", -2);
        v.AddChild(Ui.Lbl(label, 20, UiTheme.TextDim));
        v.AddChild(Ui.Lbl(value, 32, color));
        h.AddChild(v);
        parent.AddChild(card);
    }

    private static Control PlayerCard(PlayerSim p, bool coop)
    {
        var color = coop ? Ui.PlayerColor(p.Idx) : UiTheme.Accent;
        var card = Ui.Card(color.Darkened(0.2f), UiTheme.PanelBg2);
        card.CustomMinimumSize = new Vector2(coop ? 780 : 1200, 0);
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 16);
        card.AddChild(h);
        h.AddChild(Ui.Portrait(p.Key, 110));
        var v = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 4);
        h.AddChild(v);
        v.AddChild(Ui.Lbl($"{(coop ? PlayerSim.Tags[p.Idx].tag + " · " : "")}{p.Def.Name} · poziom {p.Level}", 32, color));
        v.AddChild(ItemIcon.Strip(p.Items, 50, coop ? 12 : 22, null, false));
        return card;
    }

    private void NewRun()
    {
        var s = _scene.Setup;
        var setup = new RunSetup { Chars = s.Chars.ToList(), Difficulty = s.Difficulty, Seal = s.Seal, Slot = s.Slot };
        _scene.CloseOverlay();
        RunScene.Start(setup);
    }
}
