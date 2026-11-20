using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Otchlan.Core;
using Otchlan.Sim;

namespace Otchlan.Save;

// ============================================================================ profil gracza
/// <summary>Postępy wspólne dla wszystkich slotów: odblokowania, osiągnięcia, rekordy, statystyki. Plik: user://profile.json.</summary>
public sealed class Profile
{
    public const string FileName = "profile.json";
    public int Version = 1;
    public List<string> Unlocked = new() { "rycerz", "lowczyni", "berserker", "mag", "zlodziej" };
    public int Wins, BestDepth, SealMax, BestSeal = -1;
    public Dictionary<string, int> BestDepthByChar = new();
    public Dictionary<string, int> WinsByChar = new();
    public Dictionary<string, long> Stats = new();
    public Dictionary<string, bool> Achievements = new();
    public Dictionary<string, float> AchProgress = new();
    public List<string> SeenItems = new();
    public string LastChar = "rycerz", LastChar2 = "mag";
    public int LastDifficulty = 1;

    private static Profile _p;
    public static Profile P => _p ??= JsonStore.Load<Profile>(FileName) ?? new Profile();

    public void Save() => JsonStore.Save(FileName, this);

    public void Add(string stat, long v = 1) => Stats[stat] = Stats.GetValueOrDefault(stat) + v;

    public bool IsUnlocked(string charKey) => Unlocked.Contains(charKey);

    public void Unlock(string charKey)
    {
        if (!Unlocked.Contains(charKey)) Unlocked.Add(charKey);
    }

    /// <summary>Wpisuje głębokość do rekordów i odblokowuje postacie za postęp.</summary>
    public List<string> RecordDepth(IEnumerable<string> chars, int depth)
    {
        var newly = new List<string>();
        BestDepth = Math.Max(BestDepth, depth);
        foreach (var c in chars)
            BestDepthByChar[c] = Math.Max(BestDepthByChar.GetValueOrDefault(c), depth);
        foreach (var cd in Content.Db.Characters)
            if (cd.Unlock is { Kind: "floor" } u && depth >= u.Value && !IsUnlocked(cd.Key))
            {
                Unlock(cd.Key);
                newly.Add(cd.Key);
            }
        return newly;
    }

    public List<string> RecordWin(IEnumerable<string> chars, int seal)
    {
        var newly = new List<string>();
        Wins++;
        foreach (var c in chars)
            WinsByChar[c] = WinsByChar.GetValueOrDefault(c) + 1;
        BestSeal = Math.Max(BestSeal, seal);
        SealMax = Math.Min(Content.Db.Seals.Count, Math.Max(SealMax, seal + 1));
        foreach (var cd in Content.Db.Characters)
            if (cd.Unlock is { Kind: "win" } && !IsUnlocked(cd.Key))
            {
                Unlock(cd.Key);
                newly.Add(cd.Key);
            }
        return newly;
    }

    /// <summary>Tylko do testów: podmienia bieżący profil.</summary>
    public static void Override(Profile p) => _p = p;
}

// ============================================================================ zapis wyprawy
public sealed class SlotInfo
{
    public int Slot;
    public bool Empty = true;
    public List<string> Chars = new();
    public int Depth, Difficulty, Seal;
    public string Biome;
    public float RunTime;
    public DateTime SavedAt;

    public string Title => Empty ? "Pusty slot" :
        string.Join(" + ", Chars.Select(c => Content.Db.CharByKey.TryGetValue(c, out var d) ? d.Name : c));

    public string Subtitle => Empty ? "Nowa wyprawa" :
        $"{(Depth > C.StoryFloors ? $"Głębia {Depth - C.StoryFloors}" : $"Piętro {Depth}")} · {Biome} · {Content.Db.Difficulties[Difficulty].Name}" +
        (Seal > 0 ? $" · Pieczęć {Seal}" : "") + $" · {(int)RunTime / 60:00}:{(int)RunTime % 60:00}";
}

public sealed class PlayerSave
{
    public string Key;
    public Dictionary<string, float> Stats = new();
    public float Hp, X, Y, Xp, AbilityT, AbilityCd, EshieldT, BhT, ReviveP;
    public int Level, DashLeft, PendingLevelUps, BanishesLeft;
    public bool EshieldUp, Downed, Dead;
    public List<string> Items = new(), Banished = new(), UpgradeLog = new();
}

public sealed class ObstacleSave
{
    public ObstacleKind Kind;
    public int Col, Row, Variant;
    public float Hp;
}

public sealed class PickupSave
{
    public PickupKind Kind;
    public float X, Y, Value;
}

public sealed class PedestalSave
{
    public float X, Y, Heal;
    public string Item;
    public int Price, Group, HpCost, Id;
    public PedestalKind Kind;
}

public sealed class RoomSave
{
    public int Gx, Gy, Dist, Seed;
    public RoomKind Kind;
    public bool Visited, Seen, Cleared, RewardGiven;
    public List<List<string>> Waves = new();
    public List<ObstacleSave> Obstacles = new();
    public List<PickupSave> Pickups = new();
    public List<PedestalSave> Pedestals = new();
    public float[] Trapdoor;
    public List<float[]> Stains = new();
}

