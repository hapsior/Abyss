using System;
using System.Collections.Generic;
using Godot;
using Otchlan.Core;

namespace Otchlan.Sim;

public enum RoomKind { Start, Combat, Treasure, Shop, Shrine, Exit, Boss }

public enum ObstacleKind { Rock, Crate, Barrel, Pillar }

/// <summary>Przeszkoda na siatce pokoju (kamień, skrzynia, beczka wybuchowa, filar).</summary>
public sealed class Obstacle
{
    public ObstacleKind Kind;
    public int Col, Row;
    public float Hp;
    public bool Solid = true;
    public float Flash;
    public int Variant;

    public float X => C.RX0 + Col * C.Tile + C.Tile / 2;
    public float Y => C.RY0 + Row * C.Tile + C.Tile / 2;

    public Obstacle(ObstacleKind kind, int col, int row, int variant = 0)
    {
        Kind = kind;
        Col = col;
        Row = row;
        Variant = variant;
        Hp = kind switch { ObstacleKind.Crate => 28, ObstacleKind.Barrel => 12, _ => 99999 };
    }

    public bool Breakable => Kind is ObstacleKind.Crate or ObstacleKind.Barrel;
}

/// <summary>Pokój: siatka 25×12 przeszkód, drzwi, zawartość. Geometria jak w wersji 1.x.</summary>
public sealed class Room
{
    public int Gx, Gy;
    public RoomKind Kind;
    public readonly Dictionary<Dir, Room> Doors = new();
    public Obstacle[,] Grid = new Obstacle[C.GRows, C.GCols];
    /// <summary>Licznik zmian siatki (zniszczone przeszkody) – do unieważniania pól przepływu.</summary>
    public int Version;
    public bool Visited, Seen, Cleared, RewardGiven;
    public List<List<string>> Waves = new();
    public readonly List<Pickup> Pickups = new();
    public readonly List<Pedestal> Pedestals = new();
    public Vector2? Trapdoor;
    public int Dist;
    public int Seed;
    /// <summary>Plamy na podłodze (po wrogach, wybuchach) – zapamiętywane w pokoju.</summary>
    public readonly List<(Vector2 pos, Color color, float r)> Stains = new();

    public Room(int gx, int gy, RoomKind kind, int seed)
    {
        Gx = gx;
        Gy = gy;
        Kind = kind;
        Seed = seed;
        Cleared = kind is not (RoomKind.Combat or RoomKind.Boss or RoomKind.Exit);
    }

    public bool IsFight => Kind is RoomKind.Combat or RoomKind.Boss or RoomKind.Exit;

    // ------------------------------------------------------------------ geometria
    public Obstacle TileAt(float x, float y)
    {
        int col = (int)MathF.Floor((x - C.RX0) / C.Tile);
        int row = (int)MathF.Floor((y - C.RY0) / C.Tile);
        if (col >= 0 && col < C.GCols && row >= 0 && row < C.GRows)
            return Grid[row, col];
        return null;
    }

    public bool SolidAt(float x, float y) => TileAt(x, y) is { Solid: true };

    public bool Solid(int col, int row)
    {
        if (col >= 0 && col < C.GCols && row >= 0 && row < C.GRows)
            return Grid[row, col] is { Solid: true };
        return true;
    }

    public static int ColOf(float x) => (int)MathF.Floor((x - C.RX0) / C.Tile);
    public static int RowOf(float y) => (int)MathF.Floor((y - C.RY0) / C.Tile);
    public static Vector2 CellCenter(int col, int row) => new(C.RX0 + col * C.Tile + C.Tile / 2, C.RY0 + row * C.Tile + C.Tile / 2);

    /// <summary>Wypycha okrąg z przeszkód i ścian (jak resolve_circle w 1.x).</summary>
    public Vector2 ResolveCircle(float x, float y, float r)
    {
        x = C.Clamp(x, C.RX0 + r, C.RX1 - r);
        y = C.Clamp(y, C.RY0 + r, C.RY1 - r);
        int c0 = ColOf(x - r), c1 = ColOf(x + r), r0 = RowOf(y - r), r1 = RowOf(y + r);
        for (int row = Math.Max(0, r0); row <= Math.Min(C.GRows - 1, r1); row++)
            for (int col = Math.Max(0, c0); col <= Math.Min(C.GCols - 1, c1); col++)
            {
                var ob = Grid[row, col];
                if (ob == null || !ob.Solid)
                    continue;
                float rx = C.RX0 + col * C.Tile + 2, ry = C.RY0 + row * C.Tile + 2, rw = C.Tile - 4;
                float cx = C.Clamp(x, rx, rx + rw), cy = C.Clamp(y, ry, ry + rw);
                float dx = x - cx, dy = y - cy, d2 = dx * dx + dy * dy;
                if (d2 >= r * r)
                    continue;
                if (d2 > 1e-6f)
                {
                    float d = MathF.Sqrt(d2), push = r - d;
                    x += dx / d * push;
                    y += dy / d * push;
                }
                else
                {
                    float left = x - rx, right = rx + rw - x, top = y - ry, bottom = ry + rw - y;
                    float m = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
                    if (m == left) x = rx - r;
                    else if (m == right) x = rx + rw + r;
                    else if (m == top) y = ry - r;
                    else y = ry + rw + r;
                }
            }
        x = C.Clamp(x, C.RX0 + r, C.RX1 - r);
        y = C.Clamp(y, C.RY0 + r, C.RY1 - r);
        return new Vector2(x, y);
    }

