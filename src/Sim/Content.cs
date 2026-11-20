using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Otchlan.Sim;

// Definicje treści gry wczytywane z res://data/*.json. Zachowania (SI, ataki, efekty) są w kodzie.

public sealed class CharacterDef
{
    public string Key, Name, Title, Color, Accent, Bullet, BulletColor, Sound, Passive, Ability, AbilityDesc, Weapon;
    public UnlockDef Unlock;
    public float AbilityCd;
    public Dictionary<string, float> Stats = new();
    public float[] WeaponPivot = { 44, 48 };
    [JsonIgnore] public Color BodyColor => new(Color);
    [JsonIgnore] public Color BulletCol => new(BulletColor);
}

public sealed class UnlockDef
{
    public string Kind, Text;
    public float Value;
}

public sealed class FxDef
{
    public string Stat, Op;
    public float Value;
}

public sealed class ItemDef
{
    public string Key, Name, Desc, Special;
    public int Rarity;
    public List<FxDef> Fx = new();
}

public sealed class UpgradeDef
{
    public string Key, Name, Desc, Stat, Op;
    public float[] Values;
}

public sealed class DifficultyDef
{
    public string Key, Name, Color;
    public string[] Desc;
    public float EnemyHp, EnemyDmg, BulletSpeed, EnemyCount, Elite, Heal, Price, FireRate, Lead;
    public int ExtraBullets;
}

public sealed class SealDef
{
    public string Name, Desc;
    public Dictionary<string, float> Mods = new();
}

public sealed class EnemyDef
{
    public string Key, Cls, Name, Color;
    public float Hp, Radius, Speed, Contact, Xp, Coin, KnockResist, Cost;
    public bool Flying, Ghost;
}

public sealed class BossDef
{
    public string Key, Cls, Name, Title, Color;
    public float Radius, Speed, Contact, Hp;
    public bool Flying;
    public string[] Attacks;
    public float[] Phases, Rest;
}

public sealed class PoolEntry
{
    public string Enemy;
    public float Weight;
}

public sealed class BiomeTint
{
    public float Hue, Sat = 1, Val = 1;
}

public sealed class BiomeDef
{
    public string Key, Name, Floor, Floor2, Wall, WallTop, Accent, Decor, Boss;
    public List<PoolEntry> Pool = new();
    public BiomeTint Tint = new();
    [JsonIgnore] public Color AccentColor => new(Accent);
}

public sealed class AchievementDef
{
    public string Api, Name, Desc, Icon, Color;
    public bool Hidden;
    public float? Goal;
}

/// <summary>Baza treści gry (singleton). Wczytywana leniwie przy pierwszym użyciu.</summary>
public sealed class Content
{
    private static Content _db;
    public static Content Db => _db ??= Load();

    public List<CharacterDef> Characters;
    public List<ItemDef> Items;
    public List<UpgradeDef> Upgrades;
    public List<DifficultyDef> Difficulties;
    public List<SealDef> Seals;
    public List<EnemyDef> Enemies;
    public List<BossDef> Bosses;
    public List<BiomeDef> Biomes;
    public List<AchievementDef> Achievements;

    public Dictionary<string, CharacterDef> CharByKey;
    public Dictionary<string, ItemDef> ItemByKey;
    public Dictionary<string, EnemyDef> EnemyByKey;
    public Dictionary<string, BossDef> BossByKey;

    private static readonly JsonSerializerOptions Opt = new()
    {
        IncludeFields = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private static List<T> Read<T>(string name)
    {
        var text = Godot.FileAccess.GetFileAsString($"res://data/{name}.json");
        if (string.IsNullOrEmpty(text))
            throw new Exception($"Brak pliku danych: res://data/{name}.json");
        return JsonSerializer.Deserialize<List<T>>(text, Opt);
    }

    public static Content Load()
    {
        var c = new Content
        {
            Characters = Read<CharacterDef>("characters"),
            Items = Read<ItemDef>("items"),
            Upgrades = Read<UpgradeDef>("upgrades"),
            Difficulties = Read<DifficultyDef>("difficulties"),
            Seals = Read<SealDef>("seals"),
            Enemies = Read<EnemyDef>("enemies"),
            Bosses = Read<BossDef>("bosses"),
            Biomes = Read<BiomeDef>("biomes"),
            Achievements = Read<AchievementDef>("achievements"),
        };
        c.CharByKey = c.Characters.ToDictionary(x => x.Key);
        c.ItemByKey = c.Items.ToDictionary(x => x.Key);
        c.EnemyByKey = c.Enemies.ToDictionary(x => x.Key);
        c.BossByKey = c.Bosses.ToDictionary(x => x.Key);
        return c;
    }

    /// <summary>Kraina dla piętra (1..∞). Po 40. piętrze (Głębia) krainy krążą od nowa.</summary>
    public BiomeDef BiomeFor(int depth)
    {
        int i = (depth - 1) / C.FloorsPerBiome;
        return Biomes[i % Biomes.Count];
    }
}
