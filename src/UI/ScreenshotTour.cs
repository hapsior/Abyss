using System;
using System.Linq;
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
    /// <summary>Gdy ustawione: tylko zrzuty walk z bossami ("*" = wszyscy, albo lista kluczy po przecinku).</summary>
    public string BossesOnly;

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

    /// <summary>Wyprawa sterowana botem (przyspieszona): zrzuty walki, sklepu, awansu i bossa.</summary>
    private async Task RunTour()
    {
        var g = Game.I;
        var scene = View.RunScene.Start(new View.RunSetup { Chars = new() { "lowczyni" }, Seed = 4242 });
        scene.Sim.God = true;
        await Wait(0.2);
        scene.Sources.Clear();
        scene.Sources.Add(new View.BotSource());
        View.RunScene.LevelUpFactory = (s, p) => new View.BasicLevelUp(s, p);
        var taken = new System.Collections.Generic.HashSet<string>();
        double t0 = Time.GetTicksMsec() / 1000.0;
        while (Time.GetTicksMsec() / 1000.0 - t0 < 240 && taken.Count < 5)
        {
            var sim = scene.Sim;
            // przyspieszenie: dodatkowe kroki symulacji między klatkami
            if (scene.Overlay == null)
                for (int k = 0; k < 3; k++)
                    sim.Update(1 / 60f, new[] { scene.Sources[0].Read(scene, sim.Players[0]) });
            string key = null;
            if (scene.Overlay is View.BasicLevelUp && !taken.Contains("awans")) key = "awans";
            else if (sim.Room.Kind == Sim.RoomKind.Combat && sim.Enemies.Count >= 4 && sim.Bullets.Count >= 6 && sim.Depth >= 2) key = "walka";
            else if (sim.Room.Kind == Sim.RoomKind.Shop && sim.InteractTarget(sim.Players[0]) != null) key = "sklep";
            else if (sim.Boss != null && sim.Boss.Intro <= 0 && sim.Bullets.Count > 12) key = "boss";
            else if (sim.Room.Kind == Sim.RoomKind.Treasure && sim.InteractTarget(sim.Players[0]) != null) key = "skarbiec";
            if (key != null && !taken.Contains(key))
            {
                taken.Add(key);
                await Shot($"1{taken.Count}_wyprawa_{key}");
            }
            if (scene.Overlay is View.BasicLevelUp lu)
                lu.CallDeferred("Pick", 0);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    /// <summary>Walki z bossami (bot, nieśmiertelność): zrzut fazy 1 i fazy 2 każdego bossa.</summary>
    private async Task BossTour()
    {
        var keys = BossesOnly == "*"
            ? Sim.Content.Db.Bosses.Select(b => b.Key).Where(Sim.EnemyFactory.Has).ToList()
            : BossesOnly.Split(',').Select(k => k.Trim()).ToList();
        foreach (var key in keys)
        {
            var scene = View.RunScene.Start(new View.RunSetup { Chars = new() { "rycerz" }, Seed = 99, StartBoss = key, Slot = 0 });
            scene.Sim.God = true;
            await Wait(0.2);
            scene.Sources.Clear();
            scene.Sources.Add(new View.BotSource());
            View.RunScene.LevelUpFactory = (s, p) => new View.BasicLevelUp(s, p);
            // zrzut każdego ataku (1.2 s po jego rozpoczęciu), najpierw w fazie 1, potem w ostatniej fazie
            var shot = new System.Collections.Generic.HashSet<string>();
            for (int phase = 1; phase <= 2; phase++)
            {
                var sim = scene.Sim;
                if (sim.Boss == null) break;
                var b = sim.Boss;
                if (phase == 2)
                {
                    if (b.BDef.Phases.Length == 0) break;
                    b.Hp = MathF.Min(b.Hp, b.MaxHp * (b.BDef.Phases.Min() - 0.02f));
                }
                double t0 = Time.GetTicksMsec() / 1000.0;
                string cur = null;
                float since = 0;
                int shotsThisPhase = 0;
                while (Time.GetTicksMsec() / 1000.0 - t0 < 45 && sim.Boss != null && shotsThisPhase < 6)
                {
                    if (scene.Overlay is View.BasicLevelUp lu) lu.CallDeferred("Pick", 0);
                    if (scene.Overlay == null)
                        for (int k = 0; k < 2; k++)
                        {
                            sim.Update(1 / 60f, new[] { scene.Sources[0].Read(scene, sim.Players[0]) });
                            since += 1 / 60f;
                        }
                    if (sim.Boss == null) break;
                    float floor = phase == 1 && b.BDef.Phases.Length > 0 ? b.BDef.Phases.Max() + 0.03f : 0.15f;
                    if (b.Hp < b.MaxHp * floor) b.Hp = b.MaxHp * floor;
                    if (b.LastAttack != cur) { cur = b.LastAttack; since = 0; }
                    string tag = $"f{b.Phase}_{cur}";
                    if (cur != null && b.Intro <= 0 && since > 1.2f && !shot.Contains(tag))
                    {
                        shot.Add(tag);
                        shotsThisPhase++;
                        await Shot($"boss_{key}_{tag}");
                    }
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
            }
        }
    }

    private async Task Run()
    {
        var g = Game.I;
        if (BossesOnly != null)
        {
            g.Settings.ShowFps = true;
            g.OnSettingsChanged();
            g.InputMgr.SetDevice(InputDevice.Gamepad, 0);
            await BossTour();
            g.Quit(0);
            return;
        }
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
        await RunTour();
        GD.Print($"FPS na koniec: {Engine.GetFramesPerSecond()}");
        g.Quit(0);
    }
}
