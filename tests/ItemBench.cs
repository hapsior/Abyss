using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Otchlan.Core;
using Otchlan.Sim;

namespace Otchlan.Tests;

/// <summary>
/// Pomiar siły przedmiotów botem. Dla każdego etapu gry (wczesny / środek / późny) bot walczy na arenie z ciągle
/// uzupełnianą falą wrogów z krainy danego piętra, raz bez przedmiotu, raz z nim (ta sama postać, te same ulepszenia,
/// to samo ziarno). Mierzy:
///   – atak: obrażenia zadane wrogom na sekundę (bez nadmiaru ponad HP),
///   – obronę: czas przeżycia = maks. HP / ubytek HP na sekundę (po pancerzu, uniku, tarczy, regeneracji, kradzieży życia).
/// Siła = atak^0.6 · obrona^0.4 (względem bazy). Przedmioty czysto użytkowe (szczęście, monety, zbieranie) wychodzą ~0%.
/// Uruchomienie: godot --headless --path . -- --item-bench WYJŚCIE.csv [--items k1,k2] [--chars rycerz,lowczyni] [--seeds 3]
/// </summary>
public static class ItemBench
{
    public sealed record Stage(string Name, int Depth, int Level, int BaseItems);

    public static readonly Stage[] Stages =
    {
        new("wczesny", 3, 3, 0),
        new("srodek", 15, 12, 4),
        new("pozny", 30, 22, 10),
    };

    public const float ArenaTime = 60, Warmup = 4;
    public static bool Verbose;

    public sealed record Result(float Dps, float LossPerSec, float MaxHp)
    {
        public float Survive => MaxHp / MathF.Max(0.5f, LossPerSec);
    }

    /// <summary>Jedna walka na arenie. extra = klucz przedmiotu dodanego do bazowej postaci (null = baza).</summary>
    public static Result Arena(string ch, Stage st, string extra, ulong seed)
    {
        var g = new RunSim(new[] { ch }, 1, seed) { Bench = true };
        g.NewFloor(st.Depth);
        var room = g.Floor.Rooms.Values.First(r => r.Kind == RoomKind.Combat);
        g.EnterRoom(room, null);
        g.Room.Waves.Clear();
        g.Enemies.Clear();
        g.Spawns.Clear();
        Array.Clear(g.Room.Grid);
        var p = g.Players[0];
        p.X = C.RCX;
        p.Y = C.RCY;

        // budowa postaci: ulepszenia z awansów i bazowe przedmioty – zależą tylko od (postać, etap, ziarno), nie od testowanego przedmiotu
        var rng = new Rng(seed * 7919 + (ulong)st.Depth);
        for (int lv = 2; lv <= st.Level; lv++)
        {
            p.Level = lv;
            Items.ApplyUpgrade(p, Items.RollUpgrades(rng, p.Luck, lv)[0]);
        }
        var common = Content.Db.Items.Where(i => i.Rarity <= 1 && i.Fx.Count > 0 && i.Special == null).ToList();
        for (int i = 0; i < st.BaseItems; i++)
            p.ApplyItem(common[rng.RangeInt(0, common.Count - 1)], g);
        if (extra != null)
            p.ApplyItem(Content.Db.Items.First(i => i.Key == extra), g);
        p.Hp = p.MaxHp;
        p.PendingLevelUps = 0;

        var pool = Content.Db.BiomeFor(st.Depth).Pool;
        float total = pool.Sum(e => e.Weight);
        int keep = 8 + st.Depth / 4;   // dużo wrogów naraz – bot musi obrywać, żeby obrona była mierzalna
        var bot = new Bot(g, p, seed + 1);
        const float dt = 1f / 60;
        float t = 0, dealt0 = 0, loss0 = 0, hp0 = 0;
        while (t < ArenaTime)
        {
            // ciągła fala: zawsze `keep` wrogów, nowi z dala od gracza
            while (g.Enemies.Count(e => e.Alive) + g.Spawns.Count < keep)
            {
                float r = rng.Range(0, total);
                string key = pool[^1].Enemy;
                foreach (var pe in pool)
                {
                    if (r < pe.Weight) { key = pe.Enemy; break; }
                    r -= pe.Weight;
                }
                float a = rng.Range(0, MathF.Tau), d = rng.Range(300, 520);
                g.SpawnEnemy(key, p.X + MathF.Cos(a) * d, p.Y + MathF.Sin(a) * d);
            }
            g.Update(dt, new[] { bot.Input() });
            while (g.Events.Count > 0) g.Events.Dequeue();
            if (g.Room != room)
            {
                GD.PrintErr($"bench: bot opuścił arenę ({ch}, {st.Name}, {extra})");
                break;
            }
            p.PendingLevelUps = 0;
            t += dt;
            if (t >= Warmup && dealt0 == 0 && loss0 == 0 && hp0 == 0)
            {
                dealt0 = g.BenchDealt;
                loss0 = g.BenchRefill;
                hp0 = p.Hp;
            }
        }
        float span = ArenaTime - Warmup;
        if (Verbose)
            GD.Print($"  arena {ch} {st.Name} {extra ?? "-"} seed {seed}: t {t:0.0} zabici {g.Stats.Kills} zadane {g.BenchDealt:0} uzup {g.BenchRefill:0} hp {p.Hp:0}/{p.MaxHp:0} wrogów {g.Enemies.Count} stan {g.State}");
        float loss = g.BenchRefill - loss0 + (hp0 - p.Hp);
        return new Result((g.BenchDealt - dealt0) / span, MathF.Max(0, loss) / span, p.MaxHp);
    }

