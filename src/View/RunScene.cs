using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Otchlan.Core;
using Otchlan.Input;
using Otchlan.Sim;
using Otchlan.UI;

namespace Otchlan.View;

/// <summary>Parametry nowej wyprawy (z menu wyboru postaci/trudności albo z zapisu).</summary>
public sealed class RunSetup
{
    public List<string> Chars = new() { "rycerz" };
    public int Difficulty = 1;
    public int Seal;
    public int Slot = 1;
    public ulong Seed = Rng.RandomSeed();
    /// <summary>Tryb deweloperski: start od razu w pokoju tego bossa (piętro jego biomu, bez zapisu).</summary>
    public string StartBoss;
}

/// <summary>
/// Scena wyprawy: trzyma symulację (<see cref="RunSim"/>), zbiera wejście graczy, aktualizuje warstwy widoku
/// i HUD, otwiera nakładki (awans, pauza, sklep, wynik). Świat jest skalowany ×1.5 (1280×720 → 1920×1080).
/// </summary>
public partial class RunScene : Node2D
{
    public RunSim Sim { get; private set; }
    public RunSetup Setup { get; private set; }
    public readonly List<InputSource> Sources = new();
    public bool AutoAimToggled;

    private Node2D _world;
    private Camera2D _cam;
    private RoomView _roomView;
    private EntityView _entities;
    private BulletView _bullets;
    private GroundFxView _ground;
    private TopFxView _top;
    public FxLayer Fx { get; private set; }
    public RunHud Hud { get; private set; }
    private PromptLayer _prompts;
    public Control Overlay { get; private set; }
    private float _gameOverT = -1;
    /// <summary>Logika osiągnięć tej wyprawy (odblokowania trafiają do profilu i na platformę).</summary>
    public Save.AchievementTracker Achievements { get; private set; }
    private readonly Random _shakeR = new();

    /// <summary>Fabryka nakładek – ekrany UI mogą ją podmienić na pełne wersje (awans, pauza, sklep, wynik).</summary>
    public static Func<RunScene, PlayerSim, Control> LevelUpFactory = (s, p) => new LevelUpOverlay(s, p);
    public static Func<RunScene, Control> PauseFactory = s => new PauseMenu(s);
    public static Func<RunScene, PlayerSim, Control> ShopFactory = (s, p) => new ShopMenu(s, p);
    public static Func<RunScene, Control> ResultFactory = s => new ResultScreen(s);

    public static RunScene Start(RunSetup setup, RunSim sim = null)
    {
        var scene = new RunScene { Setup = setup };
        scene.Sim = sim ?? new RunSim(setup.Chars, setup.Difficulty, setup.Seed, setup.Seal, startFloor: false);
        Game.I.SetWorld(scene);
        return scene;
    }

    public override void _Ready()
    {
        var g = Game.I;
        Fx = new FxLayer { Name = "Fx", ZIndex = 8, ShakeSetting = g.Settings.ScreenShake };
        Sim.Fx = Fx;
        Sim.DamageNumbers = g.Settings.DamageNumbers;

        _cam = new Camera2D { Position = new Vector2(C.W / 2, C.H / 2) * C.ViewScale, Enabled = true };
        AddChild(_cam);
        _world = new Node2D { Name = "World", Scale = new Vector2(C.ViewScale, C.ViewScale) };
        AddChild(_world);
        _roomView = new RoomView { Name = "Room" };
        _ground = new GroundFxView { Name = "Ground" };
        _entities = new EntityView { Name = "Entities" };
        _bullets = new BulletView { Name = "Bullets" };
        _top = new TopFxView { Name = "Top" };
        _prompts = new PromptLayer { Name = "Prompts", ZIndex = 20 };
        _world.AddChild(_roomView);
        _world.AddChild(_ground);
        _world.AddChild(_entities);
        _world.AddChild(_bullets);
        _world.AddChild(_top);
        _world.AddChild(Fx);
        _world.AddChild(_prompts);
        _roomView.Bind(Sim, _entities);
        _ground.Bind(Sim);
        _entities.Bind(Sim);
        _bullets.Bind(Sim);
        _top.Bind(Sim);
        _prompts.Bind(this);

        Hud = new RunHud(this);
        g.ShowScreen(Hud);
        Fx.OnBanner = Hud.PushBanner;
        Fx.OnSound = (n, v, p) => Audio.Sfx.Play(n, v, p);
        AssignInputs();
        Achievements = new Save.AchievementTracker(Sim, Save.Profile.P);
        if (Sim.Floor == null)
        {
            if (Setup.StartBoss != null)
                JumpToBoss(Setup.StartBoss);
            else
                Sim.NewFloor(1);
        }
    }

