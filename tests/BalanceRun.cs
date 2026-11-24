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
    /// <param name="mode">god – nieśmiertelny (stary pomiar „obrażeń boga”), bench – prawdziwe obrażenia, ale bez śmierci
    /// (HP uzupełniane poniżej połowy), mortal – zwykła gra do śmierci (zapisuje piętro i przyczynę).</param>
    /// <summary>Gdy ustawione: wszystkie wpisy telemetrii (pokój po pokoju) trafiają do tego pliku JSONL – ten sam format co u gracza.</summary>
    public static string TelemetryOut;
    private static readonly List<RoomLog> _logs = new();

    public static void Run(string outPath, string chars, int floors, ulong seed, BotProfile profile = null, string mode = "god")
    {
        var keys = chars.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ci = CultureInfo.InvariantCulture;
        profile ??= BotProfile.Ideal;
        var sb = new StringBuilder("char,bot,mode,depth,biome,floor_time_s,kills,god_dmg_pct_hp,level,items,dps,enemy_hp_scale,boss_time_s,coins,real_dmg_pct_hp,hits,died\n");
        foreach (var key in keys)
        {
            var g = new RunSim(new[] { key }, 1, seed) { God = mode == "god", Bench = mode == "bench" };
            var p = g.Players[0];
            var bot = new Bot(g, p, seed + 3, profile);
            int tel0 = 0;
            float dealt0 = 0, combatT = 0;
            const float dt = 1f / 60;
            int depth = g.Depth, kills0 = 0;
            float floorT = 0, god0 = 0, bossT = 0, total = 0;
            bool bossAlive = false;
            while (g.Depth <= floors && total < 60 * 60 * 3)
            {
                if (g.State == RunState.Dying || p.Dead)
                {
                    _logs.AddRange(g.Telemetry.All.Select(r => { r.Run = $"bot-{profile.Name}-{seed}"; return r; }));
                    var last = g.Telemetry.All.LastOrDefault();
                    float rd = g.Telemetry.All.Skip(tel0).Sum(r => r.Damage);
                    sb.AppendLine(string.Join(",", key, profile.Name, mode, depth, Content.Db.BiomeFor(depth).Key, floorT.ToString("0", ci),
                        g.Stats.Kills - kills0, "", p.Level, p.Items.Count, "", Balance.EnemyHpScale(depth).ToString("0.00", ci),
                        bossAlive ? bossT.ToString("0", ci) : "", g.Coins, (rd / p.MaxHp * 100).ToString("0", ci),
                        g.Telemetry.All.Skip(tel0).Sum(r => r.Hits), 1));
                    GD.Print($"  {key} [{profile.Name}] ZGINĄŁ na piętrze {depth} ({last?.Room}, przyczyna: {last?.DeathCause}) po {g.RunTime / 60:0.0} min");
                    break;
                }
                g.Update(dt, new[] { bot.Input() });
                floorT += dt;
                total += dt;
                if (g.Boss != null) { bossT += dt; bossAlive = true; }
                if (g.Enemies.Count > 0) combatT += dt;
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
                    float dps = (g.Stats.DamageDealt - dealt0) / MathF.Max(1, combatT);   // rzeczywiste obrażenia na sekundę walki
                    float realDmg = g.Telemetry.All.Skip(tel0).Sum(r => r.Damage);
                    int hits = g.Telemetry.All.Skip(tel0).Sum(r => r.Hits);
                    tel0 = g.Telemetry.All.Count;
                    sb.AppendLine(string.Join(",", key, profile.Name, mode, depth, Content.Db.BiomeFor(depth).Key,
                        floorT.ToString("0", ci), g.Stats.Kills - kills0, ((g.Stats.GodDmg - god0) / p.MaxHp * 100).ToString("0", ci),
                        p.Level, p.Items.Count, dps.ToString("0", ci), Balance.EnemyHpScale(depth).ToString("0.00", ci),
                        bossAlive ? bossT.ToString("0", ci) : "", g.Coins, (realDmg / p.MaxHp * 100).ToString("0", ci), hits, 0));
                    GD.Print($"  {key} piętro {depth}: {floorT:0} s, dmg {(mode == "god" ? (g.Stats.GodDmg - god0) : realDmg) / p.MaxHp * 100:0}% HP, trafień {hits}, poz. {p.Level}, przedm. {p.Items.Count}, dps {dps:0}{(bossAlive ? $", boss {bossT:0} s" : "")}");
                    depth = g.Depth;
                    floorT = 0;
                    bossT = 0;
                    bossAlive = false;
                    kills0 = g.Stats.Kills;
                    god0 = g.Stats.GodDmg;
                    dealt0 = g.Stats.DamageDealt;
                    combatT = 0;
                }
            }
            if (!(g.State == RunState.Dying || p.Dead))
                _logs.AddRange(g.Telemetry.All.Select(r => { r.Run = $"bot-{profile.Name}-{seed}"; return r; }));
        }
        using var f = FileAccess.Open(outPath, FileAccess.ModeFlags.Write);
        f.StoreString(sb.ToString());
        GD.Print("zapisano: ", outPath);
        if (TelemetryOut != null)
        {
            var opt = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower };
            System.IO.File.WriteAllLines(TelemetryOut, _logs.Select(l => System.Text.Json.JsonSerializer.Serialize(l, opt)));
            GD.Print("telemetria: ", TelemetryOut);
        }
    }
}
