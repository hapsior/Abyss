using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Otchlan.Core;

namespace Otchlan.Sim;

public enum RunState { Play, Transition, Descend, Dying }

public sealed class RunStats
{
    public int Kills, Rooms, Coins, Bosses, Elites, Shrines, Bought, Dashes, Abilities, ItemsTaken;
    public float DamageTaken, DamageDealt, Spent, GodHits, GodDmg;
}

public sealed class SpawnMarker
{
    public string Key;
    public float X, Y, T, Dur;
    public bool Elite, MiniBoss;
}

public enum RunEventKind { FloorEntered, RoomEntered, RoomCleared, LevelUp, PlayerDown, PlayerRevived, GameOver, BossDefeated, StoryComplete, ShopOpened, ItemTaken, Achievement }

public readonly record struct RunEvent(RunEventKind Kind, PlayerSim Player = null, string Data = null);

/// <summary>
/// Stan jednej wyprawy (port klasy Run z wersji 1.x + co-op 1.1). Czysta symulacja bez rysowania:
/// widok czyta stan i zdarzenia (<see cref="Events"/>, <see cref="Fx"/>). Działa też bez okna (testy, boty, balans).
/// </summary>
public sealed class RunSim
{
    public readonly List<PlayerSim> Players = new();
    public bool Coop => Players.Count > 1;
    public DifficultyDef Diff;
    public int DiffIdx, Seal;
    public Rng R;
    /// <summary>Losowość kosmetyczna (cząsteczki itp.) – nie wpływa na rozgrywkę ani zapis.</summary>
    /// <summary>RNG kosmetyczny (cząsteczki, rozrzut znajdziek, animacje). Ziarno z ziarna wyprawy – symulacja bez okna jest powtarzalna.</summary>
    public readonly Random Cos;
    public IFx Fx = NullFx.I;
    public bool God, DamageNumbers = true;

    public int Depth, MaxDepth;
    public Floor Floor;
    public Room Room;
    public Room PrevRoom;
    public readonly List<Enemy> Enemies = new();
    public readonly List<Bullet> Bullets = new();
    public readonly List<Effect> Effects = new();
    public readonly List<SpawnMarker> Spawns = new();
    public Boss Boss;
    public int Coins;
    public float Time, RunTime, Hitstop, FrameDt, StateT, TrapDelay, FlowT;
    public RunState State = RunState.Play;
    public Dir TransDir;
    public bool StoryDone;
    public int WaveIdx;
    public readonly HashSet<string> Taken = new();
    public readonly RunStats Stats = new();
    public readonly Queue<RunEvent> Events = new();
    private readonly Dictionary<PlayerSim, int[,]> _flows = new();
    private readonly Dictionary<Enemy, (string kind, float until)> _tokens = new();
    private bool _descended;

    // ================================================================== tworzenie
    public RunSim(IList<string> charKeys, int diffIdx, ulong seed, int seal = 0, bool startFloor = true)
    {
        var db = Content.Db;
        DiffIdx = diffIdx;
        Diff = db.Difficulties[diffIdx];
        Seal = seal;
        R = new Rng(seed);
        Cos = new Random(unchecked((int)(seed ^ (seed >> 32)) ^ 0x5bd1e995));
        for (int i = 0; i < charKeys.Count; i++)
        {
            var p = new PlayerSim(db.CharByKey[charKeys[i]], i);
            float hpMod = SealMod("start_hp");
            if (hpMod != 1)
            {
                p.MaxHp = MathF.Round(p.MaxHp * hpMod);
                p.Hp = p.MaxHp;
            }
            Players.Add(p);
        }
        if (startFloor)
            NewFloor(1);
    }

    public IEnumerable<PlayerSim> ActivePlayers() => Players.Where(p => p.Active);

    public float CosRange(float a, float b) => a + (float)Cos.NextDouble() * (b - a);

    /// <summary>Iloczyn modyfikatorów aktywnych Pieczęci Otchłani dla danej wielkości (1 = brak zmian).</summary>
    public float SealMod(string name)
    {
        float m = 1;
        var seals = Content.Db.Seals;
        for (int i = 0; i < Math.Min(Seal, seals.Count); i++)
            if (seals[i].Mods.TryGetValue(name, out var v))
                m *= v;
        return m;
    }

    public float CoopHpMult() => Coop ? Balance.CoopEnemyHp : 1;
    public float CoopBossMult() => Coop ? Balance.CoopBossHp : 1;

    // ================================================================== piętra / pokoje
    public void NewFloor(int depth)
    {
        Depth = depth;
        MaxDepth = Math.Max(MaxDepth, depth);
        Floor = Floor.Generate(depth, Diff, R, Coop ? Balance.CoopEnemyCount : 1);
        int i = 0;
        foreach (var p in Players)
        {
            p.X = C.RCX + (Coop ? (i == 0 ? -24 : 24) : 0);
            p.Y = C.RCY;
            p.HitThisFloor = false;
            i++;
        }
        EnterRoom(Floor.Start, null);
        var b = Floor.Biome;
        string sub = depth > C.StoryFloors ? $"Głębia · {b.Name}" : b.Name;
        Fx.Banner($"Piętro {depth}", sub, b.AccentColor);
        Events.Enqueue(new RunEvent(RunEventKind.FloorEntered));
    }

    public void EnterRoom(Room room, Dir? cameThrough)
    {
        bool first = !room.Visited;
        PrevRoom = Room;
        Room = room;
        room.Visited = room.Seen = true;
        foreach (var n in room.Doors.Values)
            n.Seen = true;
        Bullets.Clear();
        Effects.Clear();
        Enemies.Clear();
        Spawns.Clear();
        _tokens.Clear();
        Boss = null;
        if (cameThrough is { } ct)
        {
            var ent = Dirs.Opposite(ct);
            var basePos = ent switch
            {
                Dir.W => new Vector2(C.RX0 + 46, C.RCY),
                Dir.E => new Vector2(C.RX1 - 46, C.RCY),
                Dir.N => new Vector2(C.RCX, C.RY0 + 46),
                _ => new Vector2(C.RCX, C.RY1 - 46),
            };
            var side = ent is Dir.W or Dir.E ? new Vector2(0, 30) : new Vector2(30, 0);
            int i = 0;
            foreach (var p in Players)
            {
                var pos = basePos + (Coop ? side * (i == 0 ? -1 : 1) : Vector2.Zero);
                p.X = pos.X;
                p.Y = pos.Y;
                p.Vx *= 0.3f;
                p.Vy *= 0.3f;
                i++;
            }
        }
        foreach (var p in Players)
        {
            p.DashT = 0;
            p.Trail.Clear();
            p.Trail.Add(new Vector2(p.X, p.Y));
            foreach (var f in p.Fams) { f.X = p.X; f.Y = p.Y; }
            p.HitThisRoom = false;
        }
        if (first) Stats.Rooms++;
        if (!room.Cleared)
        {
            foreach (var p in Players)
                if (p.RoomShield > 0) p.ShieldUp = true;
            if (room.Kind is RoomKind.Combat or RoomKind.Exit or RoomKind.Boss)
                foreach (var p in ActivePlayers())
                    if (p.WarHorn > 0)
                    {
                        p.RageT = MathF.Max(p.RageT, 2.5f * p.WarHorn);
                        Fx.Ring(p.X, p.Y, new Color("ff4632"), 90, 0.35f, 5);
                    }
            if (room.Kind is RoomKind.Combat or RoomKind.Exit)
            {
                WaveIdx = 0;
                if (room.Waves.Count > 0) SpawnWave(room.Waves[0]);
                Fx.Sound("door", 0.8f);
            }
            else if (room.Kind == RoomKind.Boss)
            {
                var def = Content.Db.BossByKey[Floor.Biome.Boss];
                var b = SpawnBoss(def, C.RCX, C.RCY - 90);
                Fx.Banner(b.Name, b.Title, new Color("ff5050"), true);
                Fx.Sound("boss_roar");
                Fx.Sound("door", 0.8f);
            }
        }
        if (first && !room.RewardGiven)
        {
            room.RewardGiven = true;
            PopulateSpecial(room);
        }
        RecomputeFlows();
        Events.Enqueue(new RunEvent(RunEventKind.RoomEntered));
    }

