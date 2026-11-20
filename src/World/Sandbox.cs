using System.Collections.Generic;
using Godot;
using Otchlan.Core;
using Otchlan.Input;
using Otchlan.UI;

namespace Otchlan.World;

/// <summary>
/// Arena testowa etapu 1: sprawdza grafikę, skalowanie do proporcji ekranu, sterowanie
/// (klawiatura+mysz / pad), podpowiedzi przycisków i płynność powyżej 60 FPS.
/// Docelowa rozgrywka (pokoje, wrogowie, przedmioty) zastąpi ją w etapie 2.
/// </summary>
public partial class Sandbox : Node2D
{
    // Pokój w jednostkach świata (1 jednostka = 1 px przy 1920×1080).
    public static readonly Rect2 Room = new(60, 156, 1800, 864);
    private const float Wall = 60;

    private Sprite2D _player, _weapon;
    private Vector2 _vel;
    private float _aim;
    private float _fireCd;
    private readonly List<Dummy> _dummies = new();
    private BulletLayer _bullets;
    private Node2D _pedestal;
    private Sprite2D _pedestalItem;
    private InputPrompt _prompt;
    private float _t;
    private float _respawnPedestal;
    private Label _info;

    private sealed class Dummy
    {
        public Sprite2D Sprite;
        public Vector2 Pos, Vel;
        public float Hp = 5, Flash, WanderT;
    }

    public static void Open()
    {
        var g = Game.I;
        g.SetWorld(new Sandbox());
        g.ShowScreen(new SandboxHud());
    }

    public override void _Ready()
    {
        var cam = new Camera2D { Position = new Vector2(960, 588), Enabled = true };
        AddChild(cam);

        AddChild(Tiled("res://art/tiles/piwnice_floor.svg", Room, new Color("463832"), 128));
        // ściany: cztery pasy wokół pokoju
        var wallColor = new Color("2b2020");
        var outer = Room.Grow(Wall);
        AddChild(Tiled("res://art/tiles/piwnice_wall.svg", new Rect2(outer.Position, new Vector2(outer.Size.X, Wall)), wallColor, 96));
        AddChild(Tiled("res://art/tiles/piwnice_wall.svg", new Rect2(outer.Position.X, Room.End.Y, outer.Size.X, Wall), wallColor, 96));
        AddChild(Tiled("res://art/tiles/piwnice_wall.svg", new Rect2(outer.Position.X, Room.Position.Y, Wall, Room.Size.Y), wallColor, 96));
        AddChild(Tiled("res://art/tiles/piwnice_wall.svg", new Rect2(Room.End.X, Room.Position.Y, Wall, Room.Size.Y), wallColor, 96));

        _pedestal = new Node2D { Position = new Vector2(1500, 420) };
        AddChild(_pedestal);
        _pedestal.AddChild(Sprite("res://art/props/filar.svg", 0.75f, new Vector2(0, 10)));
        _pedestalItem = Sprite("res://art/items/serce_otchlani.svg", 0.75f, new Vector2(0, -60));
        _pedestal.AddChild(_pedestalItem);
        _prompt = new InputPrompt(Act.Interact, "Weź: Serce Otchłani", 44) { Position = new Vector2(-200, -170), Size = new Vector2(400, 50), Visible = false };
        _pedestal.AddChild(_prompt);

        for (int i = 0; i < 5; i++)
            SpawnDummy(new Vector2(500 + i * 220, 300 + (i % 2) * 380));

        _bullets = new BulletLayer();
        AddChild(_bullets);

        _player = Sprite("res://art/characters/rycerz.svg", 0.42f, Vector2.Zero);
        _player.Position = new Vector2(960, 640);
        AddChild(_player);
        _weapon = Sprite("res://art/weapons/miecz.svg", 0.5f, Vector2.Zero);
        _weapon.Offset = new Vector2(96 - 44, 0); // pivot rękojeści (44,48) z pliku SVG
        AddChild(_weapon);
    }

    private static Sprite2D Sprite(string path, float scale, Vector2 pos)
    {
        var s = new Sprite2D { Position = pos, Scale = new Vector2(scale, scale) };
        if (ResourceLoader.Exists(path))
            s.Texture = GD.Load<Texture2D>(path);
        return s;
    }

