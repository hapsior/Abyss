using System;
using System.Collections.Generic;
using Godot;
using Otchlan.Sim;
using Otchlan.UI;

namespace Otchlan.View;

/// <summary>Tło pokoju: podłoga, ściany, drzwi, plamy, właz i przeszkody. Odbudowywane przy zmianie pokoju.</summary>
public partial class RoomView : Node2D
{
    private RunSim _g;
    private Room _room;
    private Node2D _tiles;
    private readonly Dictionary<Obstacle, Sprite2D> _obstacles = new();
    private Node2D _obstacleRoot;
    private float _t;

    public void Bind(RunSim g, Node2D obstacleRoot)
    {
        _g = g;
        _obstacleRoot = obstacleRoot;
    }

    private static Sprite2D TiledSprite(Texture2D tex, Rect2 rect, float tile)
    {
        float k = tex.GetWidth() / tile;
        return new Sprite2D
        {
            Texture = tex, Centered = false, Position = rect.Position, Scale = new Vector2(1 / k, 1 / k),
            RegionEnabled = true, RegionRect = new Rect2(0, 0, rect.Size.X * k, rect.Size.Y * k),
            TextureRepeat = TextureRepeatEnum.Enabled, ZIndex = -10,
        };
    }

    private void Rebuild()
    {
        _room = _g.Room;
        _tiles?.QueueFree();
        _tiles = new Node2D { ZIndex = -10 };
        AddChild(_tiles);
        MoveChild(_tiles, 0);
        var b = _g.Floor.Biome;
        var floorTex = Art.Tile(b.Key, (_room.Seed & 1) == 0 ? "floor" : "floor2");
        var wallTex = Art.Tile(b.Key, "wall");
        var inner = new Rect2(C.RX0, C.RY0, C.RX1 - C.RX0, C.RY1 - C.RY0);
        // ściany – duży prostokąt pod podłogą, sięgający poza ekran (ładne brzegi przy 21:9 i 4:3)
        var outer = new Rect2(-400, C.HudH - 400, C.W + 800, C.H - C.HudH + 800);
        if (wallTex != null) _tiles.AddChild(TiledSprite(wallTex, outer, 64));
        if (floorTex != null) _tiles.AddChild(TiledSprite(floorTex, inner, 64));

        foreach (var s in _obstacles.Values) s.QueueFree();
        _obstacles.Clear();
        foreach (var ob in _room.Obstacles())
        {
            Texture2D tex = ob.Kind switch
            {
                ObstacleKind.Crate => Art.Prop("skrzynka"),
                ObstacleKind.Barrel => Art.Prop("beczka_wybuchowa"),
                ObstacleKind.Pillar => Art.Prop("filar"),
                _ => Art.RockFor(b, ob.Variant),
            };
            if (tex == null) continue;
            float scale = C.Tile * 1.18f / tex.GetWidth();
            var s = new Sprite2D
            {
                Texture = tex, Position = new Vector2(ob.X, ob.Y - 4), Scale = new Vector2(scale, scale),
                FlipH = ((ob.Col * 7 + ob.Row * 3) & 1) == 0,
            };
            _obstacleRoot.AddChild(s);
            _obstacles[ob] = s;
        }
    }

