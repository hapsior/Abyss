using Godot;
using Otchlan.UI;

namespace Otchlan.Core;

/// <summary>
/// Scena startowa. Obsługuje tryby uruchomienia z linii poleceń (po „--”):
///   --run-tests            testy automatyczne, kod wyjścia 0 = OK
///   --tour DIR             zrzuty ekranów (menu, opcje, arena) do katalogu DIR i wyjście
///   --sandbox              od razu arena testowa
///   --boss KLUCZ           od razu walka z bossem (np. spore_mother), bez zapisu
///   --boss-tour DIR        zrzuty walk z bossami (opcjonalnie --bosses k1,k2) i wyjście
///   --balance PLIK.csv     symulacja balansu botem (opcjonalnie --chars k1,k2 --floors N --seed S), bez okna
///   --char-tour DIR        zrzuty postaci używających umiejętności (opcjonalnie --chars k1,k2) i wyjście
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
        if (game.ArgValue("--boss-tour") is { } bdir)
        {
            AddChild(new ScreenshotTour(bdir) { Name = "Tour", BossesOnly = game.ArgValue("--bosses") ?? "*" });
            return;
        }
        if (game.ArgValue("--balance") is { } csv)
        {
            Tests.BalanceRun.Run(csv, game.ArgValue("--chars") ?? "rycerz", int.Parse(game.ArgValue("--floors") ?? "45"),
                ulong.Parse(game.ArgValue("--seed") ?? "1"));
            game.Quit(0);
            return;
        }
        if (game.ArgValue("--char-tour") is { } cdir)
        {
            AddChild(new CharacterTour(cdir, game.ArgValue("--chars") ?? "*") { Name = "CharTour" });
            return;
        }
        if (game.ArgValue("--boss") is { } boss)
        {
            View.RunScene.Start(new View.RunSetup { StartBoss = boss, Slot = 0 });
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
