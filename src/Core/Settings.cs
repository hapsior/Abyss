using Godot;

namespace Otchlan.Core;

public enum WindowModeSetting
{
    Okno,
    PelnyEkran,          // okno bez ramki na cały ekran (szybkie przełączanie Alt+Tab)
    PelnyEkranWylaczny,  // tryb wyłączny (najniższe opóźnienie, zmiana trybu monitora)
}

/// <summary>Ustawienia gracza: ekran, dźwięk, rozgrywka. Plik: user://settings.json.</summary>
public sealed class Settings
{
    public const string FileName = "settings.json";

    /// <summary>Dostępne limity klatek (0 = bez limitu).</summary>
    public static readonly int[] FpsLimits = { 60, 120, 144, 165, 240, 0 };

    public int Version = 1;

    // --- ekran ---
    public WindowModeSetting WindowMode = WindowModeSetting.PelnyEkran;
    public bool VSync = true;
    public int MaxFps = 0;
    public bool ShowFps = false;
    public float ScreenShake = 1.0f;

    // --- dźwięk (0..1) ---
    public float MusicVolume = 0.6f;
    public float SfxVolume = 0.8f;

    // --- rozgrywka ---
    public bool AutoAimP1 = false;
    public bool AutoAimP2 = true;
    public bool DamageNumbers = true;

    public static Settings Load()
    {
        var s = JsonStore.Load<Settings>(FileName) ?? new Settings();
        if (System.Array.IndexOf(FpsLimits, s.MaxFps) < 0)
            s.MaxFps = 0;
        return s;
    }

    public void Save() => JsonStore.Save(FileName, this);

    /// <summary>Stosuje ustawienia ekranu. Wywołuj po każdej zmianie.</summary>
    public void Apply()
    {
        if (DisplayServer.GetName() == "headless")
            return;
        var mode = WindowMode switch
        {
            WindowModeSetting.Okno => DisplayServer.WindowMode.Windowed,
            WindowModeSetting.PelnyEkranWylaczny => DisplayServer.WindowMode.ExclusiveFullscreen,
            _ => DisplayServer.WindowMode.Fullscreen,
        };
        if (DisplayServer.WindowGetMode() != mode)
            DisplayServer.WindowSetMode(mode);
        DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        // Z vsync tempo wyznacza monitor (144/165/240 Hz); limit dodatkowo ogranicza, jeśli gracz chce.
        Engine.MaxFps = MaxFps;
        AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Mathf.Max(0.0001f, SfxVolume)));
    }

    public static string FpsLabel(int fps) => fps == 0 ? "Bez limitu" : fps.ToString();

    public static string WindowModeLabel(WindowModeSetting m) => m switch
    {
        WindowModeSetting.Okno => "Okno",
        WindowModeSetting.PelnyEkranWylaczny => "Pełny ekran (wyłączny)",
        _ => "Pełny ekran",
    };
}
