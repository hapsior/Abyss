using System;
using System.Linq;
using Otchlan.Sim;

namespace Otchlan.Tests;

/// <summary>Reguły balansu przedmiotów i ołtarza krwi.</summary>
public static class BalanceTests
{
    [Test]
    public static void ExtraProjectilesAddLessThanFullBullet()
    {
        Assert.Near(1, Balance.ProjectileDmgMult(1), 1e-5);
        Assert.Near(1.45, Balance.ProjectileDmgMult(2) * 2, 1e-4);
        Assert.Near(1.9, Balance.ProjectileDmgMult(3) * 3, 1e-4);
    }

    [Test]
    public static void BloodAltarCostsCurrentAndMaxHp()
    {
        var g = new RunSim(new[] { "rycerz" }, 1, 21) { God = false };
        var p = g.Players[0];
        var room = g.Floor.Rooms.Values.First(r => r.Kind == RoomKind.Combat);
        g.EnterRoom(room, null);
        var item = Content.Db.Items.First(i => i.Rarity >= 2);
        var pd = new Pedestal(p.X, p.Y, item, 0, PedestalKind.Shrine, group: 9, hpCost: Balance.ShrineHpCostPct) { Id = 999 };
        room.Pedestals.Add(pd);
        float max0 = p.MaxHp;
        p.Hp = max0;
        Assert.True(g.UsePedestal(pd, p), "ofiara nieprzyjęta przy pełnym życiu");
        Assert.Near(MathF.Round(max0 * (1 - Balance.ShrineMaxHpLoss)), p.MaxHp, 1e-3);
        Assert.Near(max0 * (1 - Balance.ShrineHpCostPct / 100f), p.Hp, 1.01);
        // przy niskim życiu ołtarz odmawia – sama ofiara nie może zabić
        var pd2 = new Pedestal(p.X, p.Y, Content.Db.Items.Last(i => i.Rarity >= 2), 0, PedestalKind.Shrine, group: 10, hpCost: Balance.ShrineHpCostPct) { Id = 998 };
        room.Pedestals.Add(pd2);
        p.Hp = p.MaxHp * 0.3f;
        Assert.True(!g.UsePedestal(pd2, p), "ofiara przyjęta przy zbyt niskim życiu");
    }
}