    public static void Run(string outPath, string items, string chars, int seeds)
    {
        var ci = CultureInfo.InvariantCulture;
        var keys = items == "*" ? Content.Db.Items.Select(i => i.Key).ToList() : items.Split(',').Select(s => s.Trim()).ToList();
        var chs = chars.Split(',').Select(s => s.Trim()).ToArray();
        var jobs = new List<(Stage st, string ch, int seed)>();
        foreach (var st in Stages)
            foreach (var ch in chs)
                for (int s = 0; s < seeds; s++)
                    jobs.Add((st, ch, s));

        // baza dla każdego (etap, postać, ziarno)
        var baseRes = new Dictionary<(string, string, int), Result>();
        foreach (var j in jobs)
            baseRes[(j.st.Name, j.ch, j.seed)] = Arena(j.ch, j.st, null, (ulong)(1000 + j.seed));
        var sb = new StringBuilder("item,rarity,stage,offense_pct,defense_pct,power_pct,base_dps,base_survive_s\n");
        foreach (var key in keys)
        {
            var def = Content.Db.Items.First(i => i.Key == key);
            var line = new StringBuilder($"{key,-20} r{def.Rarity}");
            foreach (var st in Stages)
            {
                // stosunek sum (a nie średnia stosunków): pojedyncze walki z bardzo małym ubytkiem HP nie zawyżają wyniku
                double bDps = 0, iDps = 0, bHp = 0, iHp = 0, bLoss = 0, iLoss = 0, bsurv = 0;
                int n = 0;
                foreach (var ch in chs)
                    for (int s = 0; s < seeds; s++)
                    {
                        var b = baseRes[(st.Name, ch, s)];
                        var r = Arena(ch, st, key, (ulong)(1000 + s));
                        bDps += b.Dps; iDps += r.Dps;
                        bHp += b.MaxHp; iHp += r.MaxHp;
                        bLoss += b.LossPerSec; iLoss += r.LossPerSec;
                        bsurv += b.Survive;
                        n++;
                    }
                double bdps = bDps;
                double o = iDps / Math.Max(1, bDps);
                double d = (iHp / Math.Max(0.05, iLoss)) / (bHp / Math.Max(0.05, bLoss));
                double pw = Math.Pow(o, 0.6) * Math.Pow(d, 0.4);
                sb.AppendLine(string.Join(",", key, def.Rarity, st.Name, ((o - 1) * 100).ToString("0", ci), ((d - 1) * 100).ToString("0", ci),
                    ((pw - 1) * 100).ToString("0", ci), (bdps / n).ToString("0", ci), (bsurv / n).ToString("0", ci)));
                line.Append($"  {st.Name}: atak {(o - 1) * 100,4:0}% obr {(d - 1) * 100,4:0}% siła {(pw - 1) * 100,4:0}%");
            }
            GD.Print(line.ToString());
        }
        using var f = FileAccess.Open(outPath, FileAccess.ModeFlags.Write);
        f.StoreString(sb.ToString());
        GD.Print("zapisano: ", outPath);
    }
}
