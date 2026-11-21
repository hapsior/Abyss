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

    private static T Find<T>(Node root, Func<T, bool> pred = null) where T : Node
    {
        foreach (var c in root.GetChildren())
        {
            if (c is T t && (pred == null || pred(t))) return t;
            var r = Find(c, pred);
            if (r != null) return r;
        }
        return null;
    }

    private static void PressButton(Node root, string text) =>
        Find<Button>(root, b => b.Text == text)?.EmitSignal(BaseButton.SignalName.Pressed);

    /// <summary>Ekrany menu (slot, tryb, postać, trudność) i nakładki wyprawy (awans, sklep, pauza, wynik).</summary>
    private async Task UiTour()
    {
        var g = Game.I;
        g.InputMgr.SetDevice(InputDevice.KeyboardMouse, -1);
        Save.Profile.Override(new Save.Profile { SealMax = 3 });
        var demo = new Sim.RunSim(new[] { "lowczyni", "mag" }, 2, 77, 1);
        Save.SaveSystem.Save(2, demo);
        Save.SaveSystem.Delete(1);
        Save.SaveSystem.Delete(3);

        g.ShowScreen(new SlotSelect());
        await Wait(0.5);
        await Shot("10_sloty");
        PressButton(g.CurrentScreen, "Usuń");
        await Wait(0.4);
        await Shot("11_sloty_usun");
        g.ShowScreen(new ModeSelect(new NewRunFlow()));
        await Wait(0.4);
        await Shot("12_tryb");
        g.ShowScreen(new CharSelect(new NewRunFlow(), 0));
        await Wait(0.5);
        await Shot("13_postac");
        g.InputMgr.SetDevice(InputDevice.Gamepad, 0);
        var lastBtn = Find<Button>(g.CurrentScreen, b => b.Text == "" && b.CustomMinimumSize.X == 200 && b.GetIndex() == b.GetParent().GetChildCount() - 1);
        lastBtn?.GrabFocus();
        await Wait(0.4);
        await Shot("14_postac_zablokowana_pad");
        g.InputMgr.SetDevice(InputDevice.KeyboardMouse, -1);
        g.ShowScreen(new CharSelect(new NewRunFlow { Coop = true, Chars = new() { "rycerz" } }, 1));
        await Wait(0.4);
        await Shot("15_postac_p2");
        var df = new DiffSelect(new NewRunFlow { Coop = true, Chars = new() { "rycerz", "mag" } });
        g.ShowScreen(df);
        await Wait(0.4);
        var seal = Find<SealRow>(df);
        for (int i = 0; i < 3; i++)
            seal?.EmitSignal(BaseButton.SignalName.Pressed);
        await Wait(0.3);
        await Shot("16_trudnosc");

        // --- nakładki wyprawy
        var scene = View.RunScene.Start(new View.RunSetup { Chars = new() { "rycerz", "mag" }, Seed = 31, Slot = 0, Difficulty = 2, Seal = 1 });
        await Wait(0.4);
        var sim = scene.Sim;
        sim.Coins = 63;
        sim.Depth = 43;
        var items = Sim.Content.Db.Items;
        for (int i = 0; i < 46; i++) sim.Players[0].Items.Add(items[i % items.Count].Key);
        sim.Players[0].Items.Add(items[0].Key);
        for (int i = 0; i < 9; i++) sim.Players[1].Items.Add(items[(i * 7) % items.Count].Key);
        sim.Players[0].Level = 5;
        sim.Players[0].PendingLevelUps = 1;
        sim.Players[1].Hp = 40;
        scene.OpenOverlay(new LevelUpOverlay(scene, sim.Players[0]));
        await Wait(0.6);
        await Shot("20_awans");
        var lu = (LevelUpOverlay)scene.Overlay;
        lu.Banish();
        await Wait(0.3);
        lu.Reroll();
        await Wait(0.4);
        await Shot("21_awans_po_losowaniu");
        sim.Players[0].PendingLevelUps = 0;
        scene.CloseOverlay();

        var room = sim.Room;
        room.Pedestals.Clear();
        var rar = items.OrderBy(i => i.Rarity).ToList();
        room.Pedestals.Add(new Sim.Pedestal(500, 400, rar[2], 14, Sim.PedestalKind.Shop));
        room.Pedestals.Add(new Sim.Pedestal(600, 400, rar[items.Count / 2], 29, Sim.PedestalKind.Shop));
        room.Pedestals.Add(new Sim.Pedestal(700, 400, rar[items.Count - 2], 140, Sim.PedestalKind.Shop));
        room.Pedestals.Add(new Sim.Pedestal(800, 400, null, 6, Sim.PedestalKind.HeartShop, heal: 0.35f));
        scene.OpenOverlay(new ShopMenu(scene, sim.Players[1]));
        await Wait(0.5);
        await Shot("22_sklep");
        scene.CloseOverlay();

        scene.OpenOverlay(View.RunScene.PauseFactory(scene));
        await Wait(0.5);
        await Shot("23_pauza");
        PressButton(scene.Overlay, "Statystyki");
        await Wait(0.5);
        await Shot("24_pauza_statystyki");
        scene.CloseOverlay();
        scene.OpenOverlay(View.RunScene.PauseFactory(scene));
        await Wait(0.3);
        PressButton(scene.Overlay, "Opcje");
        await Wait(0.5);
        await Shot("25_pauza_opcje");
        scene.CloseOverlay();

        sim.Stats.Kills = 412; sim.Stats.DamageDealt = 88123; sim.Stats.Bosses = 8; sim.Stats.Coins = 530;
        sim.RunTime = 1834;
        scene.OpenOverlay(View.RunScene.ResultFactory(scene));
        await Wait(0.5);
        await Shot("26_wynik");
        scene.CloseOverlay();
        Save.SaveSystem.Delete(2);
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
        await UiTour();
        if (OS.GetCmdlineUserArgs().Contains("--uionly"))
        {
            g.Quit(0);
            return;
        }
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
