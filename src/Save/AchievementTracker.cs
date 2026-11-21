using System;
using System.Collections.Generic;
using System.Linq;
using Otchlan.Platform;
using Otchlan.Sim;

namespace Otchlan.Save;

/// <summary>
/// Czysta logika osiągnięć (bez UI i bez Godota poza <see cref="Content"/>): sprawdza warunki na podstawie stanu
/// <see cref="RunSim"/> i jego zdarzeń, prowadzi liczniki łączne w <see cref="Profile.Stats"/>, zapisuje odblokowania
/// w <see cref="Profile.Achievements"/> (raz) i melduje je platformie. Nie zapisuje pliku profilu – robi to widok.
/// Użycie: po każdym kroku symulacji <c>OnEvent</c> dla każdego zdarzenia, potem <c>Step</c>, na końcu <c>TakeFresh</c>.
/// </summary>
public sealed class AchievementTracker
{
    public const string StatKills = "tot_kills", StatElites = "tot_elites";

    // progi (w jednym miejscu, żeby opisy w data/achievements.json łatwo zgrać)
    public const int Items10 = 10, Items25 = 25, Items50 = 50, MinimalistItems = 8, SpenderCoins = 300, RichCoins = 400, VeteranLevel = 25;
    public const int WaveBullets = 15, KiBullets = 20, DanceKills = 6, TotemKills = 10, BloodCult = 3, BarrelKills = 3;
    public const float FastWinSeconds = 75 * 60;
    public static readonly int[] DepthMarks = { 45, 50, 60 };

    private sealed class Rule
    {
        public string Api;
        public Func<bool> Step;                 // sprawdzane po każdym kroku
        public Func<RunEvent, bool> Event;      // sprawdzane dla zdarzeń
        public Func<float> Run;                 // bieżąca wartość postępu w wyprawie (zapamiętywana jako najlepsza)
    }

    private readonly RunSim _sim;
    private readonly Profile _prof;
    private readonly IPlatform _plat;
    private readonly List<Rule> _rules = new();
    private readonly List<AchievementDef> _fresh = new();
    private readonly HashSet<string> _seen;
    private int _lastKills, _lastElites, _itemSig = -1;

    public AchievementTracker(RunSim sim, Profile profile, IPlatform platform = null)
    {
        _sim = sim;
        _prof = profile;
        _plat = platform ?? PlatformService.Current;
        _seen = new HashSet<string>(profile.SeenItems);
        _lastKills = sim.Stats.Kills;
        _lastElites = sim.Stats.Elites;
        BuildRules();
    }

    /// <summary>Świeżo odblokowane osiągnięcia (do pokazania) – odbiera i czyści listę.</summary>
    public List<AchievementDef> TakeFresh()
    {
        var l = new List<AchievementDef>(_fresh);
        _fresh.Clear();
        return l;
    }

    public bool HasFresh => _fresh.Count > 0;

    // ================================================================== reguły
    private IEnumerable<PlayerSim> P => _sim.Players;
    private int ItemCount => Math.Max(_sim.Stats.ItemsTaken, P.Sum(p => p.Items.Count));

    private void Step(string api, Func<bool> cond, Func<float> run = null) => _rules.Add(new Rule { Api = api, Step = cond, Run = run });
    private void Ev(string api, Func<RunEvent, bool> cond) => _rules.Add(new Rule { Api = api, Event = cond });