    public Boss SpawnBoss(BossDef def, float x, float y)
    {
        var e = EnemyFactory.Create(def.Key);
        if (e is not Boss b)
        {
            GD.PushWarning($"Brak klasy bossa '{def.Key}' – zastępczy Król Glutów");
            b = (Boss)EnemyFactory.Create("slime_king");
            def = Content.Db.BossByKey["slime_king"] is { } sk ? new BossDef
            {
                Key = def.Key, Cls = sk.Cls, Name = def.Name, Title = def.Title, Color = def.Color, Radius = sk.Radius, Speed = sk.Speed,
                Contact = sk.Contact, HpMult = def.HpMult, Flying = sk.Flying, Attacks = sk.Attacks, Phases = sk.Phases, Rest = sk.Rest,
            } : def;
        }
        b.InitBoss(this, def, x, y);
        Enemies.Add(b);
        Boss = b;
        return b;
    }

    /// <summary>Pozycja sklepikarza (rozmowa otwiera menu sklepu).</summary>
    public static readonly Vector2 ShopkeeperPos = new(C.RCX, C.RCY - 130);

    private void PopulateSpecial(Room room)
    {
        float pm = Diff.Price * Players.Min(p => p.PriceMult);
        switch (room.Kind)
        {
            case RoomKind.Treasure:
                if (Coop)
                {
                    room.Pedestals.Add(new Pedestal(C.RCX - 80, C.RCY - 10, RollNewItem(), 0, PedestalKind.Item));
                    room.Pedestals.Add(new Pedestal(C.RCX + 80, C.RCY - 10, RollNewItem(), 0, PedestalKind.Item));
                }
                else
                    room.Pedestals.Add(new Pedestal(C.RCX, C.RCY - 10, RollNewItem(), 0, PedestalKind.Item));
                break;
            case RoomKind.Shop:
                foreach (var x in new[] { C.RCX - 240, C.RCX - 80, C.RCX + 80 })
                {
                    var it = RollNewItem();
                    room.Pedestals.Add(new Pedestal(x, C.RCY + 40, it, Items.Price(it, Depth, pm), PedestalKind.Shop));
                }
                room.Pedestals.Add(new Pedestal(C.RCX + 240, C.RCY + 40, null, Math.Max(3, (int)MathF.Round(6 * pm)), PedestalKind.HeartShop, heal: 0.35f));
                break;
            case RoomKind.Shrine:
            {
                int cost = DiffIdx < 3 ? 20 : 25;
                foreach (var x in new[] { C.RCX - 120, C.RCX + 120 })
                    room.Pedestals.Add(new Pedestal(x, C.RCY + 10, RollNewItem(2), 0, PedestalKind.Shrine, group: 1, hpCost: cost));
                break;
            }
        }
        int id = 1;
        foreach (var pd in room.Pedestals) pd.Id = id++;
    }

    public ItemDef RollNewItem(int minRarity = 0, PlayerSim forPlayer = null)
    {
        var taken = new HashSet<string>(Taken);
        foreach (var p in Players)
            taken.UnionWith(p.Items);
        float luck = forPlayer?.Luck ?? Players.Max(p => p.Luck);
        var it = Items.RollItem(R, taken, luck, Depth, minRarity);
        Taken.Add(it.Key);
        return it;
    }

    public void GiveRandomItems(int n, PlayerSim p)
    {
        for (int i = 0; i < n; i++)
        {
            var it = RollNewItem(0, p);
            p.ApplyItem(it, this);
            Fx.Banner(it.Name, it.Desc, C.RarityColors[it.Rarity]);
            Stats.ItemsTaken++;
        }
    }

    public void SpawnWave(List<string> keys)
    {
        var free = Room.FreeTiles();
        R.Shuffle(free);
        float eliteCh = Diff.Elite > 0 ? Diff.Elite + Balance.EliteChancePerFloor * (Depth - 1) : 0;
        var used = new HashSet<(int, int)>();
        int idx = 0;
        foreach (var raw in keys)
        {
            bool forcedElite = raw.StartsWith("!"), mini = raw.StartsWith("!!");
            string k = raw.TrimStart('!');
            Vector2? pos = null;
            while (idx < free.Count)
            {
                var (c, r) = free[idx++];
                var cc = Room.CellCenter(c, r);
                if (Players.Any(p => p.Active && cc.DistanceTo(new Vector2(p.X, p.Y)) < 260) || used.Contains((c, r)))
                    continue;
                if ((c <= 1 || c >= C.GCols - 2 || r <= 0 || r >= C.GRows - 1) && R.Chance(0.7f))
                    continue;
                pos = cc;
                used.Add((c, r));
                break;
            }
            pos ??= new Vector2(R.Range(C.RX0 + 60, C.RX1 - 60), R.Range(C.RY0 + 60, C.RY1 - 60));
            bool elite = forcedElite || R.Chance(eliteCh);
            Spawns.Add(new SpawnMarker { Key = k, X = pos.Value.X, Y = pos.Value.Y, Elite = elite, MiniBoss = mini, T = R.Range(0.75f, 1.15f) });
            Spawns[^1].Dur = Spawns[^1].T;
        }
        Fx.Sound("spawn", 0.5f);
    }

    public Enemy SpawnEnemy(string key, float x, float y, bool minion = false, bool elite = false, bool miniBoss = false)
    {
        if (!Content.Db.EnemyByKey.TryGetValue(key, out var def) || Enemies.Count > 45)
            return null;
        x = C.Clamp(x, C.RX0 + 20, C.RX1 - 20);
        y = C.Clamp(y, C.RY0 + 20, C.RY1 - 20);
        var e = EnemyFactory.Create(key);
        e.Init(this, def, x, y, elite, minion, miniBoss);
        if (!(e.Flying || e.Ghost))
        {
            var p = Room.ResolveCircle(e.X, e.Y, e.R);
            e.X = p.X;
            e.Y = p.Y;
        }
        Enemies.Add(e);
        Fx.Burst(e.X, e.Y, new Color("c8c8dc"), 8, 120, 4, 0.35f, kind: "smoke");
        return e;
    }