public sealed class RunSave
{
    public const int CurrentVersion = 1;
    public int Version = CurrentVersion;
    public DateTime SavedAt;
    public List<string> Chars = new();
    public int Difficulty, Seal, Depth, MaxDepth, Coins;
    public float RunTime, TrapDelay;
    public bool StoryDone;
    public RngState Rng;
    public RunStats Stats = new();
    public List<string> Taken = new();
    public List<PlayerSave> Players = new();
    public List<RoomSave> Rooms = new();
    public int RoomGx, RoomGy;
}

/// <summary>
/// Sloty zapisu (3). Każdy trzyma jedną wyprawę. Zapis przy wejściu do pokoju, po jego oczyszczeniu,
/// po pokonaniu bossa i przy wyjściu. Śmierć kasuje slot (roguelike). Walka w niedokończonym pokoju
/// zaczyna się po wczytaniu od nowa (bez zapisywania pozycji wrogów).
/// </summary>
public static class SaveSystem
{
    public const int SlotCount = 3;

    public static string SlotFile(int slot) => $"slot_{slot}.json";

    public static SlotInfo Info(int slot)
    {
        var s = JsonStore.Load<RunSave>(SlotFile(slot));
        if (s == null || s.Version > RunSave.CurrentVersion)
            return new SlotInfo { Slot = slot };
        return new SlotInfo
        {
            Slot = slot, Empty = false, Chars = s.Chars, Depth = s.Depth, Difficulty = s.Difficulty, Seal = s.Seal, RunTime = s.RunTime,
            SavedAt = s.SavedAt, Biome = Content.Db.BiomeFor(s.Depth).Name,
        };
    }

    public static List<SlotInfo> AllSlots() => Enumerable.Range(1, SlotCount).Select(Info).ToList();

    public static void Delete(int slot) => JsonStore.Delete(SlotFile(slot));

    public static bool Save(int slot, RunSim g) => JsonStore.Save(SlotFile(slot), Capture(g));

    public static RunSim Load(int slot)
    {
        var s = JsonStore.Load<RunSave>(SlotFile(slot));
        if (s == null || s.Version > RunSave.CurrentVersion)
            return null;
        try
        {
            return Restore(s);
        }
        catch (Exception e)
        {
            GD.PushError($"Nie udało się odtworzyć zapisu {slot}: {e}");
            return null;
        }
    }

    // ------------------------------------------------------------------ zapis
    public static RunSave Capture(RunSim g)
    {
        var s = new RunSave
        {
            SavedAt = DateTime.Now, Chars = g.Players.Select(p => p.Key).ToList(), Difficulty = g.DiffIdx, Seal = g.Seal,
            Depth = g.Depth, MaxDepth = g.MaxDepth, Coins = g.Coins, RunTime = g.RunTime, TrapDelay = g.TrapDelay,
            StoryDone = g.StoryDone, Rng = g.R.GetState(), Stats = g.Stats, Taken = g.Taken.ToList(),
            RoomGx = g.Room.Gx, RoomGy = g.Room.Gy,
        };
        foreach (var p in g.Players)
        {
            var ps = new PlayerSave
            {
                Key = p.Key, Hp = p.Hp, X = p.X, Y = p.Y, Xp = p.Xp, Level = p.Level, AbilityT = p.AbilityT, AbilityCd = p.AbilityCd,
                EshieldT = p.EshieldT, EshieldUp = p.EshieldUp, BhT = p.BhT, DashLeft = p.DashLeft, PendingLevelUps = p.PendingLevelUps,
                BanishesLeft = p.BanishesLeft, Downed = p.Downed, ReviveP = p.ReviveP, Dead = p.Dead,
                Items = p.Items.ToList(), Banished = p.Banished.ToList(), UpgradeLog = p.UpgradeLog.ToList(),
            };
            foreach (var name in PlayerSim.StatFields.Keys)
                ps.Stats[name] = p.GetStat(name);
            s.Players.Add(ps);
        }
        foreach (var r in g.Floor.Rooms.Values)
        {
            var rs = new RoomSave
            {
                Gx = r.Gx, Gy = r.Gy, Dist = r.Dist, Seed = r.Seed, Kind = r.Kind, Visited = r.Visited, Seen = r.Seen,
                Cleared = r.Cleared, RewardGiven = r.RewardGiven, Waves = r.Waves.Select(w => w.ToList()).ToList(),
                Trapdoor = r.Trapdoor is { } t ? new[] { t.X, t.Y } : null,
                Stains = r.Stains.TakeLast(60).Select(st => new[] { st.pos.X, st.pos.Y, st.color.R, st.color.G, st.color.B, st.r }).ToList(),
            };
            foreach (var ob in r.Obstacles())
                rs.Obstacles.Add(new ObstacleSave { Kind = ob.Kind, Col = ob.Col, Row = ob.Row, Variant = ob.Variant, Hp = ob.Hp });
            foreach (var pk in r.Pickups)
                rs.Pickups.Add(new PickupSave { Kind = pk.Kind, X = pk.X, Y = pk.Y, Value = pk.Value });
            foreach (var pd in r.Pedestals.Where(pd => pd.Alive))
                rs.Pedestals.Add(new PedestalSave
                {
                    X = pd.X, Y = pd.Y, Item = pd.Item?.Key, Price = pd.Price, Group = pd.Group, HpCost = pd.HpCost, Heal = pd.Heal,
                    Kind = pd.Kind, Id = pd.Id,
                });
            s.Rooms.Add(rs);
        }
        return s;
    }