    private static Node2D Tiled(string texPath, Rect2 rect, Color fallback, float tileSize)
    {
        if (!ResourceLoader.Exists(texPath))
        {
            var poly = new Polygon2D
            {
                Color = fallback,
                Polygon = new[] { rect.Position, new Vector2(rect.End.X, rect.Position.Y), rect.End, new Vector2(rect.Position.X, rect.End.Y) },
            };
            return poly;
        }
        var tex = GD.Load<Texture2D>(texPath);
        float k = tex.GetWidth() / tileSize;
        return new Sprite2D
        {
            Texture = tex,
            Centered = false,
            Position = rect.Position,
            Scale = new Vector2(1 / k, 1 / k),
            RegionEnabled = true,
            RegionRect = new Rect2(0, 0, rect.Size.X * k, rect.Size.Y * k),
            TextureRepeat = TextureRepeatEnum.Enabled,
        };
    }

    private void SpawnDummy(Vector2 pos)
    {
        var d = new Dummy { Pos = pos, Sprite = Sprite("res://art/enemies/slime.svg", 0.36f, pos) };
        AddChild(d.Sprite);
        _dummies.Add(d);
    }

    public override void _Process(double delta)
    {
        float dt = Mathf.Min((float)delta, 1 / 30f);
        _t += dt;
        var im = Game.I.InputMgr;

        // --- ruch ---
        var move = Godot.Input.GetVector(Act.MoveLeft, Act.MoveRight, Act.MoveUp, Act.MoveDown);
        _vel = _vel.Lerp(move * 360, 1 - Mathf.Exp(-14 * dt));
        var p = _player.Position + _vel * dt;
        p = p.Clamp(Room.Position + new Vector2(30, 40), Room.End - new Vector2(30, 30));
        _player.Position = p;

        // --- celowanie ---
        bool wantFire = Godot.Input.IsActionPressed(Act.Fire);
        if (im.LastDevice == InputDevice.Gamepad && im.LastJoypad >= 0)
        {
            var rs = new Vector2(Godot.Input.GetJoyAxis(im.LastJoypad, JoyAxis.RightX), Godot.Input.GetJoyAxis(im.LastJoypad, JoyAxis.RightY));
            if (rs.Length() > 0.35f)
            {
                _aim = rs.Angle();
                wantFire = true;
            }
            else if (move.Length() > 0.2f && !wantFire)
                _aim = Mathf.LerpAngle(_aim, move.Angle(), 1 - Mathf.Exp(-8 * dt));
        }
        else
            _aim = (GetGlobalMousePosition() - _player.Position).Angle();
        var shoot = Godot.Input.GetVector(Act.ShootLeft, Act.ShootRight, Act.ShootUp, Act.ShootDown);
        if (shoot.Length() > 0.1f)
        {
            _aim = shoot.Angle();
            wantFire = true;
        }

        // --- wygląd gracza: podskok, zgniecenie, odbicie w stronę celowania ---
        bool facingLeft = Mathf.Abs(Mathf.Wrap(_aim, -Mathf.Pi, Mathf.Pi)) > Mathf.Pi / 2;
        float speedK = Mathf.Clamp(_vel.Length() / 360f, 0, 1);
        float bob = Mathf.Abs(Mathf.Sin(_t * 12)) * 8 * speedK;
        float breathe = 1 + 0.035f * Mathf.Sin(_t * 3);
        _player.Scale = new Vector2(0.42f * (facingLeft ? -1 : 1) * (2 - breathe), 0.42f * breathe);
        _player.Offset = new Vector2(0, -bob / 0.42f);
        _player.Rotation = _vel.X / 360f * 0.12f;
        _weapon.Position = _player.Position + new Vector2(0, 8) + Vector2.FromAngle(_aim) * 26;
        _weapon.Rotation = _aim;
        _weapon.Scale = new Vector2(0.5f, facingLeft ? -0.5f : 0.5f);
        _weapon.ZIndex = Mathf.Sin(_aim) < -0.3f ? -1 : 1;

        // --- strzelanie ---
        _fireCd -= dt;
        if (wantFire && _fireCd <= 0)
        {
            _fireCd = 0.16f;
            var dir = Vector2.FromAngle(_aim);
            _bullets.Spawn(_weapon.Position + dir * 60, dir * 900, 1.2f);
        }
        _bullets.Tick(dt, Room);

        // --- manekiny ---
        foreach (var d in _dummies)
        {
            d.WanderT -= dt;
            if (d.WanderT <= 0)
            {
                d.WanderT = (float)GD.RandRange(0.6, 1.6);
                d.Vel = Vector2.FromAngle((float)GD.RandRange(0, Mathf.Tau)) * (float)GD.RandRange(30, 110);
            }
            d.Pos = (d.Pos + d.Vel * dt).Clamp(Room.Position + new Vector2(40, 50), Room.End - new Vector2(40, 30));
            float hop = Mathf.Abs(Mathf.Sin(_t * 7 + d.Pos.X * 0.01f));
            d.Sprite.Position = d.Pos - new Vector2(0, hop * 10);
            d.Sprite.Scale = new Vector2(0.36f * (1 + 0.08f * (1 - hop)) * (d.Pos.X > _player.Position.X ? -1 : 1), 0.36f * (1 - 0.06f * (1 - hop)));
            d.Flash = Mathf.Max(0, d.Flash - dt * 6);
            d.Sprite.Modulate = d.Flash > 0 ? new Color(3, 3, 3) : Colors.White;
            if (_bullets.HitCircle(d.Pos, 36))
            {
                d.Hp -= 1;
                d.Flash = 1;
                if (d.Hp <= 0)
                {
                    d.Hp = 5;
                    d.Pos = new Vector2((float)GD.RandRange(Room.Position.X + 100, Room.End.X - 100), (float)GD.RandRange(Room.Position.Y + 100, Room.End.Y - 100));
                }
            }
        }

        // --- piedestał: podejdź i naciśnij przycisk interakcji ---
        if (_respawnPedestal > 0)
        {
            _respawnPedestal -= dt;
            if (_respawnPedestal <= 0)
                _pedestalItem.Visible = true;
        }
        bool near = _pedestalItem.Visible && _player.Position.DistanceTo(_pedestal.Position) < 140;
        _prompt.Visible = near;
        _pedestalItem.Position = new Vector2(0, -64 - 8 * Mathf.Sin(_t * 2.5f));
        if (near && Godot.Input.IsActionJustPressed(Act.Interact))
        {
            _pedestalItem.Visible = false;
            _respawnPedestal = 2;
        }

        if (Godot.Input.IsActionJustPressed(Act.Pause))
        {
            Game.I.SetWorld(null);
            Game.I.ShowScreen(new MainMenu());
        }
    }
}

