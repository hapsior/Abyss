using System.Threading.Tasks;
using Godot;
using Otchlan.Core;
using Otchlan.Input;

namespace Otchlan.UI;

/// <summary>
/// Automatyczny przegląd ekranów do QA: otwiera kolejne ekrany i zapisuje zrzuty do katalogu.
/// Uruchomienie: godot --path . --rendering-driver opengl3 -- --windowed --tour DIR
/// </summary>
public partial class ScreenshotTour : Node
{
    private readonly string _dir;

    public ScreenshotTour(string dir) => _dir = dir;

    public override void _Ready()
    {
        DirAccess.MakeDirRecursiveAbsolute(_dir);
        _ = Run();
    }

    private async Task Wait(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

    private async Task Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var vs = GetViewport().GetVisibleRect().Size;
        var path = $"{_dir}/{name}_{vs.X:0}x{vs.Y:0}.png";
        Game.I.Screenshot(path);
        GD.Print("zrzut: ", path);
    }

    private async Task Run()
    {
        var g = Game.I;
        g.Settings.ShowFps = true;
        g.OnSettingsChanged();
        g.InputMgr.SetDevice(InputDevice.KeyboardMouse, -1);
        g.ShowScreen(new MainMenu());
        await Wait(0.8);
        await Shot("01_menu_klawiatura");
        g.InputMgr.SetDevice(InputDevice.Gamepad, 0);
        await Wait(0.3);
        await Shot("02_menu_pad");
        g.ShowScreen(new OptionsMenu(() => { }));
        await Wait(0.5);
        await Shot("03_opcje");
        World.Sandbox.Open();
        g.InputMgr.SetDevice(InputDevice.KeyboardMouse, -1);
        await Wait(1.0);
        await Shot("04_arena_klawiatura");
        g.InputMgr.SetDevice(InputDevice.Gamepad, 0);
        await Wait(0.3);
        await Shot("05_arena_pad");
        GD.Print($"FPS na koniec: {Engine.GetFramesPerSecond()}");
        g.Quit(0);
    }
}