    // ------------------------------------------------------------------ odczyt
    public static RunSim Restore(RunSave s)
    {
        var db = Content.Db;
        var g = new RunSim(s.Chars, s.Difficulty, 1, s.Seal, startFloor: false);
        g.R = new Rng(s.Rng);
        g.Depth = s.Depth;
        g.MaxDepth = s.MaxDepth;
        g.Coins = s.Coins;
        g.RunTime = s.RunTime;
        g.StoryDone = s.StoryDone;
        g.Taken.UnionWith(s.Taken);
        CopyStats(s.Stats, g.Stats);
        for (int i = 0; i < g.Players.Count && i < s.Players.Count; i++)
        {
            var p = g.Players[i];
            var ps = s.Players[i];
            foreach (var (k, v) in ps.Stats)
                if (PlayerSim.StatFields.ContainsKey(k)) p.SetStat(k, v);
            p.Hp = ps.Hp;
            p.X = ps.X;
            p.Y = ps.Y;
            p.Xp = ps.Xp;
            p.Level = ps.Level;
            p.AbilityT = ps.AbilityT;
            p.AbilityCd = ps.AbilityCd;
            p.EshieldT = ps.EshieldT;
            p.EshieldUp = ps.EshieldUp;
            p.BhT = ps.BhT;
            p.DashLeft = ps.DashLeft;
            p.PendingLevelUps = ps.PendingLevelUps;
            p.BanishesLeft = ps.BanishesLeft;
            p.Downed = ps.Downed;     // powalony gracz zostaje powalony po wczytaniu (błąd z wersji 1.1)
            p.ReviveP = ps.ReviveP;
            p.Dead = ps.Dead;
            p.Items.AddRange(ps.Items);
            p.Banished.UnionWith(ps.Banished);
            p.UpgradeLog.AddRange(ps.UpgradeLog);
        }
        var floor = new Floor { Depth = s.Depth, Biome = db.BiomeFor(s.Depth) };
        foreach (var rs in s.Rooms)
        {
            var r = new Room(rs.Gx, rs.Gy, rs.Kind, rs.Seed)
            {
                Dist = rs.Dist, Visited = rs.Visited, Seen = rs.Seen, Cleared = rs.Cleared, RewardGiven = rs.RewardGiven,
                Trapdoor = rs.Trapdoor is { Length: 2 } t ? new Vector2(t[0], t[1]) : null,
            };
            r.Waves.AddRange(rs.Waves);
            foreach (var o in rs.Obstacles)
                r.Grid[o.Row, o.Col] = new Obstacle(o.Kind, o.Col, o.Row, o.Variant) { Hp = o.Hp };
            foreach (var pk in rs.Pickups)
                r.Pickups.Add(new Pickup(pk.Kind, pk.X, pk.Y, g.Cos, pk.Value, pop: false));
            foreach (var pd in rs.Pedestals)
                r.Pedestals.Add(new Pedestal(pd.X, pd.Y, pd.Item != null && db.ItemByKey.TryGetValue(pd.Item, out var it) ? it : null, pd.Price, pd.Kind, pd.Group, pd.Heal, pd.HpCost) { Id = pd.Id });
            r.Pedestals.RemoveAll(pd => pd.Item == null && pd.Kind != PedestalKind.HeartShop);
            foreach (var st in rs.Stains)
                if (st.Length == 6) r.Stains.Add((new Vector2(st[0], st[1]), new Color(st[2], st[3], st[4]), st[5]));
            floor.Rooms[(r.Gx, r.Gy)] = r;
        }
        foreach (var ((x, y), room) in floor.Rooms)
            foreach (var d in Dirs.All)
            {
                var (dx, dy) = Dirs.Delta(d);
                if (floor.Rooms.TryGetValue((x + dx, y + dy), out var n))
                    room.Doors[d] = n;
            }
        floor.Start = floor.Rooms.Values.First(r => r.Kind == RoomKind.Start);
        g.Floor = floor;
        var cur = floor.Rooms[(s.RoomGx, s.RoomGy)];
        // pozycje graczy z zapisu; pokój walki zaczyna się od nowa
        var pos = g.Players.Select(p => new Vector2(p.X, p.Y)).ToList();
        g.EnterRoom(cur, null);
        for (int i = 0; i < g.Players.Count; i++)
        {
            g.Players[i].X = pos[i].X;
            g.Players[i].Y = pos[i].Y;
        }
        g.TrapDelay = s.TrapDelay;
        g.Events.Clear();
        return g;
    }

    private static void CopyStats(RunStats from, RunStats to)
    {
        foreach (var f in typeof(RunStats).GetFields())
            f.SetValue(to, f.GetValue(from));
    }
}
