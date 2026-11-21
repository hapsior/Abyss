using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Otchlan.Platform;
using Otchlan.Save;
using Otchlan.Sim;

namespace Otchlan.Tests;

/// <summary>Testy osiągnięć: dane, odblokowania z symulacji (tryb God), liczniki łączne, brak podwójnych odblokowań.</summary>
public static class AchievementTests
{
    /// <summary>Platforma-atrapa zapamiętująca wywołania.</summary>
    private sealed class FakePlatform : IPlatform
    {
        public string Name => "Test";
        public readonly List<string> Unlocked = new();
        public readonly Dictionary<string, int> Stats = new();
        public void Unlock(string api) => Unlocked.Add(api);
        public void SetStat(string name, int value) => Stats[name] = value;
    }

    /// <summary>Wykonuje test na czystym profilu; prawdziwy profil gracza wraca na miejsce (i nic nie trafia na dysk).</summary>
    private static void WithProfile(Action<Profile> body)
    {
        var old = Profile.P;
        var prof = new Profile();
        Profile.Override(prof);
        try { body(prof); }
        finally { Profile.Override(old); }
    }

    private static RunSim NewSim(string key = "rycerz", int diff = 1, ulong seed = 77, int seal = 0)
    {
        var g = new RunSim(new[] { key }, diff, seed, seal) { God = true };
        g.Events.Clear();
        return g;
    }

    /// <summary>Tak jak RunScene: zdarzenia → OnEvent, potem Step.</summary>
    private static void Pump(RunSim g, AchievementTracker t)
    {
        while (g.Events.Count > 0)
            t.OnEvent(g.Events.Dequeue());
        t.Step();
    }

    private static void Run(RunSim g, AchievementTracker t, float seconds, Func<float, SimInput> input = null)
    {
        const float dt = 1f / 60;
        for (float s = 0; s < seconds; s += dt)
        {
            g.Update(dt, new[] { input?.Invoke(s) ?? new SimInput() });
            Pump(g, t);
        }
    }

    private static bool Has(Profile p, string api) => p.Achievements.GetValueOrDefault(api);

    /// <summary>Przechodzi do pokoju bossa danego piętra.</summary>
    private static void ToBossRoom(RunSim g, int depth)
    {
        g.NewFloor(depth);
        g.EnterRoom(g.Floor.Rooms.Values.First(r => r.Kind == RoomKind.Boss), null);
        Assert.True(g.Boss != null, $"brak bossa na piętrze {depth}");
    }

