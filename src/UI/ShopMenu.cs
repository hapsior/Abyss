using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Otchlan.Core;
using Otchlan.Input;
using Otchlan.Sim;
using Otchlan.View;

namespace Otchlan.UI;

/// <summary>Menu sklepu: lista piedestałów sklepowych z bieżącego pokoju, cena, „Kup”, statystyki kupującego.</summary>
public partial class ShopMenu : Control
{
    private readonly RunScene _scene;
    private readonly PlayerSim _p;
    private VBoxContainer _list, _side;
    private Label _empty;
    private Button _close;
    private float _age;

    public ShopMenu(RunScene s, PlayerSim p)
    {
        _scene = s;
        _p = p;
    }

    private RunSim Sim => _scene.Sim;

    public override void _Ready()
    {
        AddChild(Ui.Dim(0.78f));
        var root = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        UiTheme.Fill(root);
        root.OffsetTop = 96;
        root.OffsetBottom = -24;
        root.AddThemeConstantOverride("separation", 14);
        AddChild(root);
        // ograniczenie szerokości na ultrawide
        var outer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(UiTheme.MakeLabel(Sim.Coop ? $"Sklep · {PlayerSim.Tags[_p.Idx].tag}" : "Sklep", "HeaderLabel", HorizontalAlignment.Center));
        ((Label)root.GetChild(0)).AddThemeFontSizeOverride("font_size", 56);
        if (Sim.Coop)
            ((Label)root.GetChild(0)).AddThemeColorOverride("font_color", Ui.PlayerColor(_p.Idx));
        root.AddChild(outer);

        var row = new HBoxContainer { CustomMinimumSize = new Vector2(1500, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 24);
        outer.AddChild(row);

        var listCard = Ui.Card();
        listCard.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        listCard.SizeFlagsStretchRatio = 2.2f;
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 12);
        var pad = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        pad.AddThemeConstantOverride("margin_left", 8);
        pad.AddThemeConstantOverride("margin_right", 8);
        pad.AddThemeConstantOverride("margin_top", 8);
        pad.AddThemeConstantOverride("margin_bottom", 8);
        pad.AddChild(_list);
        scroll.AddChild(pad);
        listCard.AddChild(scroll);
        row.AddChild(listCard);

        var sideCard = Ui.Card();
        sideCard.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        sideCard.CustomMinimumSize = new Vector2(440, 0);
        _side = new VBoxContainer();
        _side.AddThemeConstantOverride("separation", 6);
        sideCard.AddChild(_side);
        row.AddChild(sideCard);

        _close = Ui.Btn("Zamknij", 360, 66, _scene.CloseOverlay);
        _close.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        root.AddChild(_close);
        var bar = InputPrompt.Bar(40, (Act.Accept, "Kup"), (Act.Cancel, "Zamknij"));
        bar.CustomMinimumSize = new Vector2(0, 52);
        root.AddChild(bar);

        Rebuild(0);
    }

    public override void _Process(double delta) => _age += (float)delta;

    private List<Pedestal> Stock() =>
        Sim.Room.Pedestals.Where(pd => pd.Alive && (pd.Kind == PedestalKind.Shop || pd.Kind == PedestalKind.HeartShop)).ToList();

    private void Rebuild(int focus)
    {
        foreach (var c in _list.GetChildren())
        {
            _list.RemoveChild(c);
            c.QueueFree();
        }
        var stock = Stock();
        Button target = null;
        for (int i = 0; i < stock.Count; i++)
        {
            var b = MakeRow(stock[i]);
            _list.AddChild(b);
            if (i == Math.Clamp(focus, 0, stock.Count - 1))
                target = b;
        }
        if (stock.Count == 0)
            _list.AddChild(Ui.Lbl("Wyprzedane! Sklepikarz nie ma już nic na sprzedaż.", 32, UiTheme.TextDim, HorizontalAlignment.Center, wrap: true));
        (target ?? _close).CallDeferred(Control.MethodName.GrabFocus);
        RefreshSide();
    }

    private Button MakeRow(Pedestal pd)
    {
        bool heart = pd.Kind == PedestalKind.HeartShop;
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 18);
        Color color = heart ? new Color("ff6478") : C.RarityColors[Math.Clamp(pd.Item.Rarity, 0, 3)];
        var icon = Ui.Icon(heart ? Art.Pickup("serduszko") : Art.Item(pd.Item.Key), 84);
        h.AddChild(icon);
        var txt = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        txt.AddThemeConstantOverride("separation", 0);
        string name = heart ? "Leczenie" : $"{pd.Item.Name}  ·  {C.RarityNames[Math.Clamp(pd.Item.Rarity, 0, 3)]}";
        txt.AddChild(Ui.Lbl(name, 30, color));
        string desc = heart ? $"Przywraca {pd.Heal * 100:0}% zdrowia" : pd.Item.Desc;
        var dl = Ui.Lbl(desc, 21, UiTheme.TextDim, wrap: true);
        dl.MaxLinesVisible = 2;
        dl.CustomMinimumSize = new Vector2(0, 54);
        txt.AddChild(dl);
        h.AddChild(txt);
        bool afford = Sim.Coins >= pd.Price;
        var price = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        price.AddThemeConstantOverride("separation", 8);
        price.AddChild(Ui.Icon(Art.Ui("coin"), 40));
        price.AddChild(Ui.Lbl($"{pd.Price}", 38, afford ? UiTheme.Accent : UiTheme.Bad));
        var buy = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buy.AddChild(price);
        buy.AddChild(Ui.Lbl(afford ? "Kup" : "Za mało monet", 22, afford ? UiTheme.Good : UiTheme.Bad, HorizontalAlignment.Right));
        buy.CustomMinimumSize = new Vector2(190, 0);
        h.AddChild(buy);

        var normal = UiTheme.Box(UiTheme.PanelBg, 18, 5, 5, color.Darkened(0.35f));
        var b = MenuScreen.CardButton(new Vector2(0, 142), h, normal, UiTheme.Box(UiTheme.ButtonHover, 18, 5, 5, color));
        b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        b.Pressed += () => Buy(pd, b);
        return b;
    }

    private void Buy(Pedestal pd, Button b)
    {
        if (_age < 0.25f)
            return;
        int idx = b.GetIndex();
        if (Sim.UsePedestal(pd, _p))
            Rebuild(idx);
        else
            RefreshSide();
    }

    private void RefreshSide()
    {
        foreach (var c in _side.GetChildren())
        {
            _side.RemoveChild(c);
            c.QueueFree();
        }
        var coins = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        coins.AddThemeConstantOverride("separation", 10);
        coins.AddChild(Ui.Icon(Art.Ui("coin"), 56));
        coins.AddChild(Ui.Lbl($"{Sim.Coins}", 52, UiTheme.Accent));
        _side.AddChild(coins);
        _side.AddChild(Ui.Lbl("Twoje monety", 22, UiTheme.TextDim, HorizontalAlignment.Center));
        _side.AddChild(Ui.Spacer(8));
        var who = new HBoxContainer();
        who.AddThemeConstantOverride("separation", 10);
        who.AddChild(Ui.Portrait(_p.Key, 64));
        who.AddChild(Ui.Lbl($"{_p.Def.Name} · poz. {_p.Level}", 28, Sim.Coop ? Ui.PlayerColor(_p.Idx) : UiTheme.Text));
        _side.AddChild(who);
        foreach (var l in Ui.Lines(_p, false))
            _side.AddChild(Ui.StatRow(l, 24));
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed(Act.Cancel))
        {
            GetViewport().SetInputAsHandled();
            _scene.CloseOverlay();
        }
    }
}
