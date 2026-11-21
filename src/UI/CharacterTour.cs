using System.Linq;
using System.Threading.Tasks;
using Godot;
using Otchlan.Core;

namespace Otchlan.UI;

/// <summary>
/// QA postaci: każda wskazana postać walczy (bot) w pokoju walki na piętrze 3 i używa umiejętności; zrzut 0.4 s po jej użyciu.
/// Uruchomienie: godot --path . --rendering-driver opengl3 -- --windowed --char-tour DIR [--chars k1,k2]
/// </summary>
public partial class CharacterTour : Node
{
    private readonly string _dir, _chars;

    public CharacterTour(string dir, string chars)
    {
        _dir = dir;
        _chars = chars;
    }

    public override void _Ready()
    {
        DirAccess.MakeDirRecursiveAbsolute(_dir);
        _ = Run();
    }

    private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async Task Run()
    {
        var g = Game.I;
        g.Settings.ShowFps = true;
        g.OnSettingsChanged();
        var keys = _chars == "*" ? Sim.Content.Db.Characters.Select(c => c.Key).ToList() : _chars.Split(',').Select(k => k.Trim()).ToList();
        foreach (var key in keys)
        {
            var scene = View.RunScene.Start(new View.RunSetup { Chars = new() { key }, Seed = 77, Slot = 0 });
            scene.Sim.God = true;
            await Frame();
            var sim = scene.Sim;
            sim.NewFloor(3);
            sim.EnterRoom(sim.Floor.Rooms.Values.First(r => r.Kind == Sim.RoomKind.Combat), null);
            scene.Sources.Clear();
            scene.Sources.Add(new View.BotSource());
            View.RunScene.LevelUpFactory = (s, p) => new View.BasicLevelUp(s, p);
            float t = 0;
            bool used = false;
            float since = 0;
            while (t < 12)
            {
                if (scene.Overlay is View.BasicLevelUp lu) lu.CallDeferred("Pick", 0);
                if (scene.Overlay == null)
                {
                    for (int k = 0; k < 2; k++)
                        sim.Update(1 / 60f, new[] { scene.Sources[0].Read(scene, sim.Players[0]) });
                    t += 2 / 60f;
                    if (used) since += 2 / 60f;
                }
                var p = sim.Players[0];
                if (!used && t > 2.5f && sim.Enemies.Count >= 2)
                {
                    var e = sim.Enemies[0];
                    p.UseAbility(sim, new Sim.SimInput { Aim = p.Aim, Target = new Vector2(e.X, e.Y), Move = Vector2.Zero });
                    used = true;
                }
                if (used && since > 0.4f) break;
                await Frame();
            }
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var vs = GetViewport().GetVisibleRect().Size;
            var path = $"{_dir}/postac_{key}_{vs.X:0}x{vs.Y:0}.png";
            g.Screenshot(path);
            GD.Print("zrzut: ", path);
        }
        g.Quit(0);
    }
}