    /// <summary>Przejście przez drzwi (gdy pokój czysty). Zwraca true, jeśli zaczęło się przejście.</summary>
    public bool CheckDoors(float nx, float ny, PlayerSim p)
    {
        if (!Room.Cleared || State != RunState.Play)
            return false;
        float r = p.R;
        foreach (var (d, nroom) in Room.Doors)
        {
            bool go = d switch
            {
                Dir.W => nx - r < C.RX0 + 1 && MathF.Abs(ny - C.RCY) < C.DoorHalf - 4 && p.Vx < 0,
                Dir.E => nx + r > C.RX1 - 1 && MathF.Abs(ny - C.RCY) < C.DoorHalf - 4 && p.Vx > 0,
                Dir.N => ny - r < C.RY0 + 1 && MathF.Abs(nx - C.RCX) < C.DoorHalf - 4 && p.Vy < 0,
                _ => ny + r > C.RY1 - 1 && MathF.Abs(nx - C.RCX) < C.DoorHalf - 4 && p.Vy > 0,
            };
            if (!go) continue;
            TransDir = d;
            EnterRoom(nroom, d);
            State = RunState.Transition;
            StateT = 0;
            return true;
        }
        return false;
    }

    // ================================================================== zdarzenia
    public void OnLevelUp(PlayerSim p)
    {
        p.PendingLevelUps++;
        Fx.Sound("levelup", 0.8f);
        Fx.Text(p.X, p.Y - 50, "AWANS!", new Color("8cff8c"), 26, 1.2f);
        Fx.Ring(p.X, p.Y, new Color("8cff8c"), 70, 0.5f);
        p.Heal(p.MaxHp * 0.05f, this, false);
        Events.Enqueue(new RunEvent(RunEventKind.LevelUp, p));
    }

    public void OnPlayerDown(PlayerSim p)
    {
        if (Coop && Players.Any(o => o != p && o.Active))
        {
            p.Downed = true;
            p.ReviveP = 0;
            p.Vx = p.Vy = 0;
            Fx.Sound("player_hurt");
            Fx.Text(p.X, p.Y - 40, "POWALONY!", new Color("ff6464"), 24);
            Events.Enqueue(new RunEvent(RunEventKind.PlayerDown, p));
            return;
        }
        p.Dead = true;
        foreach (var o in Players)
            if (o.Downed) { o.Downed = false; o.Dead = true; }
        State = RunState.Dying;
        StateT = 0;
        Fx.Sound("gameover");
        Fx.Burst(p.X, p.Y, p.Def.BodyColor, 40, 400, 7, 1.0f);
        Events.Enqueue(new RunEvent(RunEventKind.GameOver, p));
    }

    // ================================================================== cele i nawigacja
    public PlayerSim NearestPlayer(float x, float y)
    {
        PlayerSim best = null;
        float bd = float.MaxValue;
        foreach (var p in Players)
        {
            if (!p.Active) continue;
            float d = (p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y);
            if (d < bd) { bd = d; best = p; }
        }
        return best;
    }

    /// <summary>Wybór celu dla wroga: najbliższy aktywny gracz, zmiana tylko gdy inny jest wyraźnie bliżej.</summary>
    public PlayerSim TargetPlayerFor(Enemy e, PlayerSim current)
    {
        var near = NearestPlayer(e.X, e.Y);
        if (current == null || !current.Active || near == null)
            return near ?? Players.FirstOrDefault();
        float dc = (current.X - e.X) * (current.X - e.X) + (current.Y - e.Y) * (current.Y - e.Y);
        float dn = (near.X - e.X) * (near.X - e.X) + (near.Y - e.Y) * (near.Y - e.Y);
        return dn < dc * 0.49f ? near : current; // ~30% bliżej
    }

    public Vector2 TargetPos(Enemy e = null)
    {
        var p = e != null ? e.Target(this) : NearestPlayer(C.RCX, C.RCY);
        if (p == null)
            return C.RoomCenter;
        if (p.InvisT > 0 && p.Decoy is { } d)
            return d;
        return new Vector2(p.X, p.Y);
    }

    public Enemy NearestEnemy(float x, float y, float maxd = 9999, HashSet<int> exclude = null)
    {
        Enemy best = null;
        float bd = maxd * maxd;
        foreach (var e in Enemies)
        {
            if (!e.Alive || e.Invuln || exclude != null && exclude.Contains(e.Id)) continue;
            float d = (e.X - x) * (e.X - x) + (e.Y - y) * (e.Y - y);
            if (d < bd) { bd = d; best = e; }
        }
        return best;
    }

    private void RecomputeFlows()
    {
        _flows.Clear();
        foreach (var p in Players)
        {
            var t = p.InvisT > 0 && p.Decoy is { } d ? d : new Vector2(p.X, p.Y);
            _flows[p] = Room.FlowField(t.X, t.Y);
        }
    }

    /// <summary>Kierunek pościgu: wprost, gdy widać cel; inaczej po polu odległości (omijanie przeszkód).</summary>
    public Vector2 ChaseDir(Enemy e)
    {
        var t = TargetPos(e);
        float dx = t.X - e.X, dy = t.Y - e.Y, d = MathF.Max(1, MathF.Sqrt(dx * dx + dy * dy));
        var tp = e.Target(this);
        if (e.Flying || e.Ghost || tp == null || !_flows.TryGetValue(tp, out var flow) || Room.Los(e.X, e.Y, t.X, t.Y))
            return new Vector2(dx / d, dy / d);
        int c = Room.ColOf(e.X), r = Room.RowOf(e.Y);
        if (c < 0 || c >= C.GCols || r < 0 || r >= C.GRows)
            return new Vector2(dx / d, dy / d);
        int best = flow[r, c];
        (int c, int r)? bc = null;
        foreach (var (dc, dr) in Room.Steps8)
        {
            int nc = c + dc, nr = r + dr;
            if (nc < 0 || nc >= C.GCols || nr < 0 || nr >= C.GRows) continue;
            if (dc != 0 && dr != 0 && (Room.Solid(c + dc, r) || Room.Solid(c, r + dr))) continue;
            if (flow[nr, nc] < best) { best = flow[nr, nc]; bc = (nc, nr); }
        }
        if (bc == null)
            return new Vector2(dx / d, dy / d);
        var tc = Room.CellCenter(bc.Value.c, bc.Value.r);
        return (tc - new Vector2(e.X, e.Y)).Normalized();
    }