    private void BuildRules()
    {
        var db = Content.Db;
        var st = _sim.Stats;

        Step("ACH_FIRST_BLOOD", () => st.Kills >= 1);

        // krainy (piętra 1, 6, 11, …) i Głębia
        for (int i = 0; i < db.Biomes.Count; i++)
        {
            int floor = i * C.FloorsPerBiome + 1;
            Step($"ACH_BIOME_{i + 1}", () => _sim.Depth >= floor);
        }
        foreach (int d in DepthMarks)
            Step($"ACH_DEPTH_{d}", () => _sim.Depth >= d);

        // bossowie krain 1–7 (ósmy to Serce Otchłani = ACH_WIN)
        for (int i = 0; i < db.Biomes.Count - 1; i++)
        {
            string boss = db.Biomes[i].Boss;
            Ev($"ACH_BOSS_{i + 1}", ev => ev.Kind == RunEventKind.BossDefeated && ev.Data == boss);
        }
        Ev("ACH_BOSS_NO_HIT", ev => ev.Kind == RunEventKind.BossDefeated && !P.Any(p => p.HitThisRoom));
        Ev("ACH_BOSS_LOW_HP", ev => ev.Kind == RunEventKind.BossDefeated && P.Any(p => p.Active && p.Hp < p.MaxHp * 0.1f));
        Ev("ACH_FLOOR_NO_HIT", ev => (ev.Kind == RunEventKind.BossDefeated || ev.Kind == RunEventKind.RoomCleared && _sim.Room?.Kind == RoomKind.Exit)
                                     && !P.Any(p => p.HitThisFloor));

        // wygrana (zdarzenie StoryComplete)
        bool Win(RunEvent ev) => ev.Kind == RunEventKind.StoryComplete;
        Ev("ACH_WIN", Win);
        Ev("ACH_WIN_HARD", ev => Win(ev) && _sim.DiffIdx >= 2);
        Ev("ACH_WIN_NIGHTMARE", ev => Win(ev) && _sim.DiffIdx >= 3);
        Ev("ACH_WIN_FAST", ev => Win(ev) && _sim.RunTime < FastWinSeconds);
        Ev("ACH_SEAL_5", ev => Win(ev) && _sim.Seal >= 5);
        Ev("ACH_SEAL_10", ev => Win(ev) && _sim.Seal >= 10);
        Ev("ACH_COOP_WIN", ev => Win(ev) && _sim.Coop);
        Ev("ACH_MINIMALIST", ev => Win(ev) && P.Sum(p => p.Items.Count) <= MinimalistItems);
        Ev("ACH_NO_SHOP", ev => Win(ev) && st.Bought == 0);
        Ev("ACH_ALL_CHARS", ev => Win(ev) && WonChars(includeCurrent: true) >= db.Characters.Count);
        foreach (var c in db.Characters)
        {
            string key = c.Key;
            Ev($"ACH_WIN_{key.ToUpperInvariant()}", ev => Win(ev) && P.Any(p => p.Key == key));
            if (c.Unlock != null)
                Step($"ACH_UNLOCK_{key.ToUpperInvariant()}", () => _prof.IsUnlocked(key));
        }

        // co-op
        Step("ACH_REVIVE", () => st.PartnerRevives > 0);

        // przedmioty
        Step("ACH_ITEMS_10", () => ItemCount >= Items10, () => ItemCount);
        Step("ACH_ITEMS_25", () => ItemCount >= Items25, () => ItemCount);
        Step("ACH_ITEMS_50", () => ItemCount >= Items50, () => ItemCount);
        Step("ACH_LEGENDARY", () => P.Any(p => p.Items.Any(k => db.ItemByKey.TryGetValue(k, out var it) && it.Rarity >= 3)));
        Step("ACH_ITEMS_ALL", () => SeenItemCount(_seen) >= db.Items.Count);
        Step("ACH_SYNERGY", () => P.Any(p => p.Homing > 0 && p.Pierce > 0 && p.Bounce > 0));

        // umiejętności postaci
        Step("ACH_ABILITY_RYCERZ", () => st.WaveBest >= WaveBullets, () => st.WaveBest);
        Step("ACH_ABILITY_INZYNIER", () => st.TwoSentries > 0);
        Step("ACH_ABILITY_BARD", () => st.BardDuo > 0);
        Step("ACH_ABILITY_REWOLWEROWIEC", () => st.DanceBest >= DanceKills, () => st.DanceBest);
        Step("ACH_ABILITY_MNICH", () => st.KiBest >= KiBullets, () => st.KiBest);
        Step("ACH_ABILITY_SZAMAN", () => st.TotemKills >= TotemKills, () => st.TotemKills);

        // klasyka z wersji 1.x
        Step("ACH_SHRINE", () => st.Shrines >= 1);
        Step("ACH_BLOOD_CULT", () => st.Shrines >= BloodCult, () => st.Shrines);
        Step("ACH_SPENDER", () => st.Spent >= SpenderCoins, () => st.Spent);
        Step("ACH_RICH", () => _sim.Coins >= RichCoins, () => _sim.Coins);
        Step("ACH_LEVEL_25", () => P.Any(p => p.Level >= VeteranLevel), () => P.Max(p => p.Level));
        Step("ACH_KILLS_5000", () => Total(StatKills) >= 5000);
        Step("ACH_ELITES_250", () => Total(StatElites) >= 250);
        Step("ACH_BARREL", () => st.BarrelBest >= BarrelKills);
        Step("ACH_PHOENIX", () => st.Phoenix > 0);
    }

