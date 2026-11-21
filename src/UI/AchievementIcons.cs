using Godot;
using Otchlan.Sim;
using Otchlan.View;

namespace Otchlan.UI;

/// <summary>
/// Mapowanie pola <c>icon</c> z data/achievements.json na istniejące grafiki. Obsługuje nazwy symboliczne (blade, crown, flame…),
/// oraz odwołania „char:klucz” (portret postaci) i „boss:klucz” (grafika bossa). Nieznana nazwa → ikona „star”.
/// </summary>
public static class AchievementIcons
{
    public static Texture2D Texture(AchievementDef d)
    {
        string icon = d?.Icon ?? "star";
        if (icon.StartsWith("char:")) return Art.Character(icon[5..]);
        if (icon.StartsWith("boss:")) return Art.BossTex(icon[5..]);
        return icon switch
        {
            "blade" => Art.Item("noz"),
            "skull" => Art.Ui("skull"),
            "drop" => Art.Item("krwawy_pakt"),
            "flame" => Art.Item("esencja_ognia"),
            "swirl" => Art.Item("oko_otchlani"),
            "crown" => Art.Item("korona"),
            "wing" => Art.Item("skrzydla"),
            "shield" => Art.Item("tarcza_energii"),
            "heart" => Art.Item("serce"),
            "hourglass" => Art.Item("klepsydra"),
            "potion" => Art.Item("kamien_filozoficzny"),
            "gem" => Art.Item("krysztal_many"),
            "book" => Art.Item("ksiega"),
            "coin" => Art.Ui("coin"),
            "plus" => Art.Item("bandaz"),
            "dice" => Art.Item("kostka"),
            "ring" => Art.Item("pierscien_satelit"),
            "rune" => Art.Item("pryzmat"),
            "bomb" => Art.Item("wybuchowe"),
            "eye" => Art.Item("trzecie_oko"),
            "frost" => Art.Item("sopel"),
            "mushroom" => Art.Item("trujacy_grzyb"),
            "gear" => Art.Item("zebatka"),
            "bolt" => Art.Item("blyskawica"),
            "gun" => Art.Item("bebenek_burzy"),
            "ki" => Art.Item("widmowe"),
            "note" => Art.Item("kapelusz"),
            "clock" => Art.Ui("clock"),
            "check" => Art.Ui("check"),
            _ => Art.Ui("star"),
        };
    }

    public static Color Tint(AchievementDef d) => d != null && !string.IsNullOrEmpty(d.Color) ? new Color(d.Color) : new Color("ffd23f");
}
