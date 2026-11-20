using Godot;
using Otchlan.UI;

namespace Otchlan.Core;

/// <summary>
/// Scena startowa. Obsługuje tryby uruchomienia z linii poleceń (po „--”):
///   --run-tests            testy automatyczne, kod wyjścia 0 = OK
///   --tour DIR             zrzuty ekranów (menu, opcje, arena) do katalogu DIR i wyjście
///   --sandbox              od razu arena testowa
///   --windowed             okno zamiast pełnego ekranu
/// </summary>
public partial class Boot : Node
{
    public override void _Ready()
    {
        var game = Game.I;
        if (game.HasArg("--run-tests"))
        {
            CallDeferred(nameof(RunTests));
            return;
        }
        if (game.ArgValue("--tour") is { } dir)
        {
            var tour = new ScreenshotTour(dir) { Name = "Tour" };
            AddChild(tour);
            return;
        }
        if (game.HasArg("--sandbox"))
        {
            World.Sandbox.Open();
            return;
        }
        game.ShowScreen(new MainMenu());
    }

    private void RunTests()
    {
        int failed = Tests.TestRunner.RunAll();
        Game.I.Quit(failed == 0 ? 0 : 1);
    }
}