    public void Step(float dt)
    {
        _t += dt;
        if (_g.Room != _room)
            Rebuild();
        // zniszczone przeszkody
        List<Obstacle> gone = null;
        foreach (var (ob, s) in _obstacles)
        {
            if (_room.Grid[ob.Row, ob.Col] != ob)
                (gone ??= new List<Obstacle>()).Add(ob);
            else
                s.Modulate = ob.Flash > 0 ? new Color(1.8f, 1.8f, 1.8f) : Colors.White;
        }
        if (gone != null)
            foreach (var ob in gone)
            {
                _obstacles[ob].QueueFree();
                _obstacles.Remove(ob);
            }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_room == null) return;
        var ink = UiTheme.Ink;
        var b = _g.Floor.Biome;
        // plamy
        foreach (var (pos, color, r) in _room.Stains)
        {
            var c = color.Darkened(0.45f);
            c.A = 0.55f;
            DrawSetTransform(pos, 0, new Vector2(1, 0.6f));
            DrawCircle(Vector2.Zero, r, c);
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        // cień przy górnej ścianie i obramowanie wnętrza
        for (int i = 0; i < 10; i++)
            DrawRect(new Rect2(C.RX0, C.RY0 + i * 2, C.RX1 - C.RX0, 2), new Color(0, 0, 0, 0.35f * (1 - i / 10f)));
        DrawRect(new Rect2(C.RX0 - 3, C.RY0 - 3, C.RX1 - C.RX0 + 6, C.RY1 - C.RY0 + 6), ink, false, 5);
        // drzwi
        foreach (var (d, nroom) in _room.Doors)
        {
            var p = Dirs.DoorPos(d);
            bool horiz = d is Dir.N or Dir.S;
            var size = horiz ? new Vector2(C.DoorHalf * 2, C.Wall + 6) : new Vector2(C.Wall + 6, C.DoorHalf * 2);
            var pos = d switch
            {
                Dir.N => new Vector2(p.X - C.DoorHalf, p.Y - C.Wall - 3),
                Dir.S => new Vector2(p.X - C.DoorHalf, p.Y - 3),
                Dir.W => new Vector2(p.X - C.Wall - 3, p.Y - C.DoorHalf),
                _ => new Vector2(p.X - 3, p.Y - C.DoorHalf),
            };
            var rect = new Rect2(pos, size);
            var accent = nroom.Kind switch
            {
                RoomKind.Boss => new Color("ff4d5e"),
                RoomKind.Treasure => new Color("ffd23f"),
                RoomKind.Shop => new Color("7ee05a"),
                RoomKind.Shrine => new Color("c81428"),
                RoomKind.Exit => b.AccentColor,
                _ => new Color("8e8aa6"),
            };
            DrawRect(rect.Grow(5), ink);
            DrawRect(rect.Grow(2), accent.Darkened(0.2f));
            DrawRect(rect, new Color("0d0812"));
            if (!_room.Cleared)
            {
                // kraty
                int n = 5;
                for (int i = 1; i < n; i++)
                {
                    if (horiz)
                        DrawLine(new Vector2(rect.Position.X + rect.Size.X * i / n, rect.Position.Y), new Vector2(rect.Position.X + rect.Size.X * i / n, rect.End.Y), new Color("8e8aa6"), 5);
                    else
                        DrawLine(new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y * i / n), new Vector2(rect.End.X, rect.Position.Y + rect.Size.Y * i / n), new Color("8e8aa6"), 5);
                }
            }
        }
        // właz
        if (_room.Trapdoor is { } td)
        {
            bool open = _g.TrapDelay <= 0;
            float pulse = 1 + 0.05f * MathF.Sin(_t * 4);
            DrawSetTransform(td, 0, new Vector2(1, 0.62f));
            DrawCircle(Vector2.Zero, 34 * pulse, ink);
            DrawCircle(Vector2.Zero, 29 * pulse, new Color("6b4a2e"));
            DrawCircle(Vector2.Zero, 23 * pulse, open ? new Color("05030a") : new Color("3a2a1c"));
            if (open)
            {
                var a = b.AccentColor;
                a.A = 0.35f + 0.2f * MathF.Sin(_t * 3);
                DrawArc(Vector2.Zero, 18, 0, Mathf.Tau, 32, a, 3);
            }
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
        // sklepikarz – postument z dzwonkiem (grafika docelowa w etapie 2)
        if (_room.Kind == RoomKind.Shop)
        {
            var sp = RunSim.ShopkeeperPos;
            DrawCircle(sp + new Vector2(0, 26), 30, new Color(0, 0, 0, 0.25f));
        }
    }
}
