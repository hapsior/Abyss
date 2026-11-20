using Godot;

namespace Otchlan.Sim;

/// <summary>
/// Stałe świata gry. Symulacja działa w jednostkach wersji 1.x (pokój 1280×720),
/// a widok skaluje świat ×1.5 do rozdzielczości bazowej 1920×1080. Dzięki temu liczby
/// (prędkości, promienie, zasięgi) przenosimy z Pythona 1:1.
/// </summary>
public static class C
{
    public const float W = 1280, H = 720;
    public const float ViewScale = 1.5f;

    public const float HudH = 64;
    public const float Wall = 40;
    public const float RX0 = Wall;
    public const float RY0 = HudH + Wall;
    public const float RX1 = W - Wall;
    public const float RY1 = H - Wall;
    public const float RCX = (RX0 + RX1) / 2; // 640
    public const float RCY = (RY0 + RY1) / 2; // 392

    public const float Tile = 48;
    public const int GCols = 25;
    public const int GRows = 12;
    public const float DoorHalf = 52;

    public const float Tau = Mathf.Tau;

    /// <summary>Pięter w krainie i liczba krain przed Głębią.</summary>
    public const int FloorsPerBiome = 5;
    public const int StoryFloors = 40;

    public static readonly Color Ink = new("100c14");
    public static readonly Color White = new("f5f0eb");
    public static readonly Color Gold = new("ffbe3c");
    public static readonly Color EnemyBullet = new("ff5a78");
    public static readonly Color EnemyBullet2 = new("ffaa3c");
    public static readonly Color EnemyBullet3 = new("be78ff");
    public static readonly Color HpRed = new("dc3c46");
    public static readonly Color XpGreen = new("78dc78");

    public static readonly string[] RarityNames = { "Zwykły", "Niezwykły", "Rzadki", "Legendarny" };
    public static readonly Color[] RarityColors = { new("d2d2dc"), new("5aaaff"), new("be69ff"), new("ff5555") };

    public static Vector2 RoomCenter => new(RCX, RCY);

    public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
}

public enum Dir { N, S, W, E }

public static class Dirs
{
    public static readonly Dir[] All = { Dir.N, Dir.S, Dir.W, Dir.E };

    public static (int dx, int dy) Delta(Dir d) => d switch
    {
        Dir.N => (0, -1),
        Dir.S => (0, 1),
        Dir.W => (-1, 0),
        _ => (1, 0),
    };

    public static Dir Opposite(Dir d) => d switch
    {
        Dir.N => Dir.S,
        Dir.S => Dir.N,
        Dir.W => Dir.E,
        _ => Dir.W,
    };

    /// <summary>Punkt na krawędzi pokoju, gdzie są drzwi.</summary>
    public static Vector2 DoorPos(Dir d) => d switch
    {
        Dir.N => new Vector2(C.RCX, C.RY0),
        Dir.S => new Vector2(C.RCX, C.RY1),
        Dir.W => new Vector2(C.RX0, C.RCY),
        _ => new Vector2(C.RX1, C.RCY),
    };
}
