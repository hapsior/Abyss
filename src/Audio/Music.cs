using Godot;
using Otchlan.Core;

namespace Otchlan.Audio;

/// <summary>
/// Muzyka w tle z płynnym przejściem między utworami (res://audio/music/&lt;nazwa&gt;.wav|ogg, zapętlone w imporcie).
/// <see cref="Play"/> z tą samą nazwą nic nie robi, więc można ją wołać co klatkę (np. „boss” / „explore”).
/// </summary>
public partial class Music : Node
{
    public static Music I { get; private set; }

    private AudioStreamPlayer _a, _b;
    private string _current;
    private float _fade = 1;
    private const float FadeTime = 1.2f;

    public override void _EnterTree() => I = this;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _a = new AudioStreamPlayer { Name = "A", Bus = "Master" };
        _b = new AudioStreamPlayer { Name = "B", Bus = "Master" };
        AddChild(_a);
        AddChild(_b);
    }

    public static void Play(string name) => I?.Switch(name);

    public static void Stop() => I?.Switch(null);

    private void Switch(string name)
    {
        if (name == _current) return;
        _current = name;
        AudioStream stream = null;
        if (name != null)
            foreach (var ext in new[] { "ogg", "wav" })
            {
                var path = $"res://audio/music/{name}.{ext}";
                if (ResourceLoader.Exists(path)) { stream = GD.Load<AudioStream>(path); break; }
            }
        (_a, _b) = (_b, _a);          // _a = nowy, _b = wygasający
        _a.Stop();
        _a.Stream = stream;
        _a.VolumeDb = -80;
        if (stream != null) _a.Play();
        _fade = 0;
    }

    private float Target => Game.I?.Settings.MusicVolume ?? 0.6f;

    public override void _Process(double delta)
    {
        _fade = Mathf.Min(1, _fade + (float)delta / FadeTime);
        float vol = Target;
        _a.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.0001f, vol * _fade));
        _b.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.0001f, vol * (1 - _fade)));
        if (_fade >= 1 && _b.Playing) _b.Stop();
    }
}
