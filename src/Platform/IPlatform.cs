using System;

namespace Otchlan.Platform;

/// <summary>
/// Most do platformy dystrybucji (Steam, GOG, konsole…). Gra zna tylko ten interfejs; <see cref="AchievementTracker"/>
/// woła go po każdym nowym osiągnięciu i przy zmianie statystyk łącznych.
///
/// Jak podpiąć Steamworks.NET (po dodaniu paczki i steam_appid.txt – tu celowo bez NuGeta, bo budujemy offline):
/// <code>
/// public sealed class SteamPlatform : IPlatform
/// {
///     public string Name => "Steam";
///     public SteamPlatform() { if (!SteamAPI.Init()) throw new Exception("Steam niedostępny"); }
///     public void Unlock(string api)           { SteamUserStats.SetAchievement(api); SteamUserStats.StoreStats(); }
///     public void SetStat(string name, int v)  { SteamUserStats.SetStat(name, v);    SteamUserStats.StoreStats(); }
/// }
/// </code>
/// Następnie w Boot (przed pierwszą wyprawą): <c>PlatformService.Current = new SteamPlatform();</c>, a w _Process wywołać
/// <c>SteamAPI.RunCallbacks()</c>. Wartości <c>api</c> (ACH_*) z data/achievements.json muszą być zdefiniowane w panelu Steamworks
/// pod tymi samymi nazwami API; statystyki łączne mają nazwy "tot_kills" i "tot_elites".
/// </summary>
public interface IPlatform
{
    /// <summary>Nazwa platformy (do logów i ekranu osiągnięć).</summary>
    string Name { get; }

    /// <summary>Odblokowuje osiągnięcie o danym identyfikatorze (<c>api</c> z danych). Wolno wołać wielokrotnie.</summary>
    void Unlock(string api);

    /// <summary>Ustawia statystykę łączną (np. liczbę zabitych wrogów).</summary>
    void SetStat(string name, int value);
}

/// <summary>Domyślna platforma: nic nie robi (osiągnięcia żyją tylko w profilu lokalnym).</summary>
public sealed class LocalPlatform : IPlatform
{
    public string Name => "Lokalna";
    public void Unlock(string api) { }
    public void SetStat(string name, int value) { }
}

/// <summary>Aktualnie używana platforma (domyślnie <see cref="LocalPlatform"/>).</summary>
public static class PlatformService
{
    private static IPlatform _current = new LocalPlatform();

    public static IPlatform Current
    {
        get => _current;
        set => _current = value ?? new LocalPlatform();
    }
}