    public Vector2 ResolveCircle(Vector2 p, float r) => ResolveCircle(p.X, p.Y, r);

    /// <summary>Linia wzroku: czy odcinek nie przechodzi przez stałą przeszkodę (próbkowanie co 12 jednostek).</summary>
    public bool Los(float x1, float y1, float x2, float y2)
    {
        float d = MathF.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
        int n = (int)(d / 12) + 1;
        for (int i = 1; i < n; i++)
        {
            float t = i / (float)n;
            if (SolidAt(x1 + (x2 - x1) * t, y1 + (y2 - y1) * t))
                return false;
        }
        return true;
    }

    /// <summary>
    /// „Gruba” linia wzroku: środek i dwie równoległe linie ±w. Pocisk o promieniu ~w przeleci,
    /// więc strzelec nie strzela w krawędź kamienia (problem snajpera z wersji 1.0).
    /// </summary>
    public bool LosThick(float x1, float y1, float x2, float y2, float w = 7)
    {
        if (!Los(x1, y1, x2, y2))
            return false;
        float dx = x2 - x1, dy = y2 - y1, d = MathF.Sqrt(dx * dx + dy * dy);
        if (d < 1)
            return true;
        float nx = -dy / d * w, ny = dx / d * w;
        return Los(x1 + nx, y1 + ny, x2 + nx, y2 + ny) && Los(x1 - nx, y1 - ny, x2 - nx, y2 - ny);
    }

    /// <summary>Promień do pierwszej przeszkody lub ściany (np. dla lasera snajpera).</summary>
    public Vector2 Raycast(float x, float y, float angle, float maxLen = 2000)
    {
        float cx = MathF.Cos(angle), sy = MathF.Sin(angle);
        for (float t = 0; t < maxLen; t += 6)
        {
            float px = x + cx * t, py = y + sy * t;
            if (px < C.RX0 || px > C.RX1 || py < C.RY0 || py > C.RY1 || SolidAt(px, py))
                return new Vector2(px, py);
        }
        return new Vector2(x + cx * maxLen, y + sy * maxLen);
    }

    public List<(int col, int row)> FreeTiles()
    {
        var list = new List<(int, int)>();
        for (int r = 0; r < C.GRows; r++)
            for (int c = 0; c < C.GCols; c++)
                if (Grid[r, c] == null)
                    list.Add((c, r));
        return list;
    }

    public IEnumerable<Obstacle> Obstacles()
    {
        for (int r = 0; r < C.GRows; r++)
            for (int c = 0; c < C.GCols; c++)
                if (Grid[r, c] != null)
                    yield return Grid[r, c];
    }

    public void RemoveObstacle(Obstacle ob)
    {
        if (Grid[ob.Row, ob.Col] == ob)
            Grid[ob.Row, ob.Col] = null;
            Version++;
    }

    /// <summary>Pole odległości BFS do celu – wrogowie schodzą po nim do gracza omijając przeszkody.</summary>
    public int[,] FlowField(float tx, float ty)
    {
        int tc = Math.Clamp(ColOf(tx), 0, C.GCols - 1);
        int tr = Math.Clamp(RowOf(ty), 0, C.GRows - 1);
        var dist = new int[C.GRows, C.GCols];
        for (int r = 0; r < C.GRows; r++)
            for (int c = 0; c < C.GCols; c++)
                dist[r, c] = 9999;
        dist[tr, tc] = 0;
        var q = new Queue<(int c, int r)>();
        q.Enqueue((tc, tr));
        while (q.Count > 0)
        {
            var (c, r) = q.Dequeue();
            int d = dist[r, c] + 1;
            foreach (var (dc, dr) in Steps4)
            {
                int nc = c + dc, nr = r + dr;
                if (nc >= 0 && nc < C.GCols && nr >= 0 && nr < C.GRows && dist[nr, nc] > d && !Solid(nc, nr))
                {
                    dist[nr, nc] = d;
                    q.Enqueue((nc, nr));
                }
            }
        }
        return dist;
    }

    public static readonly (int, int)[] Steps4 = { (1, 0), (-1, 0), (0, 1), (0, -1) };
    public static readonly (int, int)[] Steps8 = { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) };

    /// <summary>Strefy przy drzwiach i środek krawędzi muszą zostać wolne.</summary>
    public static bool Forbidden(int col, int row)
    {
        if (row is >= 4 and <= 7 && (col <= 3 || col >= C.GCols - 4))
            return true;
        if (col is >= 10 and <= 14 && (row <= 2 || row >= C.GRows - 3))
            return true;
        return false;
    }
}
