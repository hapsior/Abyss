using System;
using System.Collections.Generic;
using System.Linq;
using Otchlan.Core;

namespace Otchlan.Sim;

/// <summary>
/// Piętro: siatka pokoi jak w Isaacu (mapa 9×8). Co 5. piętro kończy się bossem,
/// pozostałe – pokojem wyjścia ze strażnikami (na 3. piętrze krainy: mini-boss).
/// </summary>
public sealed class Floor
{
    public const int MapW = 9, MapH = 8;

    public int Depth;
    public BiomeDef Biome;
    public readonly Dictionary<(int, int), Room> Rooms = new();
    public Room Start;

    public bool IsBossFloor => Depth % C.FloorsPerBiome == 0;
    public bool IsMiniBossFloor => Depth % C.FloorsPerBiome == 3;

    public Room RoomAt(int gx, int gy) => Rooms.TryGetValue((gx, gy), out var r) ? r : null;

    public static Floor Generate(int depth, DifficultyDef diff, Rng rng, float countMult = 1f)
    {
        var f = new Floor { Depth = depth, Biome = Content.Db.BiomeFor(depth) };
        f.Build(diff, rng, countMult);
        return f;
    }

    private static int Neighbors(HashSet<(int, int)> cells, (int x, int y) c)
    {
        int n = 0;
        foreach (var d in Dirs.All)
        {
            var (dx, dy) = Dirs.Delta(d);
            if (cells.Contains((c.x + dx, c.y + dy))) n++;
        }
        return n;
    }

