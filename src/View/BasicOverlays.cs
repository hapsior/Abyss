using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Otchlan.Core;
using Otchlan.Input;
using Otchlan.Sim;
using Otchlan.UI;

namespace Otchlan.View;

// Proste nakładki, żeby wyprawa była grywalna. Pełne wersje (karty awansu z odrzucaniem i losowaniem,
// menu sklepu, ekran wyniku ze statystykami) podmieniają fabryki w RunScene.

public partial class BasicLevelUp : Control
{
    private readonly RunScene _scene;
    private readonly PlayerSim _p;
    private List<Upgrade> _ups;

    public BasicLevelUp(RunScene s, PlayerSim p)
    {
        _scene = s;
        _p = p;
    }

    public override void _Ready()
    {
        var g = _scene.Sim;
        _ups = Items.RollUpgrades(g.R, _p.Luck, _p.Level, banned: _p.Banished);
        AddChild(new ColorRect { Color = new Color(0.04f, 0.02f, 0.07f, 0.72f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });
        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        UiTheme.Fill(col);
        AddChild(col);
        string who = g.Coop ? $"{PlayerSim.Tags[_p.Idx].tag} – " : "";
        col.AddChild(UiTheme.MakeLabel($"{who}AWANS! Poziom {_p.Level - _p.PendingLevelUps + 1}", "HeaderLabel", HorizontalAlignment.Center));
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 24);
        col.AddChild(row);
        Button first = null;
        for (int i = 0; i < _ups.Count; i++)
        {
            var up = _ups[i];
            var b = new Button { Text = $"{up.Name}\n{up.Desc}\n{C.RarityNames[up.Rarity]}", CustomMinimumSize = new Vector2(340, 200) };
            b.AddThemeColorOverride("font_color", C.RarityColors[up.Rarity]);
            b.AddThemeFontSizeOverride("font_size", 28);
            int idx = i;
            b.Pressed += () => Pick(idx);
            row.AddChild(b);
            first ??= b;
        }
        first?.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Pick(int i)
    {
        Items.ApplyUpgrade(_p, _ups[i]);
        _p.PendingLevelUps--;
        _scene.CloseOverlay();
    }
}

public partial class BasicPause : Control
{
    private readonly RunScene _scene;

    public BasicPause(RunScene s) => _scene = s;

    public override void _Ready()
    {
        AddChild(new ColorRect { Color = new Color(0.04f, 0.02f, 0.07f, 0.72f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });
        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        UiTheme.Fill(col);
        col.AddThemeConstantOverride("separation", 16);
        AddChild(col);
        col.AddChild(UiTheme.MakeLabel("Pauza", "HeaderLabel", HorizontalAlignment.Center));
        var resume = new Button { Text = "Wznów", CustomMinimumSize = new Vector2(420, 72), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        resume.Pressed += _scene.CloseOverlay;
        col.AddChild(resume);
        var quit = new Button { Text = "Zapisz i wyjdź", CustomMinimumSize = new Vector2(420, 72), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        quit.Pressed += _scene.SaveAndQuit;
        col.AddChild(quit);
        resume.CallDeferred(Control.MethodName.GrabFocus);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed(Act.Pause) || e.IsActionPressed(Act.Cancel))
        {
            GetViewport().SetInputAsHandled();
            _scene.CloseOverlay();
        }
    }
}

public partial class BasicResult : Control
{
    private readonly RunScene _scene;

    public BasicResult(RunScene s) => _scene = s;

    public override void _Ready()
    {
        var g = _scene.Sim;
        AddChild(new ColorRect { Color = new Color(0.04f, 0.02f, 0.07f, 0.85f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });
        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        UiTheme.Fill(col);
        col.AddThemeConstantOverride("separation", 14);
        AddChild(col);
        col.AddChild(UiTheme.MakeLabel("Koniec wyprawy", "HeaderLabel", HorizontalAlignment.Center));
        col.AddChild(UiTheme.MakeLabel($"Piętro {g.Depth} · zabici {g.Stats.Kills} · czas {(int)g.RunTime / 60:00}:{(int)g.RunTime % 60:00}", null, HorizontalAlignment.Center));
        var b = new Button { Text = "Menu główne", CustomMinimumSize = new Vector2(420, 72), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        b.Pressed += _scene.QuitToMenu;
        col.AddChild(b);
        b.CallDeferred(Control.MethodName.GrabFocus);
    }
}
