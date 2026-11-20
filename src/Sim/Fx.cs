using Godot;

namespace Otchlan.Sim;

/// <summary>
/// Zdarzenia wizualne i dźwiękowe zgłaszane przez symulację. Widok (Godot) je rysuje i odtwarza;
/// w testach i symulacjach balansu używamy <see cref="NullFx"/>. API odpowiada particles.* z wersji 1.x.
/// </summary>
public interface IFx
{
    void Burst(float x, float y, Color color, int n, float speed, float size, float life, float grav = 0, bool glow = false, string kind = "dot");
    void Ring(float x, float y, Color color, float radius, float life, float width = 4);
    void Text(float x, float y, string text, Color color, float size, float life = 0.8f);
    void Spark(float x, float y, Color color, int n, float speed, float angle, float spread = 0.6f, float life = 0.15f);
    void Bolt(float x1, float y1, float x2, float y2);
    void Shake(float amount);
    void Sound(string name, float volume = 1f, float pitchVar = 0f);
    void Banner(string title, string sub, Color color, bool priority = false);
    void Stain(float x, float y, Color color, float r);
    void HurtFlash();
}

public sealed class NullFx : IFx
{
    public static readonly NullFx I = new();
    public void Burst(float x, float y, Color color, int n, float speed, float size, float life, float grav = 0, bool glow = false, string kind = "dot") { }
    public void Ring(float x, float y, Color color, float radius, float life, float width = 4) { }
    public void Text(float x, float y, string text, Color color, float size, float life = 0.8f) { }
    public void Spark(float x, float y, Color color, int n, float speed, float angle, float spread = 0.6f, float life = 0.15f) { }
    public void Bolt(float x1, float y1, float x2, float y2) { }
    public void Shake(float amount) { }
    public void Sound(string name, float volume = 1f, float pitchVar = 0f) { }
    public void Banner(string title, string sub, Color color, bool priority = false) { }
    public void Stain(float x, float y, Color color, float r) { }
    public void HurtFlash() { }
}