    private static Dictionary<(int, int), int> Bfs(HashSet<(int, int)> cells, (int, int) start)
    {
        var dist = new Dictionary<(int, int), int> { [start] = 0 };
        var q = new Queue<(int x, int y)>();
        q.Enqueue(start);
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            foreach (var d in Dirs.All)
            {
                var (dx, dy) = Dirs.Delta(d);
                var n = (c.x + dx, c.y + dy);
                if (cells.Contains(n) && !dist.ContainsKey(n))
                {
                    dist[n] = dist[c] + 1;
                    q.Enqueue(n);
                }
            }
        }
        return dist;
    }

    private void Build(DifficultyDef diff, Rng rng, float countMult)
    {
        var start = (4, 4);
        bool wantShrine = Depth >= Balance.ShrineMinDepth && rng.Chance(Balance.ShrineChance);   // ołtarz krwi: rzadko
        int need = 3 + (wantShrine ? 1 : 0);               // wyjście/boss + skarbiec + sklep (+ ołtarz)
        int target = Math.Min(6 + rng.RangeInt(0, 2) + (IsBossFloor ? 1 : 0) + (wantShrine ? 1 : 0), 11);
        HashSet<(int x, int y)> cells = null;
        bool ok = false;
        for (int attempt = 0; attempt < 500 && !ok; attempt++)
        {
            cells = new HashSet<(int, int)> { start };
            var q = new Queue<(int x, int y)>();
            q.Enqueue(start);
            while (q.Count > 0 && cells.Count < target)
            {
                var c = q.Dequeue();
                var dirs = Dirs.All.ToList();
                rng.Shuffle(dirs);
                foreach (var d in dirs)
                {
                    var (dx, dy) = Dirs.Delta(d);
                    var n = (c.x + dx, c.y + dy);
                    if (n.Item1 < 0 || n.Item1 >= MapW || n.Item2 < 0 || n.Item2 >= MapH || cells.Contains(n))
                        continue;
                    if (Neighbors(cells, n) > 1 || cells.Count >= target || rng.Chance(0.5f))
                        continue;
                    cells.Add(n);
                    q.Enqueue(n);
                }
                if (q.Count == 0 && cells.Count < target)
                    q.Enqueue(rng.Pick(cells.ToList()));
            }
            if (cells.Count < target)
                continue;
            var dist0 = Bfs(cells, start);
            var dead0 = cells.Where(c => c != start && Neighbors(cells, c) == 1).OrderByDescending(c => dist0[c]).ToList();
            if (dead0.Count < need || dist0[dead0[0]] < 3)
                continue;
            ok = true;
        }
        if (!ok)
        {
            // awaryjny układ, zawsze poprawny
            cells = new HashSet<(int, int)> { start, (5, 4), (6, 4), (7, 4), (8, 4), (4, 3), (4, 5), (3, 4), (2, 4), (6, 3), (6, 5) };
        }
        var dist = Bfs(cells, start);
        var dead = cells.Where(c => c != start && Neighbors(cells, c) == 1).OrderByDescending(c => dist[c]).ToList();
        var kinds = new Dictionary<(int, int), RoomKind> { [start] = RoomKind.Start };
        if (dead.Count > 0)
            kinds[dead[0]] = IsBossFloor ? RoomKind.Boss : RoomKind.Exit;
        var rest = dead.Skip(1).ToList();
        rng.Shuffle(rest);
        var specials = new List<RoomKind> { RoomKind.Treasure, RoomKind.Shop };
        if (wantShrine)
            specials.Add(RoomKind.Shrine);
        foreach (var k in specials)
        {
            if (rest.Count == 0) break;
            kinds[rest[^1]] = k;
            rest.RemoveAt(rest.Count - 1);
        }
        foreach (var c in cells)
        {
            var room = new Room(c.x, c.y, kinds.TryGetValue(c, out var k) ? k : RoomKind.Combat, (int)rng.NextUInt()) { Dist = dist[c] };
            Rooms[c] = room;
        }
        foreach (var (c, room) in Rooms)
            foreach (var d in Dirs.All)
            {
                var (dx, dy) = Dirs.Delta(d);
                if (Rooms.TryGetValue((c.Item1 + dx, c.Item2 + dy), out var n))
                    room.Doors[d] = n;
            }
        Start = Rooms[start];

        foreach (var room in Rooms.Values)
        {
            switch (room.Kind)
            {
                case RoomKind.Combat:
                {
                    Layouts.Make(room, rng);
                    float budget = Balance.RoomBudget(Depth, rng.NextFloat(), room.Dist) * diff.EnemyCount * countMult;
                    if (budget > 9)
                    {
                        float b1 = budget * 0.6f;
                        room.Waves.Add(RollWave(rng, Biome, b1));
                        room.Waves.Add(RollWave(rng, Biome, budget - b1));
                    }
                    else
                        room.Waves.Add(RollWave(rng, Biome, budget));
                    break;
                }
                case RoomKind.Exit:
                {
                    Layouts.Make(room, rng);
                    float budget = Balance.RoomBudget(Depth, 1, 3) * 1.2f * diff.EnemyCount * countMult;
                    room.Waves.Add(RollWave(rng, Biome, budget * 0.55f));
                    var last = RollWave(rng, Biome, budget * 0.45f);
                    // strażnik: najdroższy wróg krainy jako elita (na 3. piętrze krainy – mini-boss)
                    var guardian = Biome.Pool.OrderByDescending(p => Content.Db.EnemyByKey[p.Enemy].Cost).First().Enemy;
                    last.Insert(0, (IsMiniBossFloor ? "!!" : "!") + guardian);
                    room.Waves.Add(last);
                    break;
                }
                case RoomKind.Treasure:
                case RoomKind.Shrine:
                    foreach (var (c, r) in new[] { (2, 1), (C.GCols - 3, 1), (2, C.GRows - 2), (C.GCols - 3, C.GRows - 2) })
                        room.Grid[r, c] = new Obstacle(ObstacleKind.Rock, c, r);
                    break;
            }
        }
        Start.Visited = Start.Seen = true;
        foreach (var n in Start.Doors.Values)
            n.Seen = true;
    }

    /// <summary>Losuje skład fali w ramach budżetu (koszt wrogów) z puli krainy.</summary>
    public static List<string> RollWave(Rng rng, BiomeDef biome, float budget)
    {
        var db = Content.Db;
        var pool = biome.Pool;
        var weights = pool.Select(p => p.Weight).ToList();
        var outList = new List<string>();
        int guard = 0;
        while (budget > 0.5f && guard++ < 60)
        {
            var k = pool[rng.Weighted(weights)].Enemy;
            float c = db.EnemyByKey[k].Cost;
            if (c > budget + 0.5f && outList.Count > 0)
            {
                var cheap = pool.Where(p => db.EnemyByKey[p.Enemy].Cost <= budget + 0.5f).ToList();
                if (cheap.Count == 0)
                    break;
                k = rng.Pick(cheap).Enemy;
                c = db.EnemyByKey[k].Cost;
            }
            outList.Add(k);
            budget -= c;
        }
        return outList;
    }
}