/// <summary>Pociski: tablica danych + jedno rysowanie (wydajne przy setkach pocisków).</summary>
public partial class BulletLayer : Node2D
{
    private struct B
    {
        public Vector2 Pos, Vel;
        public float Life;
    }

    private readonly List<B> _list = new();
    private Texture2D _tex;

    public override void _Ready()
    {
        const string path = "res://art/projectiles/p_orb.svg";
        _tex = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        ZIndex = 2;
    }

    public void Spawn(Vector2 pos, Vector2 vel, float life) => _list.Add(new B { Pos = pos, Vel = vel, Life = life });

    public void Tick(float dt, Rect2 room)
    {
        for (int i = _list.Count - 1; i >= 0; i--)
        {
            var b = _list[i];
            b.Pos += b.Vel * dt;
            b.Life -= dt;
            if (b.Life <= 0 || !room.HasPoint(b.Pos))
                _list.RemoveAt(i);
            else
                _list[i] = b;
        }
        QueueRedraw();
    }

    public bool HitCircle(Vector2 c, float r)
    {
        for (int i = _list.Count - 1; i >= 0; i--)
            if (_list[i].Pos.DistanceSquaredTo(c) < r * r)
            {
                _list.RemoveAt(i);
                return true;
            }
        return false;
    }

    public override void _Draw()
    {
        foreach (var b in _list)
        {
            if (_tex != null)
                DrawTextureRect(_tex, new Rect2(b.Pos - new Vector2(16, 16), new Vector2(32, 32)), false);
            else
            {
                DrawCircle(b.Pos, 13, UiTheme.Ink);
                DrawCircle(b.Pos, 9, new Color("aee0ff"));
            }
        }
    }
}

/// <summary>Pasek podpowiedzi sterowania na arenie testowej.</summary>
public partial class SandboxHud : Control
{
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        var title = UiTheme.MakeLabel("Arena testowa – etap 1", "SmallLabel");
        title.Position = new Vector2(70, 40);
        AddChild(title);
        var bar = InputPrompt.Bar(40, (Act.GlyphMove, "Ruch"), (Act.GlyphAim, "Celuj"), (Act.Fire, "Strzał"), (Act.Dash, "Zryw"),
            (Act.Interact, "Weź"), (Act.Pause, "Menu"));
        UiTheme.Place(bar, LayoutPreset.CenterTop, new Vector2(-600, 30), new Vector2(1200, 50));
        AddChild(bar);
    }
}