    /// <summary>Piętro z bossem o danym kluczu (pierwsze wystąpienie w cyklu biomów).</summary>
    public static int DepthForBoss(string key)
    {
        var biomes = global::Otchlan.Sim.Content.Db.Biomes;
        for (int i = 0; i < biomes.Count; i++)
            if (biomes[i].Boss == key) return (i + 1) * C.FloorsPerBiome;
        return C.FloorsPerBiome;
    }

    /// <summary>Tryb deweloperski: nowe piętro bossa i od razu wejście do jego pokoju.</summary>
    public void JumpToBoss(string key)
    {
        Sim.NewFloor(DepthForBoss(key));
        var room = Sim.Floor.Rooms.Values.First(r => r.Kind == RoomKind.Boss);
        Sim.EnterRoom(room, null);
    }

    /// <summary>Przypisanie urządzeń: solo – klawiatura+mysz i pad; co-op – zależnie od liczby padów.</summary>
    public void AssignInputs()
    {
        Sources.Clear();
        var pads = InputManager.Joypads;
        if (!Sim.Coop)
        {
            Sources.Add(new SoloSource());
            return;
        }
        if (pads.Count >= 2)
        {
            Sources.Add(new GamepadSource(pads[0]));
            Sources.Add(new GamepadSource(pads[1]) { AutoAimOn = Game.I.Settings.AutoAimP2 });
        }
        else if (pads.Count == 1)
        {
            Sources.Add(new KeyboardMouseSource { AutoAimOn = Game.I.Settings.AutoAimP1 });
            Sources.Add(new GamepadSource(pads[0]) { AutoAimOn = Game.I.Settings.AutoAimP2 });
        }
        else
        {
            Sources.Add(new KeyboardMouseSource { AutoAimOn = Game.I.Settings.AutoAimP1 });
            Sources.Add(new KeyboardRightSource());
        }
    }

    /// <summary>Pozycja myszy w jednostkach symulacji.</summary>
    public Vector2 MouseWorld() => _world.ToLocal(GetGlobalMousePosition());

    /// <summary>Pozycja w jednostkach symulacji → pozycja na ekranie (dla UI).</summary>
    public Vector2 WorldToScreen(Vector2 p) => GetViewport().GetCanvasTransform() * _world.ToGlobal(p);

    public void OpenOverlay(Control overlay)
    {
        CloseOverlay();
        Overlay = overlay;
        overlay.Theme = Game.I.Theme;
        UiTheme.Fill(overlay);
        Game.I.ScreenLayer.AddChild(overlay);
    }

    public void CloseOverlay()
    {
        if (Overlay != null && IsInstanceValid(Overlay))
            Overlay.QueueFree();
        Overlay = null;
    }

    /// <summary>Zapis wyprawy do slotu (nie zapisuje po śmierci).</summary>
    public void SaveNow()
    {
        if (Sim.State == RunState.Dying || Sim.Players.All(p => p.Dead) || Setup.Slot <= 0)
            return;
        Save.SaveSystem.Save(Setup.Slot, Sim);
    }

    /// <summary>Zapisz i wyjdź do menu.</summary>
    public void SaveAndQuit()
    {
        SaveNow();
        Save.Profile.P.Save();
        QuitToMenu();
    }

    /// <summary>Porzuć wyprawę (kasuje slot) i wyjdź.</summary>
    public void Abandon()
    {
        if (Setup.Slot > 0) Save.SaveSystem.Delete(Setup.Slot);
        Save.Profile.P.Add("abandoned");
        Save.Profile.P.Save();
        QuitToMenu();
    }

    public void QuitToMenu()
    {
        CloseOverlay();
        Game.I.SetWorld(null);
        Game.I.ShowScreen(new MainMenu());
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (Overlay != null) return;
        if (e.IsActionPressed(Act.Pause) && Sim.State != RunState.Dying)
        {
            GetViewport().SetInputAsHandled();
            OpenOverlay(PauseFactory(this));
        }
        else if (e.IsActionPressed(Act.AutoAim))
            AutoAimToggled = !AutoAimToggled;
    }

