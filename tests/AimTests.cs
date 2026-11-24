using System;
using System.Linq;
using Godot;
using Otchlan.Sim;

namespace Otchlan.Tests;

/// <summary>Celowanie ręczne a naprowadzanie: pocisk leci tam, gdzie gracz celuje, a dopiero za punktem celowania szuka wroga.</summary>
public static class AimTests
{
    private static (RunSim g, PlayerSim p) Setup()
    {
        var g = new RunSim(new[] { "mag" }, 1, 11) { God = true };
        var room = g.Floor.Rooms.Values.First(r => r.Kind == RoomKind.Combat);
        g.EnterRoom(room, null);
        g.Room.Waves.Clear();
        g.Enemies.Clear();
        g.Spawns.Clear();
        Array.Clear(g.Room.Grid);
        var p = g.Players[0];
        p.X = C.RCX - 300;
        p.Y = C.RCY;
        p.Homing = 4;
        p.Range = 900;
        // wróg z boku toru lotu (ok. 25° w dół od celownika)
        var e = g.SpawnEnemy("golem", C.RCX - 120, C.RCY + 90);
        e.Grace = 0;
        return (g, p);
    }

    private static float FlyAndMeasure(RunSim g, PlayerSim p, bool manual)
    {
        var target = new Vector2(C.RCX + 150, C.RCY);   // np. beczka na wprost
        var inp = new SimInput { Aim = 0, Target = target, Fire = true, Manual = manual };
        g.Update(1f / 60, new[] { inp });
        var b = g.Bullets.First(x => x.Friendly);
        // lot do połowy drogi do celownika: ręcznie – prosto, automatycznie – skręca do wroga
        for (int i = 0; i < 12 && b.Alive; i++)
            g.Update(1f / 60, new[] { new SimInput { Aim = 0, Target = target, Manual = manual } });
        return MathF.Abs(b.Y - p.Y);
    }

    [Test]
    public static void ManualShotIgnoresHomingUntilAimPoint()
    {
        var (g1, p1) = Setup();
        float manualDev = FlyAndMeasure(g1, p1, manual: true);
        var (g2, p2) = Setup();
        float autoDev = FlyAndMeasure(g2, p2, manual: false);
        Assert.True(manualDev < 8, $"pocisk ręczny zboczył o {manualDev:0.0} px");
        Assert.True(autoDev > manualDev + 10, $"naprowadzanie bez celowania ręcznego nie działa ({autoDev:0.0} px)");
    }
}
