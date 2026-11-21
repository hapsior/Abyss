using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;
using Otchlan.Sim;

namespace Otchlan.Tests;

/// <summary>
/// Symulacja balansu: bot (nieśmiertelny, liczy „obrażenia boga”) przechodzi kolejne piętra wybraną postacią.
/// Dla każdego piętra zapisuje: czas, zabójstwa, obrażenia, które by otrzymał (w % maks. HP), poziom, liczbę przedmiotów,
/// DPS gracza (szacunek), skalę HP wrogów i czas walki z bossem.
/// Uruchomienie: godot --headless --path . -- --balance WYJŚCIE.csv [--chars rycerz,mag] [--floors 45] [--seed 1]
/// </summary>
public static class BalanceRun
{
    public static void Run(string outPath, string chars, int floors, ulong seed)
    {
        var keys = chars.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("char,depth,biome,floor_time_s,kills,god_dmg_pct_hp,level,items,dps,enemy_hp_scale,boss_time_s,coins\n");
        foreach (var key in keys)
        {
            var g = new RunSim(new[] { key }, 1, seed) { God = true };
            var p = g.Players[0];
            var bot = new Bot(g, p, seed + 3);
            const float dt = 1f / 60;
            int depth = g.Depth, kills0 = 0;
            float floorT = 0, god0 = 0, bossT = 0, total = 0;
            bool bossAlive = false;
            while (g.Depth <= floors && total < 60 * 60 * 3)
            {
                g.Update(dt, new[] { bot.Input() });
                floorT += dt;
                total += dt;
                if (g.Boss != null) { bossT += dt; bossAlive = true; }
                while (g.Events.Count > 0)
                {
                    var ev = g.Events.Dequeue();
                    if (ev.Kind == RunEventKind.LevelUp)
                    {
                        var ups = Items.RollUpgrades(g.R, ev.Player.Luck, ev.Player.Level, banned: ev.Player.Banished);
                        Items.ApplyUpgrade(ev.Player, ups[0]);
                        ev.Player.PendingLevelUps--;
                    }
                }
                if (floorT > 60 * 12)
                {
                    var pl = p;
                    GD.Print($"  {key}: utknął na piętrze {g.Depth} – przerwano: pokój {g.Room.Kind} ({g.Room.Gx},{g.Room.Gy}) czysty={g.Room.Cleared}, " +
                        $"wrogów {g.Enemies.Count} [{string.Join(";", g.Enemies.Select(e => $"{e.Key} {e.X:0},{e.Y:0} hp {e.Hp:0}"))}], spawny {g.Spawns.Count}, " +
                        $"znajdźki [{string.Join(";", g.Room.Pickups.Select(k => $"{k.Kind} {k.X:0},{k.Y:0} tile={g.Room.TileAt(k.X, k.Y)?.Kind}"))}], piedestały {g.Room.Pedestals.Count}, gracz ({pl.X:0},{pl.Y:0}), drzwi {string.Join(",", g.Room.Doors.Keys)}, " +
                        $"fale {g.WaveIdx}/{g.Room.Waves.Count}, stan {g.State}, właz {g.Room.Trapdoor}, pokoje {g.Floor.Rooms.Count} (odwiedzone {g.Floor.Rooms.Values.Count(r => r.Visited)}, czyste {g.Floor.Rooms.Values.Count(r => r.Cleared)})");
                    var grid = new StringBuilder();
                    for (int r = 0; r < C.GRows; r++)
                    {
                        for (int c = 0; c < C.GCols; c++)
                        {
                            bool pl2 = Room.ColOf(pl.X) == c && Room.RowOf(pl.Y) == r;
                            bool pk = g.Room.Pickups.Any(k => Room.ColOf(k.X) == c && Room.RowOf(k.Y) == r);
                            grid.Append(pl2 ? 'P' : pk ? 'H' : g.Room.Grid[r, c] is { } o ? (o.Solid ? '#' : 'o') : '.');
                        }
                        grid.Append('\n');
                    }
                    GD.Print(grid.ToString());
                    var et = bot.ExploreTarget();
                    if (et is { } tv)
                    {
                        var fl = g.Room.FlowField(tv.X, tv.Y);
                        int pc = Room.ColOf(pl.X), pr = Room.RowOf(pl.Y);
                        GD.Print($"  cel {tv}, los {g.Room.Los(pl.X, pl.Y, tv.X, tv.Y)}, flow tu {fl[pr, pc]}, lewo {fl[pr, pc - 1]}, góra {fl[pr - 1, pc]}, prawo {fl[pr, pc + 1]}, dół {fl[pr + 1, pc]}");
                    }
                    var inp = bot.Input();
                    GD.Print($"  ruch bota {inp.Move}, prędkość gracza {pl.Vx:0},{pl.Vy:0}, hp {pl.Hp}/{pl.MaxHp}");
                    break;
                }
                if (g.Depth != depth)
                {
                    float dps = p.EffDamage() / p.FireInterval() * MathF.Max(1, p.Projectiles);
                    sb.AppendLine(string.Join(",", key, depth, Content.Db.BiomeFor(depth).Key,
                        floorT.ToString("0", ci), g.Stats.Kills - kills0, ((g.Stats.GodDmg - god0) / p.MaxHp * 100).ToString("0", ci),
                        p.Level, p.Items.Count, dps.ToString("0", ci), Balance.EnemyHpScale(depth).ToString("0.00", ci),
                        bossAlive ? bossT.ToString("0", ci) : "", g.Coins));
                    GD.Print($"  {key} piętro {depth}: {floorT:0} s, dmg {(g.Stats.GodDmg - god0) / p.MaxHp * 100:0}% HP, poz. {p.Level}, przedm. {p.Items.Count}, dps {dps:0}{(bossAlive ? $", boss {bossT:0} s" : "")}");
                    depth = g.Depth;
                    floorT = 0;
                    bossT = 0;
                    bossAlive = false;
                    kills0 = g.Stats.Kills;
                    god0 = g.Stats.GodDmg;
                }
            }
        }
        using var f = FileAccess.Open(outPath, FileAccess.ModeFlags.Write);
        f.StoreString(sb.ToString());
        GD.Print("zapisano: ", outPath);
    }
}
