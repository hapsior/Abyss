using System.Collections.Generic;
using Godot;
using Otchlan.Sim;

namespace Otchlan.View;

/// <summary>Ładowanie grafik z pamięcią podręczną i zapasem, gdy pliku brak.</summary>
public static class Art
{
    private static readonly Dictionary<string, Texture2D> Cache = new();

    public static Texture2D Tex(string path, string fallback = null)
    {
        if (Cache.TryGetValue(path, out var t))
            return t;
        if (ResourceLoader.Exists(path))
            t = GD.Load<Texture2D>(path);
        else if (fallback != null && ResourceLoader.Exists(fallback))
            t = GD.Load<Texture2D>(fallback);
        Cache[path] = t;
        return t;
    }

    public static Texture2D Character(string key) => Tex($"res://art/characters/{key}.svg", "res://art/characters/rycerz.svg");
    public static Texture2D Weapon(string key) => Tex($"res://art/weapons/{key}.svg", "res://art/weapons/miecz.svg");
    public static Texture2D Enemy(string key) => Tex($"res://art/enemies/{key}.svg", "res://art/enemies/slime.svg");
    public static Texture2D BossTex(string key) => Tex($"res://art/bosses/{key}.svg", "res://art/enemies/golem.svg");
    public static Texture2D Item(string key) => Tex($"res://art/items/{key}.svg", "res://art/items/serce_otchlani.svg");
    public static Texture2D Prop(string key) => Tex($"res://art/props/{key}.svg");
    public static Texture2D Pickup(string key) => Tex($"res://art/pickups/{key}.svg");
    public static Texture2D Ui(string key) => Tex($"res://art/ui/{key}.svg");
    public static Texture2D Projectile(string key) => Tex($"res://art/projectiles/{key}.svg");
    public static Texture2D Tile(string biome, string kind) => Tex($"res://art/tiles/{biome}_{kind}.svg", $"res://art/tiles/piwnice_{kind}.svg");

    /// <summary>Kamień w danej krainie – motyw przeszkody.</summary>
    public static Texture2D RockFor(BiomeDef b, int variant) => b.Key switch
    {
        "groty" => Prop("krysztal"),
        "grzybnia" => Prop("grzyb"),
        "lodowa_krypta" => Prop("lodowy_blok"),
        "mechanizm" => Prop("zebatka"),
        "katakumby" => variant == 0 ? Prop("skala_1") : Prop("kosci"),
        _ => Prop(variant == 0 ? "skala_1" : "skala_2"),
    };
}