    public override void _Process(double delta)
    {
        float dt = MathF.Min((float)delta, 1 / 30f);
        Audio.Music.Play(Sim.State == RunState.Dying || Sim.Players.All(p => p.Dead) ? null : Sim.Boss != null ? "boss" : "explore");
        if (Overlay == null)
        {
            var inputs = new List<SimInput>(Sim.Players.Count);
            for (int i = 0; i < Sim.Players.Count; i++)
                inputs.Add(i < Sources.Count ? Sources[i].Read(this, Sim.Players[i]) : new SimInput());
            Sim.Update(dt, inputs);
            HandleEvents();
            Fx.Step(dt);
            UpdateAchievements();
        }
        _roomView.Step(dt);
        _entities.Step(dt);
        _bullets.QueueRedraw();
        _ground.QueueRedraw();
        _top.QueueRedraw();
        _prompts.Step();
        Hud.Step(dt);
        // wstrząs kamery
        float s = Fx.ShakeAmt;
        _cam.Offset = s > 0.1f ? new Vector2((float)_shakeR.NextDouble() * 2 - 1, (float)_shakeR.NextDouble() * 2 - 1) * s * C.ViewScale : Vector2.Zero;
        // nakładki czekające w kolejce
        if (Overlay == null)
        {
            if (_gameOverT >= 0)
            {
                _gameOverT -= dt;
                if (_gameOverT < 0)
                    OpenOverlay(ResultFactory(this));
            }
            else if (Sim.State == RunState.Play)
            {
                var p = Sim.Players.FirstOrDefault(pl => pl.PendingLevelUps > 0 && !pl.Dead);
                if (p != null)
                    OpenOverlay(LevelUpFactory(this, p));
            }
        }
    }

    private void HandleEvents()
    {
        while (Sim.Events.Count > 0)
        {
            var ev = Sim.Events.Dequeue();
            var prof = Save.Profile.P;
            switch (ev.Kind)
            {
                case RunEventKind.GameOver:
                    _gameOverT = 1.6f;
                    if (Setup.Slot > 0) Save.SaveSystem.Delete(Setup.Slot);
                    prof.Add("deaths");
                    prof.Add("runs");
                    prof.Add("kills", Sim.Stats.Kills);
                    prof.Save();
                    break;
                case RunEventKind.FloorEntered:
                    foreach (var c in prof.RecordDepth(Sim.Players.Select(p => p.Key), Sim.Depth))
                        Hud.PushBanner("Nowa postać!", Content.Db.CharByKey[c].Name, UiTheme.Accent, false);
                    prof.Save();
                    SaveNow();
                    break;
                case RunEventKind.StoryComplete:
                    foreach (var c in prof.RecordWin(Sim.Players.Select(p => p.Key), Sim.Seal))
                        Hud.PushBanner("Nowa postać!", Content.Db.CharByKey[c].Name, UiTheme.Accent, false);
                    prof.Save();
                    SaveNow();
                    break;
                case RunEventKind.RoomEntered:
                case RunEventKind.RoomCleared:
                case RunEventKind.BossDefeated:
                    SaveNow();
                    break;
                case RunEventKind.ShopOpened when ShopFactory != null:
                    OpenOverlay(ShopFactory(this, ev.Player));
                    break;
            }
            if (ev.Kind == RunEventKind.RoomEntered)
                Fx.Clear();
            Achievements?.OnEvent(ev);
            RunEvents?.Invoke(ev);
        }
    }

    /// <summary>Warunki osiągnięć oparte na stanie; nowe odblokowania → toast i zapis profilu.</summary>
    private void UpdateAchievements()
    {
        if (Achievements == null) return;
        Achievements.Step();
        var fresh = Achievements.TakeFresh();
        if (fresh.Count == 0) return;
        foreach (var def in fresh)
            Hud.PushAchievement(def);
        Save.Profile.P.Save();
    }

    /// <summary>Zdarzenia wyprawy dla innych systemów (osiągnięcia, zapis, statystyki).</summary>
    public static event Action<RunEvent> RunEvents;
}

/// <summary>Podpowiedzi „[E] Weź / Kup” nad piedestałami i sklepikarzem – osobno dla każdego gracza i jego urządzenia.</summary>
public partial class PromptLayer : Node2D
{
    private RunScene _scene;
    private readonly List<PedestalPrompt> _pool = new();

    public void Bind(RunScene s) => _scene = s;