    /// <summary>
    /// SI 1.1: ścieżka (BFS po wolnych polach od wroga) do najbliższego pola, z którego „gruba” linia
    /// do celu jest czysta i odległość mieści się w [minD, maxD]. null = nie znaleziono.
    /// </summary>
    public List<Vector2> FiringPath(Enemy e, float minD, float maxD, int maxSteps = 12)
    {
        var t = TargetPos(e);
        int sc = Math.Clamp(Room.ColOf(e.X), 0, C.GCols - 1), sr = Math.Clamp(Room.RowOf(e.Y), 0, C.GRows - 1);
        var parent = new Dictionary<(int, int), (int, int)>();
        var steps = new Dictionary<(int, int), int> { [(sc, sr)] = 0 };
        var q = new Queue<(int c, int r)>();
        q.Enqueue((sc, sr));
        var cands = new List<((int c, int r) cell, float score)>();
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            int s = steps[cur];
            var cc = Room.CellCenter(cur.c, cur.r);
            float d = cc.DistanceTo(t);
            float off = d < minD ? minD - d : d > maxD ? d - maxD : 0;
            cands.Add((cur, off * 1.0f + s * C.Tile * 0.5f));
            if (s >= maxSteps) continue;
            foreach (var (dc, dr) in Room.Steps4)
            {
                var n = (cur.c + dc, cur.r + dr);
                if (n.Item1 < 0 || n.Item1 >= C.GCols || n.Item2 < 0 || n.Item2 >= C.GRows || steps.ContainsKey(n)) continue;
                if (!e.Flying && !e.Ghost && Room.Solid(n.Item1, n.Item2)) continue;
                steps[n] = s + 1;
                parent[n] = cur;
                q.Enqueue(n);
            }
        }
        cands.Sort((a, b) => a.score.CompareTo(b.score));
        int checks = 0;
        foreach (var (cell, _) in cands)
        {
            if (++checks > 260) break;
            var cc = Room.CellCenter(cell.c, cell.r);
            float d = cc.DistanceTo(t);
            if (d < minD * 0.6f || d > maxD * 1.15f) continue;
            if (!Room.LosThick(cc.X, cc.Y, t.X, t.Y)) continue;
            var path = new List<Vector2>();
            var k = cell;
            while (k != (sc, sr))
            {
                path.Add(Room.CellCenter(k.c, k.r));
                k = parent[k];
            }
            path.Reverse();
            return path;
        }
        return null;
    }

    /// <summary>Losowa wolna pozycja z dala od graczy (opcjonalnie z linią strzału do celu wroga).</summary>
    public Vector2 RandomFreePos(float minDist = 200, bool fly = false, float margin = 50, Enemy needLos = null, float maxDist = 99999)
    {
        for (int i = 0; i < 60; i++)
        {
            float x = R.Range(C.RX0 + margin, C.RX1 - margin), y = R.Range(C.RY0 + margin, C.RY1 - margin);
            bool ok = true;
            foreach (var p in ActivePlayers())
            {
                float d = MathF.Sqrt((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y));
                if (d < minDist) ok = false;
            }
            if (!ok) continue;
            if (!fly && Room.TileAt(x, y) != null) continue;
            if (needLos != null)
            {
                var t = TargetPos(needLos);
                if (new Vector2(x, y).DistanceTo(t) > maxDist || !Room.LosThick(x, y, t.X, t.Y)) continue;
            }
            return new Vector2(x, y);
        }
        return C.RoomCenter;
    }

    // ================================================================== żetony ataku
    public bool TakeToken(Enemy e, string kind, float hold = 1.0f)
    {
        foreach (var k in _tokens.Where(kv => kv.Value.until < Time || !kv.Key.Alive).Select(kv => kv.Key).ToList())
            _tokens.Remove(k);
        if (_tokens.TryGetValue(e, out var cur) && cur.kind == kind)
            return true;
        int di = Math.Clamp(DiffIdx, 0, 3);
        int limit = (kind == "charge" ? Balance.ChargeTokens[di] : Balance.AttackTokens[di]) + (Coop ? Balance.CoopTokens : 0);
        if (e.Elite) limit++;
        int used = _tokens.Count(kv => kv.Value.kind == kind);
        if (used >= limit)
            return false;
        _tokens[e] = (kind, Time + hold);
        e.HeldToken = kind;
        return true;
    }

    public void ReleaseToken(Enemy e)
    {
        _tokens.Remove(e);
        e.HeldToken = null;
    }

    /// <summary>Najbliższy punkt (środek wolnej komórki siatki) do (x, y), gdy sam punkt jest w przeszkodzie.</summary>
    public Vector2 FreeSpotNear(float x, float y)
    {
        if (Room.TileAt(x, y) is not { Solid: true }) return new Vector2(x, y);
        int c0 = Math.Clamp(Room.ColOf(x), 0, C.GCols - 1), r0 = Math.Clamp(Room.RowOf(y), 0, C.GRows - 1);
        for (int rad = 1; rad < Math.Max(C.GCols, C.GRows); rad++)
        {
            Vector2? best = null;
            float bd = float.MaxValue;
            for (int r = r0 - rad; r <= r0 + rad; r++)
                for (int c = c0 - rad; c <= c0 + rad; c++)
                {
                    if (Math.Max(Math.Abs(r - r0), Math.Abs(c - c0)) != rad) continue;
                    if (c < 0 || c >= C.GCols || r < 0 || r >= C.GRows || Room.Solid(c, r)) continue;
                    var cc = Room.CellCenter(c, r);
                    float d = cc.DistanceSquaredTo(new Vector2(x, y));
                    if (d < bd) { bd = d; best = cc; }
                }
            if (best is { } b) return b;
        }
        return new Vector2(x, y);
    }

    public void ClearEnemyBullets()
    {
        foreach (var b in Bullets)
            if (!b.Friendly) b.Kill(this);
    }

    // ================================================================== obrażenia
    /// <summary>Limit DPS bossa (Gungeon): nadmiar ponad limit w oknie 3 s zadaje tylko 25%.</summary>
    public float BossDamageFilter(Boss b, float dmg)
    {
        if (b.Intro > 0)
            return dmg * Balance.BossIntroArmor;
        float cap = Balance.BossDpsCap(Depth) * (Coop ? Balance.CoopBossCap : 1);
        while (b.DmgLog.Count > 0 && b.DmgLog.Peek().t < Time - Balance.BossCapWindow)
            b.DmgRecent -= b.DmgLog.Dequeue().dmg;
        dmg = MathF.Min(dmg, cap * Balance.BossCapMaxHit);
        float budget = cap * Balance.BossCapWindow - b.DmgRecent;
        float real = budget >= dmg ? dmg : MathF.Max(0, budget) + (dmg - MathF.Max(0, budget)) * Balance.BossCapOverflow;
        b.DmgLog.Enqueue((Time, real));
        b.DmgRecent += real;
        return real;
    }

    public void DamageEnemy(Enemy e, float dmg, bool crit = false, Color? color = null, bool small = false, float kx = 0, float ky = 0,
        bool dot = false, PlayerSim owner = null, string source = null)
    {
        if (!e.Alive || e.Invuln || dmg <= 0) return;
        bool capped = false;
        if (e is Boss b)
        {
            float f = BossDamageFilter(b, dmg);
            capped = f < dmg * 0.99f;
            dmg = f;
        }
        e.Hp -= dmg;
        if (e.FlashCd <= 0)
        {
            e.Flash = dot ? 0.04f : 0.07f;
            e.FlashCd = e.IsBoss ? 0.3f : 0.11f;
        }
        Stats.DamageDealt += dmg;
        if (DamageNumbers)
        {
            float ox = CosRange(-8, 8);
            if (crit)
                Fx.Text(e.X + ox, e.Y - e.R - 6, $"{(int)dmg}!", new Color("ffdc46"), 24, 0.75f);
            else
                Fx.Text(e.X + ox, e.Y - e.R - 6, $"{Math.Max(1, (int)MathF.Round(dmg))}", capped ? new Color("9a96a8") : color ?? C.White, small ? 14 : 17, 0.6f);
        }
        if (kx != 0 || ky != 0) e.Knock(kx, ky);
        if (e.Hp <= 0)
            KillEnemy(e);
        else if (!dot)
            Fx.Sound(crit ? "crit" : "hit", 0.45f, 0.04f);
    }

    public void KillEnemy(Enemy e, bool drops = true)
    {
        if (!e.Alive && !Enemies.Contains(e)) return;
        e.Alive = false;
        Enemies.Remove(e);
        ReleaseToken(e);
        Fx.Burst(e.X, e.Y, e.Color, e.IsBoss ? 60 : 16, 280, 6, 0.6f);
        Fx.Burst(e.X, e.Y, C.White, 4, 150, 3, 0.3f);
        Fx.Stain(e.X, e.Y + e.R * 0.4f, e.Color, e.R * 0.9f);
        Room.Stains.Add((new Vector2(e.X, e.Y + e.R * 0.4f), e.Color, e.R * 0.9f));
        Fx.Sound("enemy_die", 0.6f, 0.03f);
        Stats.Kills++;
        if (e.Elite) Stats.Elites++;
        if (drops && !e.Minion && !e.IsBoss)
        {
            float xpv = e.Xp * (1 + 0.12f * (Depth - 1)) * (e.MiniBoss ? 8 : e.Elite ? 3 : 1);
            while (xpv > 0)
            {
                float v = xpv >= 8 ? MathF.Min(8, xpv) : xpv >= 3 ? MathF.Min(3, xpv) : xpv;
                Room.Pickups.Add(new Pickup(PickupKind.Xp, e.X, e.Y, Cos, v));
                xpv -= v;
            }
            float coinMult = Players.Max(p => p.CoinMult);
            float ch = e.Coin * coinMult;
            int n = (int)ch + (R.NextFloat() < ch - (int)ch ? 1 : 0) + (e.MiniBoss ? 8 : e.Elite ? 3 : 0);
            for (int i = 0; i < n; i++)
                Room.Pickups.Add(new Pickup(PickupKind.Coin, e.X, e.Y, Cos));
            if (R.NextFloat() < 0.025f * Diff.Heal || e.MiniBoss)
                Room.Pickups.Add(new Pickup(PickupKind.Heart, e.X, e.Y, Cos));
        }
        if (e.PoisonT > 0 && !e.IsBoss)
            foreach (var p in Players)
                if (p.PoisonBurst > 0)
                {
                    Effects.Add(new PoisonCloud(e.X, e.Y, 70, p.EffDamage() * 0.9f, 2.5f));
                    break;
                }
        e.OnDeath(this);
        if (e is Boss boss)
            BossDefeated(boss);
    }

    private void BossDefeated(Boss boss)
    {
        Stats.Bosses++;
        Boss = null;
        Hitstop = 0.35f;
        Fx.Shake(18);
        Fx.Sound("explosion");
        Fx.Sound("victory", 0.7f);
        for (int i = 0; i < 4; i++)
            Fx.Ring(boss.X, boss.Y, new Color("ffdc78"), 120 + i * 80, 0.5f + i * 0.15f, 8);
        Fx.Burst(boss.X, boss.Y, new Color("ffe696"), 50, 600, 8, 1.2f, glow: true);
        ClearEnemyBullets();
        foreach (var ef in Effects)
            if (ef is Telegraph or HazardZone or FirePool) ef.Alive = false;
        foreach (var e in Enemies.ToList())
            KillEnemy(e, false);
        Room.Cleared = true;
        foreach (var p in Players)
            if (p.Downed) p.Revive(this, 0.5f);
        PlaceTrapdoor(new Vector2(C.RCX, C.RCY + 110));
        TrapDelay = 1.6f;
        int nItems = Coop ? 2 : 1;
        for (int i = 0; i < nItems; i++)
            Room.Pedestals.Add(new Pedestal(C.RCX + (nItems > 1 ? (i == 0 ? -80 : 80) : 0), C.RCY - 90, RollNewItem(1), 0, PedestalKind.Item) { Id = 100 + i });
        float xpv = 18 + 6 * Depth;
        while (xpv > 0)
        {
            Room.Pickups.Add(new Pickup(PickupKind.Xp, boss.X, boss.Y, Cos, MathF.Min(8, xpv)));
            xpv -= 8;
        }
        for (int i = 0; i < 2; i++) Room.Pickups.Add(new Pickup(PickupKind.Heart, boss.X, boss.Y, Cos));
        for (int i = 0; i < 4 + Math.Min(Depth / 3, 10); i++) Room.Pickups.Add(new Pickup(PickupKind.Coin, boss.X, boss.Y, Cos));
        Events.Enqueue(new RunEvent(RunEventKind.BossDefeated, null, boss.Key));
        if (Depth == C.StoryFloors && !StoryDone)
        {
            StoryDone = true;
            Fx.Banner("SERCE OTCHŁANI POKONANE!", "Właz prowadzi w Głębię – jak daleko zejdziesz?", new Color("ffdc78"), true);
            Events.Enqueue(new RunEvent(RunEventKind.StoryComplete));
        }
        else
            Fx.Banner("Boss pokonany!", "Wejdź do włazu, aby zejść niżej", new Color("ffdc78"));
    }

    public void Explode(float x, float y, float r, float dmg, bool hurtPlayer = false, bool hurtEnemies = true, Color? color = null,
        bool small = false, PlayerSim owner = null, string source = "explosion")
    {
        var clr = color ?? new Color("ffa03c");
        Fx.Burst(x, y, clr, small ? 10 : 26, r * 4, 6, 0.45f, glow: true);
        Fx.Burst(x, y, new Color("5a5050"), small ? 6 : 14, r * 2, 10, 0.8f, kind: "smoke");
        Fx.Ring(x, y, new Color("ffdc96"), r, 0.3f, small ? 3 : 6);
        Fx.Shake(small ? 3 : 9);
        Fx.Sound("explosion", small ? 0.35f : 0.8f, 0.06f);
        if (!small)
        {
            Fx.Stain(x, y, new Color("1e1919"), r * 0.6f);
            Room.Stains.Add((new Vector2(x, y), new Color("1e1919"), r * 0.6f));
        }
        if (hurtEnemies)
            foreach (var e in Enemies.ToList())
            {
                float d = MathF.Sqrt((e.X - x) * (e.X - x) + (e.Y - y) * (e.Y - y));
                if (d < r + e.R)
                {
                    float k = d > 0 ? 400 / MathF.Max(1, d) : 0;
                    DamageEnemy(e, dmg, kx: (e.X - x) * k, ky: (e.Y - y) * k, owner: owner, source: source);
                }
            }
        if (hurtPlayer)
            foreach (var p in ActivePlayers().ToList())
                if (MathF.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y)) < r + p.R * 0.5f)
                    p.Hurt(dmg, this, x, y, source);
        int c0 = Room.ColOf(x - r), c1 = Room.ColOf(x + r), r0 = Room.RowOf(y - r), r1 = Room.RowOf(y + r);
        for (int row = Math.Max(0, r0); row <= Math.Min(C.GRows - 1, r1); row++)
            for (int col = Math.Max(0, c0); col <= Math.Min(C.GCols - 1, c1); col++)
            {
                var ob = Room.Grid[row, col];
                if (ob == null) continue;
                if (MathF.Sqrt((ob.X - x) * (ob.X - x) + (ob.Y - y) * (ob.Y - y)) < r + C.Tile * 0.3f)
                {
                    if (ob.Breakable) DamageObstacle(ob, 999);
                    else if (ob.Kind == ObstacleKind.Rock && !small)
                    {
                        Room.RemoveObstacle(ob);
                        Fx.Burst(ob.X, ob.Y, new Color("827d87"), 12, 200, 6, 0.5f, kind: "square");
                        RecomputeFlows();
                    }
                }
            }
    }

    public void DamageObstacle(Obstacle ob, float dmg)
    {
        if (!ob.Breakable) return;
        ob.Hp -= dmg;
        ob.Flash = 0.08f;
        if (ob.Hp > 0 || Room.Grid[ob.Row, ob.Col] != ob) return;
        Room.RemoveObstacle(ob);
        RecomputeFlows();
        Fx.Sound("break", 0.7f);
        if (ob.Kind == ObstacleKind.Crate)
        {
            Fx.Burst(ob.X, ob.Y, new Color("a5703e"), 14, 220, 6, 0.6f, kind: "square");
            float r = R.NextFloat();
            if (r < 0.35f)
                for (int i = R.RangeInt(1, 2); i > 0; i--) Room.Pickups.Add(new Pickup(PickupKind.Coin, ob.X, ob.Y, Cos));
            else if (r < 0.35f + 0.08f * Diff.Heal)
                Room.Pickups.Add(new Pickup(PickupKind.Heart, ob.X, ob.Y, Cos));
        }
        else
            Explode(ob.X, ob.Y, 95, 24 * (0.5f + 0.5f * Diff.EnemyDmg) * Balance.EnemyDmgScale(Depth), hurtPlayer: true, source: "barrel");
    }

    public void ChainLightning(float x, float y, float dmg, int jumps, float firstRange = 300, float jumpRange = 220, HashSet<int> exclude = null,
        PlayerSim owner = null)
    {
        var hit = exclude != null ? new HashSet<int>(exclude) : new HashSet<int>();
        float cx = x, cy = y;
        bool any = false;
        for (int i = 0; i < jumps; i++)
        {
            var t = NearestEnemy(cx, cy, i == 0 ? firstRange : jumpRange, hit);
            if (t == null) break;
            Fx.Bolt(cx, cy, t.X, t.Y);
            hit.Add(t.Id);
            DamageEnemy(t, dmg, color: new Color("aadcff"), owner: owner, source: "chain");
            cx = t.X;
            cy = t.Y;
            any = true;
        }
        if (any) Fx.Sound("lightning", 0.6f, 0.05f);
    }

    public void BulletHitEnemy(Bullet b, Enemy e)
    {
        var p = b.Owner ?? Players[0];
        b.Hit.Add(e.Id);
        float sp = MathF.Max(1, b.Speed), kb = 110 * b.Knock;
        DamageEnemy(e, b.Dmg, b.Crit, kx: b.Vx / sp * kb, ky: b.Vy / sp * kb, owner: b.Owner, source: b.Source);
        Fx.Spark(b.X, b.Y, b.Color, 4, 220, MathF.Atan2(-b.Vy, -b.Vx), 1.4f, 0.2f);
        if (b.Fx)
        {
            if (p.Poison > 0)
            {
                e.PoisonT = 3;
                e.PoisonDps = MathF.Max(e.PoisonDps, p.EffDamage() * 0.45f * p.Poison);
            }
            if (p.Burn > 0 && R.NextFloat() < p.Burn)
            {
                e.BurnT = 3;
                e.BurnDps = MathF.Max(e.BurnDps, p.EffDamage() * 0.7f);
            }
            if (p.Slow > 0) e.SlowT = e.IsBoss ? 0.5f : 1.5f;
            if (p.Chain > 0 && R.NextFloat() < p.Chain)
                ChainLightning(e.X, e.Y, b.Dmg * 0.6f, 3 + (int)(p.Chain * 4), 240, 220, new HashSet<int> { e.Id }, p);
            if (p.Explode > 0)
                Explode(b.X, b.Y, 48 + 14 * (p.Explode - 1), b.Dmg * 0.55f, small: true, owner: p);
            if (p.Split > 0 && !b.IsSplit)
            {
                int n = 2 + (int)p.Split;
                float baseA = b.Angle;
                for (int i = 0; i < n; i++)
                {
                    float a = baseA + (i - (n - 1) / 2f) * 0.6f;
                    var s = new Bullet(b.X, b.Y, MathF.Cos(a) * sp * 0.8f, MathF.Sin(a) * sp * 0.8f, b.Dmg * 0.4f, MathF.Max(3, b.R * 0.6f),
                        b.Color, true, 230, b.Style, spectral: b.Spectral, knock: 0.5f, source: "split") { IsSplit = true, Owner = b.Owner };
                    s.Hit.UnionWith(b.Hit);
                    Bullets.Add(s);
                }
            }
            p.OnHit(this);
        }
        if (b.Pierce > 0) b.Pierce--;
        else b.Kill(this, false);
    }

    // ================================================================== znajdźki, piedestały, sklep
    public bool CollectPickup(Pickup pk, PlayerSim p)
    {
        switch (pk.Kind)
        {
            case PickupKind.Coin:
                Coins += (int)pk.Value;
                Stats.Coins += (int)pk.Value;
                foreach (var o in ActivePlayers())
                    if (o.CoinHeal > 0) o.Heal(o.CoinHeal * pk.Value, this, false);
                Fx.Sound("coin", 0.5f, 0.04f);
                return true;
            case PickupKind.Xp:
                foreach (var o in ActivePlayers().ToList())
                    o.AddXp(pk.Value, this);
                Fx.Sound("xp", 0.4f, 0.03f);
                return true;
            case PickupKind.Heart:
                if (p.Hp >= p.MaxHp) return false;
                p.Heal((18 + p.MaxHp * 0.06f) * (0.7f + 0.3f * Diff.Heal), this);
                Fx.Sound("heal", 0.7f);
                Fx.Burst(p.X, p.Y, new Color("ff6478"), 10, 160, 4, 0.5f);
                return true;
            case PickupKind.Chest:
                Fx.Sound("coin");
                Fx.Sound("break", 0.5f);
                Fx.Burst(pk.X, pk.Y, C.Gold, 18, 250, 5, 0.6f, glow: true);
                for (int i = R.RangeInt(2, 4); i > 0; i--) Room.Pickups.Add(new Pickup(PickupKind.Coin, pk.X, pk.Y, Cos));
                if (R.NextFloat() < 0.3f * Diff.Heal) Room.Pickups.Add(new Pickup(PickupKind.Heart, pk.X, pk.Y, Cos));
                if (R.NextFloat() < 0.12f + p.Luck * 0.004f)
                    Room.Pedestals.Add(new Pedestal((int)pk.X, (int)pk.Y, RollNewItem(0, p), 0, PedestalKind.Item) { Id = 200 + Room.Pedestals.Count });
                return true;
        }
        return false;
    }

    /// <summary>Piedestał w zasięgu interakcji gracza (do podpowiedzi „[E] Weź”).</summary>
    public Pedestal InteractTarget(PlayerSim p)
    {
        Pedestal best = null;
        float bd = Pedestal.InteractRange * Pedestal.InteractRange;
        foreach (var pd in Room.Pedestals)
        {
            if (!pd.Alive) continue;
            float d = (p.X - pd.X) * (p.X - pd.X) + (p.Y - (pd.Y - 4)) * (p.Y - (pd.Y - 4));
            if (d < bd) { bd = d; best = pd; }
        }
        return best;
    }

    /// <summary>Czy gracz stoi przy sklepikarzu (rozmowa otwiera menu sklepu)?</summary>
    public bool NearShopkeeper(PlayerSim p) =>
        Room.Kind == RoomKind.Shop && new Vector2(p.X, p.Y).DistanceTo(ShopkeeperPos) < 70;

    public void TryInteract(PlayerSim p)
    {
        if (!p.Active) return;
        var pd = InteractTarget(p);
        if (pd != null)
        {
            UsePedestal(pd, p);
            return;
        }
        if (NearShopkeeper(p))
            Events.Enqueue(new RunEvent(RunEventKind.ShopOpened, p));
    }

    private void Deny(Pedestal pd, string msg)
    {
        if (pd.Cool > 0) return;
        pd.Cool = 1.2f;
        Fx.Sound("deny");
        Fx.Text(pd.X, pd.Y - 50, msg, new Color("ff7878"), 18);
    }

    /// <summary>Użycie piedestału: weź / kup / ołtarz krwi. Zwraca true, jeśli się udało.</summary>
    public bool UsePedestal(Pedestal pd, PlayerSim p)
    {
        if (!pd.Alive) return false;
        switch (pd.Kind)
        {
            case PedestalKind.Item:
                TakeItem(pd, p);
                return true;
            case PedestalKind.Shop:
                if (Coins < pd.Price) { Deny(pd, "Za mało monet!"); return false; }
                Coins -= pd.Price;
                Stats.Spent += pd.Price;
                Stats.Bought++;
                Fx.Sound("buy");
                TakeItem(pd, p);
                return true;
            case PedestalKind.HeartShop:
                if (p.Hp >= p.MaxHp) { Deny(pd, "Pełne zdrowie"); return false; }
                if (Coins < pd.Price) { Deny(pd, "Za mało monet!"); return false; }
                Coins -= pd.Price;
                Stats.Spent += pd.Price;
                p.Heal(p.MaxHp * pd.Heal, this);
                Fx.Sound("heal");
                pd.Alive = false;
                Fx.Burst(pd.X, pd.Y - 18, new Color("ff6478"), 16, 200, 5, 0.5f);
                return true;
            case PedestalKind.Shrine:
                if (p.MaxHp - pd.HpCost < 10) { Deny(pd, "Za mało życia!"); return false; }
                p.MaxHp -= pd.HpCost;
                p.Hp = MathF.Min(p.Hp, p.MaxHp);
                Fx.Burst(p.X, p.Y, new Color("c81428"), 30, 300, 6, 0.7f);
                Stats.Shrines++;
                TakeItem(pd, p);
                foreach (var other in Room.Pedestals)
                    if (other != pd && other.Group == pd.Group && other.Alive)
                    {
                        other.Alive = false;
                        Fx.Burst(other.X, other.Y - 18, new Color("781e28"), 20, 200, 6, 0.6f, kind: "smoke");
                    }
                return true;
        }
        return false;
    }

    private void TakeItem(Pedestal pd, PlayerSim p)
    {
        var it = pd.Item;
        pd.Alive = false;
        p.ApplyItem(it, this);
        Fx.Sound("item", 0.8f);
        Fx.Burst(pd.X, pd.Y - 18, C.RarityColors[it.Rarity], 30, 300, 6, 0.8f, glow: true);
        Fx.Ring(p.X, p.Y, C.RarityColors[it.Rarity], 90, 0.5f);
        Fx.Banner(it.Name, it.Desc, C.RarityColors[it.Rarity]);
        Stats.ItemsTaken++;
        Events.Enqueue(new RunEvent(RunEventKind.ItemTaken, p, it.Key));
    }

    // ================================================================== pętla
    /// <summary>Krok symulacji. inputs[i] to wejście gracza i (brakujące = brak ruchu).</summary>
    public void Update(float dt, IReadOnlyList<SimInput> inputs)
    {
        FrameDt = dt;
        Time += dt;
        switch (State)
        {
            case RunState.Transition:
                StateT += dt;
                if (StateT >= 0.3f) State = RunState.Play;
                return;
            case RunState.Descend:
                StateT += dt;
                if (StateT >= 0.6f && !_descended)
                {
                    _descended = true;
                    NewFloor(Depth + 1);
                }
                if (StateT >= 1.2f)
                {
                    State = RunState.Play;
                    _descended = false;
                }
                return;
            case RunState.Dying:
                StateT += dt;
                return;
        }
        if (Hitstop > 0)
        {
            Hitstop -= dt;
            return;
        }
        RunTime += dt;

        for (int i = 0; i < Players.Count; i++)
        {
            var inp = i < inputs.Count ? inputs[i] : new SimInput();
            Players[i].Update(dt, this, inp);
            if (State != RunState.Play) return;
            if (inp.Interact) TryInteract(Players[i]);
        }

        FlowT -= dt;
        if (FlowT <= 0)
        {
            FlowT = 0.25f;
            RecomputeFlows();
        }

        foreach (var e in Enemies.ToList())
            if (e.Alive) e.Update(dt, this);

        for (int i = Spawns.Count - 1; i >= 0; i--)
        {
            var s = Spawns[i];
            s.T -= dt;
            if (s.T > 0) continue;
            Spawns.RemoveAt(i);
            var e = SpawnEnemy(s.Key, s.X, s.Y, elite: s.Elite, miniBoss: s.MiniBoss);
            if (e != null && s.Elite)
                Fx.Ring(e.X, e.Y, new Color("ffd246"), 50, 0.4f);
        }

        for (int i = 0, n = Bullets.Count; i < n; i++)
            if (Bullets[i].Alive) Bullets[i].Update(dt, this);

        // kolizje pocisków (po indeksie: trafienia mogą dodawać nowe pociski, np. rozszczepienie)
        for (int bi = 0, bn = Bullets.Count; bi < bn; bi++)
        {
            var b = Bullets[bi];
            if (!b.Alive) continue;
            if (b.Friendly)
            {
                foreach (var e in Enemies.ToList())
                {
                    if (!e.Alive || e.Invuln || b.Hit.Contains(e.Id)) continue;
                    float rr = e.R + b.R;
                    if ((e.X - b.X) * (e.X - b.X) + (e.Y - b.Y) * (e.Y - b.Y) < rr * rr)
                    {
                        BulletHitEnemy(b, e);
                        if (!b.Alive) break;
                    }
                }
            }
            else
                foreach (var p in Players)
                {
                    if (!p.Active) continue;
                    float rr = p.R * 0.6f + b.R;
                    if ((p.X - b.X) * (p.X - b.X) + (p.Y - b.Y) * (p.Y - b.Y) < rr * rr && p.Hurt(b.Dmg, this, b.X, b.Y, b.Source))
                    {
                        if (b.Chill > 0) p.SlowT = MathF.Max(p.SlowT, b.Chill);
                        b.Kill(this);
                        break;
                    }
                }
        }
        Bullets.RemoveAll(b => !b.Alive);
        if (Bullets.Count > 900) Bullets.RemoveRange(0, Bullets.Count - 900);
        if (State != RunState.Play) return;

        // kontakt z wrogami
        foreach (var e in Enemies.ToList())
        {
            if (!e.Alive || e.ContactDmg <= 0 || e.Alpha < 0.47f) continue;
            if (e is Boss bb && !bb.ContactOn) continue;
            foreach (var p in Players)
            {
                if (!p.Active) continue;
                float rr = e.R + p.R * 0.7f;
                if ((e.X - p.X) * (e.X - p.X) + (e.Y - p.Y) * (e.Y - p.Y) >= rr * rr) continue;
                bool hurt = p.Hurt(e.ContactDmg, this, e.X, e.Y, e.Key);
                if (p.Thorns > 0 && e.ThornCd <= 0)
                {
                    e.ThornCd = 0.5f;
                    DamageEnemy(e, p.Thorns * (1 + 0.05f * (Depth - 1)), color: new Color("c8c8dc"), owner: p, source: "thorns");
                }
                if (hurt && !e.IsBoss)
                {
                    float d = MathF.Max(1, MathF.Sqrt((e.X - p.X) * (e.X - p.X) + (e.Y - p.Y) * (e.Y - p.Y)));
                    e.Knock((e.X - p.X) / d * 300, (e.Y - p.Y) / d * 300);
                }
            }
        }
        if (State != RunState.Play) return;

        // rozpychanie wrogów
        for (int i = 0; i < Enemies.Count; i++)
        {
            var a = Enemies[i];
            if (a.IsBoss) continue;
            for (int j = i + 1; j < Enemies.Count; j++)
            {
                var b2 = Enemies[j];
                if (b2.IsBoss) continue;
                float dx = b2.X - a.X, dy = b2.Y - a.Y, rr = a.R + b2.R, d2 = dx * dx + dy * dy;
                if (d2 <= 0 || d2 >= rr * rr) continue;
                float d = MathF.Sqrt(d2), push = (rr - d) * 0.5f, nx = dx / d, ny = dy / d;
                if (a.KnockResist > 0) { a.X -= nx * push; a.Y -= ny * push; }
                if (b2.KnockResist > 0) { b2.X += nx * push; b2.Y += ny * push; }
            }
        }

        foreach (var pk in Room.Pickups.ToList())
            if (pk.Alive) pk.Update(dt, this);
        Room.Pickups.RemoveAll(pk => !pk.Alive);
        foreach (var pd in Room.Pedestals) pd.Cool = MathF.Max(0, pd.Cool - dt);
        Room.Pedestals.RemoveAll(pd => !pd.Alive);
        foreach (var ob in Room.Obstacles())
            if (ob.Flash > 0) ob.Flash -= dt;

        for (int i = 0; i < Effects.Count; i++)
            if (Effects[i].Alive) Effects[i].Update(dt, this);
        Effects.RemoveAll(ef => !ef.Alive);

        // właz
        if (TrapDelay > 0) TrapDelay -= dt;
        if (Room.Trapdoor is { } td && TrapDelay <= 0)
            foreach (var p in ActivePlayers())
                if (MathF.Sqrt((p.X - td.X) * (p.X - td.X) + (p.Y - td.Y) * (p.Y - td.Y)) < 26)
                {
                    State = RunState.Descend;
                    StateT = 0;
                    Fx.Sound("trapdoor");
                    break;
                }

        // czyszczenie pokoju
        if (!Room.Cleared && Spawns.Count == 0 && Room.Kind is RoomKind.Combat or RoomKind.Exit)
        {
            if (WaveIdx < Room.Waves.Count - 1 && Enemies.Count <= 1)
            {
                WaveIdx++;
                SpawnWave(Room.Waves[WaveIdx]);
            }
            else if (Enemies.Count == 0)
                RoomCleared();
        }
    }

    /// <summary>Stawia właz i usuwa przeszkody wokół niego, żeby zawsze dało się do niego dojść.</summary>
    private void PlaceTrapdoor(Vector2 pos)
    {
        Room.Trapdoor = pos;
        int c0 = Room.ColOf(pos.X), r0 = Room.RowOf(pos.Y);
        for (int r = r0 - 1; r <= r0 + 1; r++)
            for (int c = c0 - 1; c <= c0 + 1; c++)
                if (c >= 0 && c < C.GCols && r >= 0 && r < C.GRows && Room.Grid[r, c] is { } ob)
                {
                    Room.RemoveObstacle(ob);
                    Fx.Burst(ob.X, ob.Y, new Color("827d87"), 10, 160, 5, 0.4f, kind: "square");
                }
        RecomputeFlows();
    }

    private void RoomCleared()
    {
        Room.Cleared = true;
        Fx.Sound("door_open", 0.8f);
        foreach (var p in Players)
            if (p.Downed) p.Revive(this, 0.35f);
        foreach (var p in ActivePlayers())
            if (p.RoomAbility > 0 && p.AbilityT > 0)
            {
                p.AbilityT = 0;
                Fx.Text(p.X, p.Y - 44, "Umiejętność gotowa!", new Color("ffd246"), 18);
            }
        float r = R.NextFloat(), healCh = 0.16f * Diff.Heal;
        float luck = Players.Max(p => p.Luck);
        var drop = FreeSpotNear(C.RCX, C.RCY);   // środek pokoju bywa zajęty przez kamień
        if (r < 0.22f)
            for (int i = R.RangeInt(1, 3); i > 0; i--) Room.Pickups.Add(new Pickup(PickupKind.Coin, drop.X, drop.Y, Cos));
        else if (r < 0.22f + healCh)
            Room.Pickups.Add(new Pickup(PickupKind.Heart, drop.X, drop.Y, Cos));
        else if (r < 0.22f + healCh + 0.07f + luck * 0.002f)
            Room.Pickups.Add(new Pickup(PickupKind.Chest, drop.X, drop.Y, Cos, pop: false));
        if (Room.Kind == RoomKind.Exit)
        {
            PlaceTrapdoor(new Vector2(C.RCX, C.RCY + 110));
            TrapDelay = 1.0f;
            Fx.Banner("Droga w dół otwarta", "Wejdź do włazu, aby zejść niżej", Floor.Biome.AccentColor);
        }
        foreach (var p in Players)
            if (p.Active) Fx.Ring(p.X, p.Y, Colors.White, 40, 0.3f);
        Events.Enqueue(new RunEvent(RunEventKind.RoomCleared));
    }
}
