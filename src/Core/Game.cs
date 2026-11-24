using System;
using Godot;
using Otchlan.Input;
using Otchlan.UI;

namespace Otchlan.Core;

/// <summary>
/// Globalny punkt wejścia (autoload „Game”): ustawienia, wejście, motyw UI, przełączanie ekranów.
/// Ekrany UI trafiają do <see cref="ScreenLayer"/>, świat gry do <see cref="WorldRoot"/>.
/// </summary>
public partial class Game : Node
{
    public static Game I { get; private set; }

    public Settings Settings { get; private set; }
    public InputManager InputMgr { get; private set; }
    public Theme Theme { get; private set; }

    /// <summary>Kontener na świat gry (pokoje, postacie). Pod UI.</summary>
    public Node2D WorldRoot { get; private set; }

    /// <summary>Warstwa ekranów UI (menu, HUD, nakładki).</summary>
    public CanvasLayer ScreenLayer { get; private set; }

    private Control _screen;
    private FpsOverlay _fps;

    /// <summary>Argumenty po „--” z linii poleceń (np. --windowed, --run-tests).</summary>
    public string[] UserArgs { get; private set; } = Array.Empty<string>();

    public bool HasArg(string a) => Array.IndexOf(UserArgs, a) >= 0;

    public string ArgValue(string a)
    {
        int i = Array.IndexOf(UserArgs, a);
        return i >= 0 && i + 1 < UserArgs.Length ? UserArgs[i + 1] : null;
    }

    public override void _EnterTree()
    {
        I = this;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        UserArgs = OS.GetCmdlineUserArgs();
        Settings = Settings.Load();
        if (HasArg("--windowed"))
            Settings.WindowMode = WindowModeSetting.Okno;
        Settings.Apply();

        InputMgr = new InputManager { Name = "Input" };
        AddChild(InputMgr);

        Theme = UiTheme.Build();

        WorldRoot = new Node2D { Name = "World" };
        AddChild(WorldRoot);
        ScreenLayer = new CanvasLayer { Name = "Screens", Layer = 10 };
        AddChild(ScreenLayer);
        var overlay = new CanvasLayer { Name = "Overlay", Layer = 100 };
        AddChild(overlay);
        _fps = new FpsOverlay { Name = "Fps" };
        overlay.AddChild(_fps);
        _fps.Visible = Settings.ShowFps;
        AddChild(new Audio.Music { Name = "Music" });

        // dźwięki menu: przesunięcie fokusu i wciśnięcie dowolnego przycisku
        GetViewport().GuiFocusChanged += _ =>
        {
            if (Time.GetTicksMsec() > _quietUntil) Audio.Sfx.Play("select", 0.35f);
        };
        GetTree().NodeAdded += n =>
        {
            if (n is BaseButton b && !b.HasMeta("silent"))
                b.Pressed += () => Audio.Sfx.Play("confirm", 0.5f);
            if (n is Control)
                _quietUntil = Time.GetTicksMsec() + 120; // nowy ekran ustawia fokus – bez „kliku”
        };
    }

    private ulong _quietUntil;

    public void OnSettingsChanged()
    {
        Settings.Apply();
        Settings.Save();
        _fps.Visible = Settings.ShowFps;
    }

    /// <summary>Podmienia bieżący ekran UI (poprzedni jest usuwany).</summary>
    public void ShowScreen(Control screen)
    {
        if (_screen != null && IsInstanceValid(_screen))
            _screen.QueueFree();
        _screen = screen;
        screen.Theme = Theme;
        UiTheme.Fill(screen);
        ScreenLayer.AddChild(screen);
    }

    public Control CurrentScreen => _screen;

    /// <summary>Podmienia świat gry (np. arena, wyprawa). null = brak świata (menu).</summary>
    public void SetWorld(Node2D world)
    {
        foreach (var c in WorldRoot.GetChildren())
            c.QueueFree();
        if (world != null)
            WorldRoot.AddChild(world);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            foreach (var c in WorldRoot?.GetChildren() ?? new Godot.Collections.Array<Node>())
                if (c is View.RunScene rs)
                {
                    rs.SaveNow();
                    rs.Sim.Telemetry.EndRoom(rs.Sim, "quit");
                    Save.TelemetryWriter.Flush(rs.Sim.Telemetry);
                }
            Save.Profile.P.Save();
        }
    }

    public void Quit(int code = 0)
    {
        foreach (var c in WorldRoot?.GetChildren() ?? new Godot.Collections.Array<Node>())
            if (c is View.RunScene rs) rs.SaveNow();
        Settings?.Save();
        GetTree().Quit(code);
    }

    /// <summary>Zrzut ekranu (do testów QA). Zwraca ścieżkę albo null w trybie bez okna.</summary>
    public string Screenshot(string path)
    {
        if (DisplayServer.GetName() == "headless")
            return null;
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(path);
        return path;
    }
}