    // ================================================================== przetwarzanie
    /// <summary>Zdarzenie wyprawy (po tym, jak widok zaktualizował rekordy profilu).</summary>
    public void OnEvent(RunEvent ev)
    {
        foreach (var r in _rules)
            if (r.Event != null && !IsUnlocked(r.Api) && r.Event(ev))
                Unlock(r.Api);
        if (ev.Kind is RunEventKind.RoomCleared or RunEventKind.FloorEntered or RunEventKind.GameOver or RunEventKind.StoryComplete)
            SyncPlatformStats();
    }

    /// <summary>Warunki oparte na stanie – wołać po każdym kroku symulacji.</summary>
    public void Step()
    {
        // liczniki łączne (przyrostowo, więc wczytanie zapisu nie liczy starych zabójstw drugi raz)
        int dk = _sim.Stats.Kills - _lastKills, de = _sim.Stats.Elites - _lastElites;
        _lastKills = _sim.Stats.Kills;
        _lastElites = _sim.Stats.Elites;
        if (dk > 0) _prof.Add(StatKills, dk);
        if (de > 0) _prof.Add(StatElites, de);

        // przedmioty widziane (Kolekcjoner) – tylko gdy coś się zmieniło
        int sig = P.Sum(p => p.Items.Count) * 1000 + _sim.Stats.ItemsTaken;
        if (sig != _itemSig)
        {
            _itemSig = sig;
            foreach (var p in P)
                foreach (var k in p.Items)
                    if (_seen.Add(k) && Content.Db.ItemByKey.ContainsKey(k) && !_prof.SeenItems.Contains(k))
                        _prof.SeenItems.Add(k);
        }

        foreach (var r in _rules)
        {
            if (IsUnlocked(r.Api)) continue;
            if (r.Run != null)
            {
                float v = r.Run();
                if (v > _prof.AchProgress.GetValueOrDefault(r.Api))
                    _prof.AchProgress[r.Api] = v;
            }
            if (r.Step != null && r.Step())
                Unlock(r.Api);
        }
    }

    private void SyncPlatformStats()
    {
        _plat.SetStat(StatKills, (int)Total(StatKills));
        _plat.SetStat(StatElites, (int)Total(StatElites));
    }

    private bool IsUnlocked(string api) => _prof.Achievements.GetValueOrDefault(api);

    private void Unlock(string api)
    {
        if (IsUnlocked(api)) return;
        _prof.Achievements[api] = true;
        var def = Content.Db.Achievements.FirstOrDefault(a => a.Api == api);
        if (def == null) return;
        _fresh.Add(def);
        _plat.Unlock(api);
    }

    private long Total(string stat) => _prof.Stats.GetValueOrDefault(stat);

    /// <summary>Ile różnych postaci wygrało (z profilu; opcjonalnie z doliczeniem bieżącej wyprawy).</summary>
    private int WonChars(bool includeCurrent)
    {
        var set = new HashSet<string>(_prof.WinsByChar.Where(kv => kv.Value > 0).Select(kv => kv.Key));
        if (includeCurrent)
            foreach (var p in P) set.Add(p.Key);
        return set.Count;
    }

    // ================================================================== postęp dla UI
    /// <summary>Cel osiągnięcia: z danych, a dla „Wszechstronnego” i „Kolekcjonera” liczony z liczby postaci / przedmiotów.</summary>
    public static float? GoalOf(AchievementDef d) => d.Api switch
    {
        "ACH_ALL_CHARS" => Content.Db.Characters.Count,
        "ACH_ITEMS_ALL" => Content.Db.Items.Count,
        _ => d.Goal,
    };

    private static int SeenItemCount(IEnumerable<string> seen) => seen.Count(k => Content.Db.ItemByKey.ContainsKey(k));

    /// <summary>Bieżący postęp osiągnięcia z celem (np. 342/1000) odczytany z profilu; dla odblokowanych równy celowi.</summary>
    public static float ProgressOf(Profile p, AchievementDef d)
    {
        float goal = GoalOf(d) ?? 0;
        if (p.Achievements.GetValueOrDefault(d.Api))
            return goal;
        float v = d.Api switch
        {
            "ACH_KILLS_5000" => p.Stats.GetValueOrDefault(StatKills),
            "ACH_ELITES_250" => p.Stats.GetValueOrDefault(StatElites),
            "ACH_ALL_CHARS" => p.WinsByChar.Count(kv => kv.Value > 0),
            "ACH_ITEMS_ALL" => SeenItemCount(p.SeenItems),
            _ => p.AchProgress.GetValueOrDefault(d.Api),
        };
        return goal > 0 ? Math.Min(v, goal) : v;
    }

    /// <summary>Identyfikatory wszystkich reguł (do testów spójności z data/achievements.json).</summary>
    public IEnumerable<string> RuleApis => _rules.Select(r => r.Api);
}