/// <summary>Układy przeszkód w pokojach walki (symetryczne, z zachowaniem spójności podłogi).</summary>
public static class Layouts
{
    public static void Make(Room room, Rng rng)
    {
        for (int attempt = 0; attempt < 15; attempt++)
        {
            room.Grid = new Obstacle[C.GRows, C.GCols];
            Layout(room, rng);
            if (Connected(room))
                return;
        }
        room.Grid = new Obstacle[C.GRows, C.GCols];
    }

    private static void Layout(Room room, Rng rng)
    {
        string[] pats = { "scatter", "pillars", "walls", "box", "diag", "empty", "scatter", "pillars", "crates" };
        var pat = rng.Pick(pats);
        var cells = new HashSet<(int c, int r)>();
        int hc = C.GCols / 2 + 1, hr = C.GRows / 2;
        switch (pat)
        {
            case "scatter":
                for (int i = rng.RangeInt(3, 8); i > 0; i--)
                    cells.Add((rng.RangeInt(1, hc - 1), rng.RangeInt(1, hr - 1)));
                break;
            case "pillars":
                for (int i = rng.RangeInt(1, 3); i > 0; i--)
                {
                    int c = rng.RangeInt(2, hc - 3), r = rng.RangeInt(1, hr - 2);
                    cells.Add((c, r)); cells.Add((c + 1, r)); cells.Add((c, r + 1)); cells.Add((c + 1, r + 1));
                }
                break;
            case "walls":
            {
                int r = rng.RangeInt(2, hr - 1), c0 = rng.RangeInt(3, 7);
                for (int c = c0; c < Math.Min(hc - 1, c0 + rng.RangeInt(3, 6)); c++)
                    cells.Add((c, r));
                break;
            }
            case "box":
            {
                int c0 = rng.RangeInt(6, 8), r0 = rng.RangeInt(2, 3);
                for (int c = c0; c < hc; c++) cells.Add((c, r0));
                for (int r = r0; r < hr; r++) cells.Add((c0, r));
                break;
            }
            case "diag":
            {
                int c = rng.RangeInt(4, 7), r = 1;
                while (r < hr && c < hc) cells.Add((c++, r++));
                break;
            }
        }
        foreach (var (c, r) in cells)
            foreach (var (cc, rr) in new[] { (c, r), (C.GCols - 1 - c, r), (c, C.GRows - 1 - r), (C.GCols - 1 - c, C.GRows - 1 - r) })
                if (cc >= 0 && cc < C.GCols && rr >= 0 && rr < C.GRows && !Room.Forbidden(cc, rr))
                    room.Grid[rr, cc] = new Obstacle(ObstacleKind.Rock, cc, rr, rng.RangeInt(0, 1));
        int extra = rng.RangeInt(0, 3) + (pat == "crates" ? 4 : 0);
        for (int i = 0; i < extra; i++)
        {
            int c = rng.RangeInt(1, C.GCols - 2), r = rng.RangeInt(1, C.GRows - 2);
            if (room.Grid[r, c] == null && !Room.Forbidden(c, r) && Math.Abs(c - C.GCols / 2) + Math.Abs(r - C.GRows / 2) > 2)
                room.Grid[r, c] = new Obstacle(rng.Chance(0.67f) ? ObstacleKind.Crate : ObstacleKind.Barrel, c, r);
        }
    }

    private static bool Connected(Room room)
    {
        var free = room.FreeTiles();
        if (free.Count == 0 || room.Grid[5, 2] != null)
            return false;
        var seen = new HashSet<(int, int)> { (2, 5) };
        var q = new Queue<(int c, int r)>();
        q.Enqueue((2, 5));
        while (q.Count > 0)
        {
            var (c, r) = q.Dequeue();
            foreach (var (dc, dr) in Room.Steps4)
            {
                int nc = c + dc, nr = r + dr;
                if (nc >= 0 && nc < C.GCols && nr >= 0 && nr < C.GRows && room.Grid[nr, nc] == null && seen.Add((nc, nr)))
                    q.Enqueue((nc, nr));
            }
        }
        return seen.Count == free.Count;
    }
}
