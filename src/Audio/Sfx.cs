using System;
using System.Collections.Generic;
using Godot;
using Otchlan.Core;

namespace Otchlan.Audio;

/// <summary>
/// Odtwarzanie efektów dźwiękowych po nazwie (np. "shoot", "hit", "explosion"). Pliki: res://audio/sfx/&lt;nazwa&gt;.wav.
/// Brak pliku = cisza (bez błędu). Pula odtwarzaczy, żeby wiele dźwięków mogło grać naraz.
/// </summary>
public static class Sfx
{
    private static readonly Dictionary<string, AudioStream> Cache = new();
    private static readonly List<AudioStreamPlayer> Pool = new();
    private static readonly Dictionary<string, ulong> LastPlayed = new();
    private static readonly Random R = new();

    private static AudioStream Load(string name)
    {
        if (Cache.TryGetValue(name, out var s)) return s;
        foreach (var ext in new[] { "wav", "ogg" })
        {
            var path = $"res://audio/sfx/{name}.{ext}";
            if (ResourceLoader.Exists(path))
            {
                s = GD.Load<AudioStream>(path);
                break;
            }
        }
        Cache[name] = s;
        return s;
    }

    public static void Play(string name, float volume = 1f, float pitchVar = 0f)
    {
        if (Game.I == null || string.IsNullOrEmpty(name)) return;
        var stream = Load(name);
        if (stream == null) return;
        // ten sam dźwięk nie częściej niż co 30 ms (np. wiele trafień w jednej klatce)
        ulong now = Time.GetTicksMsec();
        if (LastPlayed.TryGetValue(name, out var last) && now - last < 30) return;
        LastPlayed[name] = now;
        AudioStreamPlayer pl = null;
        foreach (var p in Pool)
            if (!p.Playing) { pl = p; break; }
        if (pl == null)
        {
            if (Pool.Count >= 24) return;
            pl = new AudioStreamPlayer { Bus = "Master" };
            Game.I.AddChild(pl);
            Pool.Add(pl);
        }
        pl.Stream = stream;
        pl.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.0001f, volume * Game.I.Settings.SfxVolume));
        pl.PitchScale = 1 + (pitchVar > 0 ? ((float)R.NextDouble() * 2 - 1) * pitchVar * 2 : 0);
        pl.Play();
    }
}