    public void Step()
    {
        var g = _scene.Sim;
        int used = 0;
        for (int i = 0; i < g.Players.Count; i++)
        {
            var p = g.Players[i];
            if (!p.Active || _scene.Overlay != null || g.State != RunState.Play) continue;
            var pd = g.InteractTarget(p);
            bool shop = pd == null && g.NearShopkeeper(p);
            if (pd == null && !shop) continue;
            if (used >= _pool.Count)
            {
                var np = new PedestalPrompt();
                AddChild(np);
                _pool.Add(np);
            }
            var pr = _pool[used++];
            var dev = i < _scene.Sources.Count ? _scene.Sources[i].Device : InputDevice.KeyboardMouse;
            var action = i == 1 && _scene.Sources.Count > 1 && _scene.Sources[1] is KeyboardRightSource ? Act.P2Interact : Act.Interact;
            if (shop)
                pr.Show(RunSim.ShopkeeperPos + new Vector2(0, -60), action, dev, "Sklep", "Porozmawiaj ze sklepikarzem", UiTheme.Accent, null, g.Coop ? PlayerSim.Tags[i].tag : null);
            else
                pr.ShowFor(pd, g, p, action, dev, g.Coop ? PlayerSim.Tags[i].tag : null);
        }
        for (int i = used; i < _pool.Count; i++)
            _pool[i].Visible = false;
    }
}

/// <summary>Dymek z opisem przedmiotu i przyciskiem interakcji (w przestrzeni świata, przeskalowany do UI).</summary>
public partial class PedestalPrompt : Node2D
{
    private readonly PanelContainer _panel;
    private readonly Label _title, _desc;
    private readonly InputPrompt _prompt;
    private string _lastAction;
    private InputDevice _lastDevice;

    public PedestalPrompt()
    {
        Scale = Vector2.One / C.ViewScale;
        _panel = new PanelContainer { Theme = Game.I.Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        _panel.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color("2b2140ee"), 16, 4, 5));
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 2);
        _panel.AddChild(col);
        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", 26);
        _desc = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(320, 0) };
        _desc.AddThemeFontSizeOverride("font_size", 20);
        _desc.AddThemeColorOverride("font_color", UiTheme.TextDim);
        _prompt = new InputPrompt(Act.Interact, "Weź", 34, 24);
        col.AddChild(_title);
        col.AddChild(_desc);
        col.AddChild(_prompt);
        AddChild(_panel);
    }

    public void Show(Vector2 worldPos, string action, InputDevice dev, string title, string desc, Color color, string verb, string tag)
    {
        Visible = true;
        _title.Text = tag != null ? $"[{tag}] {title}" : title;
        _title.AddThemeColorOverride("font_color", color);
        _desc.Text = desc;
        _desc.Visible = !string.IsNullOrEmpty(desc);
        _prompt.Text.Text = verb ?? "Otwórz sklep";
        if (action != _lastAction || dev != _lastDevice)
        {
            _prompt.Icon.Action = action;
            _prompt.Icon.ForceDevice = dev;
            _prompt.Icon.Refresh();
            _lastAction = action;
            _lastDevice = dev;
        }
        _panel.ResetSize();
        var size = _panel.Size;
        Position = worldPos;
        _panel.Position = new Vector2(-size.X / 2, -size.Y);
    }

    public void ShowFor(Pedestal pd, RunSim g, PlayerSim p, string action, InputDevice dev, string tag)
    {
        string title, desc = "", verb;
        Color color = UiTheme.Text;
        switch (pd.Kind)
        {
            case PedestalKind.HeartShop:
                title = "Leczenie";
                desc = $"Przywraca {pd.Heal * 100:0}% zdrowia";
                verb = g.Coins >= pd.Price ? $"Kup za {pd.Price}" : $"Za mało monet ({pd.Price})";
                color = new Color("ff6478");
                break;
            default:
                title = pd.Item.Name;
                desc = pd.Item.Desc;
                color = C.RarityColors[pd.Item.Rarity];
                verb = pd.Kind switch
                {
                    PedestalKind.Shop => g.Coins >= pd.Price ? $"Kup za {pd.Price}" : $"Za mało monet ({pd.Price})",
                    PedestalKind.Shrine => $"Złóż ofiarę (-{pd.HpCost} maks. HP)",
                    _ => "Weź",
                };
                break;
        }
        Show(new Vector2(pd.X, pd.Y - 62), action, dev, title, desc, color, verb, tag);
    }
}