    // ------------------------------------------------------------------ dane
    [Test]
    public static void DataLoadsWithUniqueApis()
    {
        var all = Content.Db.Achievements;
        Assert.True(all.Count >= 40 && all.Count <= 75, $"liczba osiągnięć: {all.Count}");
        Assert.Equal(all.Count, all.Select(a => a.Api).Distinct().Count(), "unikalne api");
        foreach (var a in all)
        {
            Assert.True(a.Api.StartsWith("ACH_"), $"{a.Api}: api bez prefiksu ACH_");
            Assert.True(!string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.Desc), $"{a.Api}: brak nazwy/opisu");
            Assert.True(new Color(a.Color) != default || a.Color == "#000000", $"{a.Api}: zły kolor");
        }
        Assert.Equal(Content.Db.Characters.Count, (int)AchievementTracker.GoalOf(all.First(a => a.Api == "ACH_ALL_CHARS")).Value, "cel Wszechstronnego");
        Assert.Equal(Content.Db.Items.Count, (int)AchievementTracker.GoalOf(all.First(a => a.Api == "ACH_ITEMS_ALL")).Value, "cel Kolekcjonera");
    }

    [Test]
    public static void EveryAchievementHasRuleAndViceVersa()
    {
        WithProfile(prof =>
        {
            var t = new AchievementTracker(NewSim(), prof, new FakePlatform());
            var rules = t.RuleApis.ToList();
            Assert.Equal(rules.Count, rules.Distinct().Count(), "powtórzona reguła");
            var data = Content.Db.Achievements.Select(a => a.Api).ToHashSet();
            foreach (var r in rules)
                Assert.True(data.Contains(r), $"reguła {r} bez wpisu w data/achievements.json");
            foreach (var d in data)
                Assert.True(rules.Contains(d), $"osiągnięcie {d} nie ma reguły");
        });
    }

    // ------------------------------------------------------------------ odblokowania
    [Test]
    public static void FirstBloodAndBiomeReach()
    {
        WithProfile(prof =>
        {
            var plat = new FakePlatform();
            var g = NewSim();
            var t = new AchievementTracker(g, prof, plat);
            Pump(g, t);
            Assert.True(Has(prof, "ACH_BIOME_1"), "piętro 1 = pierwsza kraina");
            Assert.True(!Has(prof, "ACH_FIRST_BLOOD"), "pierwsza krew bez zabójstwa");
            Assert.True(!Has(prof, "ACH_BIOME_2"), "druga kraina za wcześnie");
            t.TakeFresh();

            var e = g.SpawnEnemy("slime", C.RCX + 200, C.RCY);
            g.KillEnemy(e);
            Pump(g, t);
            Assert.True(Has(prof, "ACH_FIRST_BLOOD"), "pierwsza krew");

            g.NewFloor(6);
            Pump(g, t);
            Assert.True(Has(prof, "ACH_BIOME_2"), "dotarcie do Katakumb");
            g.NewFloor(46);
            Pump(g, t);
            Assert.True(Has(prof, "ACH_DEPTH_45"), "Głębia 5");
            Assert.True(!Has(prof, "ACH_DEPTH_50"), "Głębia 10 za wcześnie");
            Assert.True(plat.Unlocked.Contains("ACH_DEPTH_45"), "platforma nie dostała odblokowania");
        });
    }

    [Test]
    public static void BossDefeatUnlocksBossAndNoHit()
    {
        WithProfile(prof =>
        {
            var g = NewSim();
            var t = new AchievementTracker(g, prof, new FakePlatform());
            ToBossRoom(g, 5);
            Pump(g, t);
            g.KillEnemy(g.Boss);
            Pump(g, t);
            Assert.True(Has(prof, "ACH_BOSS_1"), "pokonanie Króla Glutów");
            Assert.True(Has(prof, "ACH_BOSS_NO_HIT"), "boss bez trafienia");
            Assert.True(Has(prof, "ACH_FLOOR_NO_HIT"), "piętro bez trafienia");
            Assert.True(!Has(prof, "ACH_BOSS_2") && !Has(prof, "ACH_WIN"), "nadmiarowe odblokowania");
            var names = t.TakeFresh().Select(a => a.Api).ToList();
            Assert.True(names.Contains("ACH_BOSS_1"), "świeże odblokowania bez bossa");
        });
    }

    [Test]
    public static void BossHitAndLowHpVariants()
    {
        WithProfile(prof =>
        {
            var g = NewSim();
            var t = new AchievementTracker(g, prof, new FakePlatform());
            ToBossRoom(g, 10);
            var p = g.Players[0];
            p.HitThisRoom = p.HitThisFloor = true;
            p.Hp = p.MaxHp * 0.05f;
            g.KillEnemy(g.Boss);
            Pump(g, t);
            Assert.True(Has(prof, "ACH_BOSS_2"), "Licz");
            Assert.True(Has(prof, "ACH_BOSS_LOW_HP"), "boss na włosku");
            Assert.True(!Has(prof, "ACH_BOSS_NO_HIT"), "trafiony, a dostał Nietykalnego");
            Assert.True(!Has(prof, "ACH_FLOOR_NO_HIT"), "trafiony, a dostał piętro bez trafienia");
        });
    }

    [Test]
    public static void WinUnlocksVictoryFamily()
    {
        WithProfile(prof =>
        {
            var g = NewSim("mag", diff: 3, seal: 5);
            var t = new AchievementTracker(g, prof, new FakePlatform());
            g.RunTime = 3000;
            ToBossRoom(g, 40);
            g.KillEnemy(g.Boss);
            Pump(g, t);
            foreach (var api in new[] { "ACH_WIN", "ACH_WIN_MAG", "ACH_WIN_HARD", "ACH_WIN_NIGHTMARE", "ACH_WIN_FAST", "ACH_SEAL_5", "ACH_NO_SHOP", "ACH_MINIMALIST", "ACH_BOSS_NO_HIT" })
                Assert.True(Has(prof, api), $"po wygranej brak {api}");
            Assert.True(!Has(prof, "ACH_SEAL_10") && !Has(prof, "ACH_COOP_WIN") && !Has(prof, "ACH_WIN_RYCERZ") && !Has(prof, "ACH_ALL_CHARS"),
                "nadmiarowe osiągnięcia wygranej");
            Assert.True(!Has(prof, "ACH_BOSS_7"), "boss 7 nie został pokonany");
        });
    }

    [Test]
    public static void SlowWinDoesNotGetSprinter()
    {
        WithProfile(prof =>
        {
            var g = NewSim();
            var t = new AchievementTracker(g, prof, new FakePlatform());
            g.RunTime = 80 * 60;
            ToBossRoom(g, 40);
            g.KillEnemy(g.Boss);
            Pump(g, t);
            Assert.True(Has(prof, "ACH_WIN") && !Has(prof, "ACH_WIN_FAST") && !Has(prof, "ACH_WIN_HARD"), "wolna wygraj na normalnym");
        });
    }

    [Test]
    public static void AllCharsCountsProfileWins()
    {
        WithProfile(prof =>
        {
            foreach (var c in Content.Db.Characters.Select(c => c.Key).Where(k => k != "rycerz"))
                prof.WinsByChar[c] = 1;
            var g = NewSim("rycerz");
            var t = new AchievementTracker(g, prof, new FakePlatform());
            ToBossRoom(g, 40);
            g.KillEnemy(g.Boss);
            Pump(g, t);
            Assert.True(Has(prof, "ACH_ALL_CHARS"), "wygrana ostatnią postacią powinna dać Wszechstronnego");
            Assert.Equal((float)Content.Db.Characters.Count, AchievementTracker.ProgressOf(prof, Content.Db.Achievements.First(a => a.Api == "ACH_ALL_CHARS")), "postęp po odblokowaniu");
        });
    }

    [Test]
    public static void CharacterUnlockAchievements()
    {
        WithProfile(prof =>
        {
            var g = NewSim();
            var t = new AchievementTracker(g, prof, new FakePlatform());
            Pump(g, t);
            Assert.True(!Has(prof, "ACH_UNLOCK_ALCHEMIK"), "alchemik zablokowany");
            prof.RecordDepth(new[] { "rycerz" }, 11);
            Pump(g, t);
            Assert.True(Has(prof, "ACH_UNLOCK_ALCHEMIK"), "odblokowanie alchemika");
            Assert.True(!Has(prof, "ACH_UNLOCK_SZAMAN"), "szaman jeszcze zablokowany");
        });
    }

    [Test]
    public static void ItemAchievementsAndCollection()
    {
        WithProfile(prof =>
        {
            var g = NewSim();
            var t = new AchievementTracker(g, prof, new FakePlatform());
            var p = g.Players[0];
            foreach (var it in Content.Db.Items.Where(i => i.Rarity < 3).Take(10))
                p.ApplyItem(it, g);
            Pump(g, t);
            Assert.True(Has(prof, "ACH_ITEMS_10") && !Has(prof, "ACH_ITEMS_25"), "10 przedmiotów");
            Assert.True(!Has(prof, "ACH_LEGENDARY"), "legendarny bez legendarnego");
            Assert.Equal(10, prof.SeenItems.Count, "zapamiętane przedmioty");
            var legend = Content.Db.Items.First(i => i.Rarity >= 3);
            p.ApplyItem(legend, g);
            Pump(g, t);
            Assert.True(Has(prof, "ACH_LEGENDARY"), "legendarny przedmiot");
            // kolekcjoner: dodaj wszystko na raz
            foreach (var it in Content.Db.Items)
                if (!p.Items.Contains(it.Key)) p.Items.Add(it.Key);
            Pump(g, t);
            Assert.True(Has(prof, "ACH_ITEMS_ALL"), "Kolekcjoner");
            Assert.True(Has(prof, "ACH_ITEMS_50"), "50 przedmiotów");
        });
    }

    [Test]
    public static void SynergyNeedsAllThreeStats()
    {
        WithProfile(prof =>
        {
            var g = NewSim();
            var t = new AchievementTracker(g, prof, new FakePlatform());
            var p = g.Players[0];
            p.Homing = 1; p.Pierce = 1;
            Pump(g, t);
            Assert.True(!Has(prof, "ACH_SYNERGY"), "synergia bez odbijania");
            p.Bounce = 1;
            Pump(g, t);
            Assert.True(Has(prof, "ACH_SYNERGY"), "synergia");
        });
    }

    // ------------------------------------------------------------------ liczniki i postęp
    [Test]
    public static void TotalsAccumulateAndUnlockOnce()
    {
        WithProfile(prof =>
        {
            var plat = new FakePlatform();
            var g = NewSim();
            var t = new AchievementTracker(g, prof, plat);
            prof.Stats[AchievementTracker.StatKills] = 4998;
            var def = Content.Db.Achievements.First(a => a.Api == "ACH_KILLS_5000");
            Assert.Equal(4998f, AchievementTracker.ProgressOf(prof, def), "postęp zabójstw");
            for (int i = 0; i < 3; i++)
                g.KillEnemy(g.SpawnEnemy("slime", C.RCX + 100 + i * 10, C.RCY));
            Pump(g, t);
            Assert.Equal(5001L, prof.Stats[AchievementTracker.StatKills], "licznik łączny");
            Assert.True(Has(prof, "ACH_KILLS_5000"), "5000 zabójstw");
            Assert.Equal(1, t.TakeFresh().Count(a => a.Api == "ACH_KILLS_5000"), "świeże: raz");
            Assert.Equal(5000f, AchievementTracker.ProgressOf(prof, def), "postęp zablokowany na celu");

            // dalsze kroki i drugi tracker na tym samym profilu nie odblokowują ponownie
            for (int i = 0; i < 5; i++)
                g.KillEnemy(g.SpawnEnemy("slime", C.RCX + 100, C.RCY));
            Pump(g, t);
            var t2 = new AchievementTracker(g, prof, plat);
            Pump(g, t2);
            Assert.Equal(0, t.TakeFresh().Count, "ponowne odblokowanie (tracker 1)");
            Assert.Equal(0, t2.TakeFresh().Count, "ponowne odblokowanie (tracker 2)");
            Assert.Equal(1, plat.Unlocked.Count(a => a == "ACH_KILLS_5000"), "platforma: jedno odblokowanie");
            Assert.Equal(5006L, prof.Stats[AchievementTracker.StatKills], "tylko nowe zabójstwa (drugi tracker nie dolicza starych)");
        });
    }

    [Test]
    public static void RunProgressKeepsBestValue()
    {
        WithProfile(prof =>
        {
            var g = NewSim();
            var t = new AchievementTracker(g, prof, new FakePlatform());
            var def = Content.Db.Achievements.First(a => a.Api == "ACH_ITEMS_25");
            var p = g.Players[0];
            foreach (var it in Content.Db.Items.Take(7)) p.ApplyItem(it, g);
            Pump(g, t);
            Assert.Equal(7f, AchievementTracker.ProgressOf(prof, def), "postęp przedmiotów");
            p.Items.Clear();
            g.Stats.ItemsTaken = 0;
            Pump(g, t);
            Assert.Equal(7f, AchievementTracker.ProgressOf(prof, def), "najlepszy wynik zostaje");
        });
    }

    // ------------------------------------------------------------------ umiejętności postaci
    [Test]
    public static void MonkBulletCounterUnlocksAchievement()
    {
        WithProfile(prof =>
        {
            var g = NewSim("mnich");
            var t = new AchievementTracker(g, prof, new FakePlatform());
            g.EnterRoom(g.Floor.Rooms.Values.First(r => r.Kind == RoomKind.Combat), null);
            g.Room.Waves.Clear(); g.Enemies.Clear(); g.Spawns.Clear();
            Array.Clear(g.Room.Grid);
            var p = g.Players[0];
            p.X = C.RCX; p.Y = C.RCY;
            for (int i = 0; i < 24; i++)
                g.Bullets.Add(new Bullet(C.RCX + 220 + i * 3, C.RCY + (i % 5) * 6, 0, 0, 5, 7, Colors.Red, false));
            Run(g, t, 0.05f, _ => new SimInput { Aim = 0, Move = Vector2.Zero, Ability = true });
            Assert.True(g.Stats.KiBest >= 20, $"Krok Ki zniszczył {g.Stats.KiBest} pocisków");
            Assert.True(Has(prof, "ACH_ABILITY_MNICH"), "osiągnięcie Mnicha");
        });
    }

    [Test]
    public static void EngineerTwoSentriesAndShamanTotemKills()
    {
        WithProfile(prof =>
        {
            var g = NewSim("inzynier");
            var t = new AchievementTracker(g, prof, new FakePlatform());
            var p = g.Players[0];
            Run(g, t, 0.02f, _ => new SimInput { Ability = true });
            Assert.True(!Has(prof, "ACH_ABILITY_INZYNIER"), "jedna wieżyczka to za mało");
            p.AbilityT = 0;
            Run(g, t, 0.02f, _ => new SimInput { Ability = true });
            Assert.True(Has(prof, "ACH_ABILITY_INZYNIER"), "dwie wieżyczki naraz");
        });
        WithProfile(prof =>
        {
            var g = NewSim("szaman");
            var t = new AchievementTracker(g, prof, new FakePlatform());
            for (int i = 0; i < 10; i++)
            {
                var e = g.SpawnEnemy("slime", C.RCX + 150, C.RCY);
                e.Hp = 0.01f;
                g.DamageEnemy(e, 5, owner: g.Players[0], source: "totem");
            }
            Pump(g, t);
            Assert.Equal(10, g.Stats.TotemKills, "zabójstwa totemu");
            Assert.True(Has(prof, "ACH_ABILITY_SZAMAN"), "osiągnięcie Szamana");
        });
    }

    [Test]
    public static void GunslingerDanceCounterAndBarrel()
    {
        WithProfile(prof =>
        {
            var g = NewSim("rewolwerowiec");
            var t = new AchievementTracker(g, prof, new FakePlatform());
            var p = g.Players[0];
            for (int i = 0; i < 6; i++)
            {
                var e = g.SpawnEnemy("slime", C.RCX + 120 + i * 3, C.RCY - 60 + i * 24);
                e.Hp = 0.01f;
            }
            foreach (var e in g.Enemies) e.Grace = 0;
            for (int i = 0; i < 6; i++)
                g.DamageEnemy(g.Enemies[0], 5, owner: p, source: "last_dance");
            Pump(g, t);
            Assert.True(g.Stats.DanceBest >= 6 || g.Stats.DanceBest == g.Stats.Kills, $"licznik tańca: {g.Stats.DanceBest}");
        });
        WithProfile(prof =>
        {
            var g = NewSim();
            var t = new AchievementTracker(g, prof, new FakePlatform());
            g.EnterRoom(g.Floor.Rooms.Values.First(r => r.Kind == RoomKind.Combat), null);
            g.Room.Waves.Clear(); g.Enemies.Clear(); g.Spawns.Clear();
            Array.Clear(g.Room.Grid);
            var ob = new Obstacle(ObstacleKind.Barrel, 10, 8);
            g.Room.Grid[8, 10] = ob;
            for (int i = 0; i < 3; i++)
            {
                var e = g.SpawnEnemy("slime", ob.X + i * 10, ob.Y + 10);
                e.Hp = 1;
                e.Grace = 0;
            }
            g.DamageObstacle(ob, 999);
            Pump(g, t);
            Assert.True(g.Stats.BarrelBest >= 3, $"beczka zabiła {g.Stats.BarrelBest}");
            Assert.True(Has(prof, "ACH_BARREL"), "Pirotechnik");
        });
    }

    [Test]
    public static void CoopReviveAndBardDuo()
    {
        WithProfile(prof =>
        {
            var g = new RunSim(new[] { "bard", "rycerz" }, 1, 5) { God = true };
            g.Events.Clear();
            var t = new AchievementTracker(g, prof, new FakePlatform());
            var (a, b) = (g.Players[0], g.Players[1]);
            a.X = C.RCX - 20; b.X = C.RCX + 20; a.Y = b.Y = C.RCY;
            b.Downed = true;
            Run(g, t, 2.5f);
            Assert.True(!b.Downed, "partner nie został podniesiony");
            Assert.True(Has(prof, "ACH_REVIVE"), "podniesienie partnera");
            a.AbilityT = 0;
            Run(g, t, 0.05f, _ => new SimInput { Ability = true });
            Assert.True(Has(prof, "ACH_ABILITY_BARD"), "pieśń na obu graczach");
        });
    }

    [Test]
    public static void PlatformDefaultsToLocalAndFailsSoft()
    {
        Assert.True(PlatformService.Current is LocalPlatform, "domyślna platforma to LocalPlatform");
        PlatformService.Current.Unlock("ACH_TEST");
        PlatformService.Current.SetStat("x", 1);
        Assert.Equal("Lokalna", PlatformService.Current.Name, "nazwa platformy");
    }
}
