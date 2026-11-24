using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Otchlan.Sim;
using Otchlan.UI;

namespace Otchlan.View;

/// <summary>Węzeł jednej postaci/wroga/znajdźki: cień i paski rysowane w _Draw, grafika jako Sprite2D.</summary>
public partial class EntityNode : Node2D
{
    public Sprite2D Body, Weapon, Badge;
    public ShaderMaterial Mat;
    public float ShadowW = 30, ShadowH = 10, ShadowY = 14, ShadowA = 0.28f;
    public float HpFrac = 1, BarW, BarY;
    public Color BarColor = new("e64650");
    public Color? RingColor;
    public float RingR;
    public float ReviveFrac = -1;
    public string Tag;
    public Color TagColor;
    public bool Seen;

    public override void _Draw()
    {
        DrawSetTransform(new Vector2(0, ShadowY), 0, new Vector2(1, ShadowH / MathF.Max(1, ShadowW)));
        DrawCircle(Vector2.Zero, ShadowW / 2, new Color(0, 0, 0, ShadowA));
        if (RingColor is { } rc)
            DrawArc(Vector2.Zero, RingR, 0, Mathf.Tau, 32, rc, 2.5f);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        if (BarW > 0 && HpFrac < 0.999f)
        {
            var r = new Rect2(-BarW / 2, BarY, BarW, 4);
            DrawRect(r.Grow(2), UiTheme.Ink);
            DrawRect(r, new Color("3c141a"));
            DrawRect(new Rect2(r.Position, new Vector2(r.Size.X * Mathf.Clamp(HpFrac, 0, 1), r.Size.Y)), BarColor);
        }
        if (ReviveFrac >= 0)
        {
            DrawArc(new Vector2(0, -34), 12, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * ReviveFrac, 24, new Color("8cff8c"), 4);
            DrawArc(new Vector2(0, -34), 12, 0, Mathf.Tau, 24, new Color(1, 1, 1, 0.25f), 2);
        }
        if (Tag != null)
        {
            var f = UiTheme.Bold;
            int fs = 11;
            var sz = f.GetStringSize(Tag, HorizontalAlignment.Left, -1, fs);
            var p = new Vector2(-sz.X / 2, BarY - 6);
            DrawStringOutline(f, p, Tag, HorizontalAlignment.Left, -1, fs, 4, UiTheme.Ink);
            DrawString(f, p, Tag, HorizontalAlignment.Left, -1, fs, TagColor);
        }
    }
}

/// <summary>Synchronizuje węzły graficzne ze stanem symulacji (gracze, wrogowie, znajdźki, piedestały).</summary>
public partial class EntityView : Node2D
{
    private RunSim _g;
    private readonly Dictionary<object, EntityNode> _nodes = new();
    private float _t;
    private Room _room;
    public Func<PlayerSim, bool> ShowPromptFor;

    public void Bind(RunSim g)
    {
        _g = g;
        YSortEnabled = true;
    }

    private EntityNode Make(object key, Texture2D tex, float scale, Vector2 offset, bool tint = false)
    {
        var n = new EntityNode();
        n.Body = new Sprite2D { Texture = tex, Scale = new Vector2(scale, scale), Offset = offset };
        if (tint)
        {
            n.Mat = Shaders.NewTint();
            n.Body.Material = n.Mat;
        }
        n.AddChild(n.Body);
        AddChild(n);
        _nodes[key] = n;
        return n;
    }

    private EntityNode Get(object key, Func<EntityNode> make) => _nodes.TryGetValue(key, out var n) ? n : make();

    public void Clear()
    {
        foreach (var n in _nodes.Values) n.QueueFree();
        _nodes.Clear();
    }

    public void Step(float dt)
    {
        _t += dt;
        if (_g.Room != _room)
        {
            _room = _g.Room;
            Clear();
        }
        foreach (var n in _nodes.Values) n.Seen = false;
        foreach (var p in _g.Players) SyncPlayer(p);
        var tint = _g.Floor.Biome.Tint;
        foreach (var e in _g.Enemies) SyncEnemy(e, tint);
        foreach (var pk in _g.Room.Pickups) SyncPickup(pk);
        foreach (var pd in _g.Room.Pedestals) SyncPedestal(pd);
        if (_g.Room.Kind == RoomKind.Shop) SyncShopkeeper();
        foreach (var k in _nodes.Where(kv => !kv.Value.Seen).Select(kv => kv.Key).ToList())
        {
            _nodes[k].QueueFree();
            _nodes.Remove(k);
        }
    }

    // ------------------------------------------------------------------ gracze
    private void SyncPlayer(PlayerSim p)
    {
        var tex = Art.Character(p.Key);
        float scale = 2 * p.R * 1.5f / 155f;
        var n = Get(p, () =>
        {
            var nn = Make(p, tex, scale, new Vector2(0, -19), tint: true);
            nn.Weapon = new Sprite2D { Texture = Art.Weapon(p.Def.Weapon) };
            var pv = p.Def.WeaponPivot;
            nn.Weapon.Offset = new Vector2(96 - pv[0], 48 - pv[1]);
            nn.AddChild(nn.Weapon);
            nn.ShadowW = p.R * 2.2f;
            nn.ShadowH = p.R * 0.8f;
            nn.ShadowY = p.R * 0.95f;
            return nn;
        });
        n.Seen = true;
        n.Position = new Vector2(p.X, p.Y);
        bool left = MathF.Abs(Mathf.Wrap(p.Aim, -Mathf.Pi, Mathf.Pi)) > Mathf.Pi / 2;
        float speedK = Mathf.Clamp(new Vector2(p.Vx, p.Vy).Length() / 240f, 0, 1);
        float bob = MathF.Abs(MathF.Sin(p.T * 12)) * 5 * speedK;
        float breathe = 1 + 0.035f * MathF.Sin(p.T * 3 + p.Idx);
        n.Body.Scale = new Vector2(scale * (left ? -1 : 1) * (2 - breathe), scale * breathe);
        n.Body.Position = new Vector2(0, -bob);
        n.Body.Rotation = p.DashT > 0 ? (left ? -0.35f : 0.35f) : p.Vx / 240f * 0.1f;
        float alpha = p.InvisT > 0 ? 0.35f : p.IFrames > 0 && (int)(p.IFrames * 14) % 2 == 0 ? 0.55f : 1f;
        n.Mat.SetShaderParameter("flash", p.Flash > 0 ? 0.8f : 0f);
        n.Mat.SetShaderParameter("alpha_mul", alpha);
        n.Mat.SetShaderParameter("sat", p.Downed ? 0.15f : 1f);
        n.Weapon.Visible = !p.Downed;
        n.Weapon.Position = new Vector2(MathF.Cos(p.Aim) * p.R * 0.7f, MathF.Sin(p.Aim) * p.R * 0.7f + 4 - bob);
        n.Weapon.Rotation = p.Aim;
        n.Weapon.Scale = new Vector2(0.3f, left ? -0.3f : 0.3f);
        n.Weapon.ZIndex = MathF.Sin(p.Aim) < -0.3f ? -1 : 1;
        n.Weapon.Modulate = new Color(1, 1, 1, alpha);
        if (p.Downed)
        {
            n.Body.Rotation = Mathf.Pi / 2 * (left ? -1 : 1);
            n.ReviveFrac = p.ReviveP;
        }
        else
            n.ReviveFrac = -1;
        if (_g.Coop)
        {
            n.Tag = PlayerSim.Tags[p.Idx].tag;
            n.TagColor = PlayerSim.Tags[p.Idx].color;
            n.BarY = -p.R * 2.4f;
        }
        n.RingColor = p.ShieldUp || p.EshieldUp ? new Color("78dcff") : null;
        n.RingR = p.R * 1.4f;
        n.Visible = !p.Dead;
        n.QueueRedraw();
    }

    // ------------------------------------------------------------------ wrogowie
    private void SyncEnemy(Enemy e, BiomeTint tint)
    {
        bool boss = e.IsBoss;
        var tex = boss ? Art.BossTex(e.Key) : Art.Enemy(e.Key);
        float texW = tex?.GetWidth() ?? 192;
        float scale = boss ? 2 * e.R * 1.4f / (0.72f * texW) : 2 * e.R * 1.35f / (0.75f * texW);
        var n = Get(e, () =>
        {
            var nn = Make(e, tex, scale, new Vector2(0, -texW * 0.05f), tint: true);
            // bossowie mają własne grafiki pod swój biom – bez przebarwienia
            if (!boss)
            {
                nn.Mat.SetShaderParameter("hue", tint.Hue);
                nn.Mat.SetShaderParameter("sat", tint.Sat);
                nn.Mat.SetShaderParameter("val", tint.Val);
            }
            if (e.Elite)
            {
                nn.Badge = new Sprite2D { Texture = Art.Tex("res://art/enemies/_elite_crown.svg"), Scale = new Vector2(0.35f, 0.35f) * (e.MiniBoss ? 1.5f : 1) };
                nn.AddChild(nn.Badge);
            }
            nn.ShadowW = e.R * 2.1f;
            nn.ShadowH = e.R * 0.75f;
            nn.ShadowY = e.Flying ? e.R + 14 : e.R * 0.85f;
            nn.ShadowA = e.Flying ? 0.18f : 0.28f;
            nn.BarW = boss ? 0 : MathF.Max(26, e.R * 2);
            nn.BarY = -e.R - 12;
            nn.BarColor = e.Elite ? new Color("ffc83c") : new Color("e64650");
            return nn;
        });
        n.Seen = true;
        n.Position = new Vector2(e.X, e.Y);
        float hover = e.Flying ? 6 * MathF.Sin(e.T * 4) - 8 : 0;
        float lift = e.Lift + (boss ? ((Boss)e).Z : 0);
        bool left = MathF.Cos(e.Face) < 0;
        float wob = 1 + 0.04f * MathF.Sin(e.T * 5);
        n.Body.Scale = new Vector2(scale * (left ? -1 : 1) * e.SquashX * (2 - wob), scale * e.SquashY * wob);
        n.Body.Position = new Vector2(0, -lift + hover);
        n.Body.Rotation = e.Tilt;
        // boss pod ciągłym ostrzałem nie może „wybielać się” – słabszy błysk
        n.Mat.SetShaderParameter("flash", e.Flash > 0 ? (boss ? 0.45f : 0.85f) : MathF.Min(0.5f, e.Charge * 0.45f));
        n.Mat.SetShaderParameter("alpha_mul", e.Alpha);
        if (n.Badge != null)
            n.Badge.Position = new Vector2(0, -lift + hover - e.R * 1.55f);
        n.HpFrac = e.Hp / e.MaxHp;
        n.RingColor = e.SlowT > 0 ? new Color("96dcff") : e.Elite ? new Color("ffd246") : null;
        n.RingR = e.R * 1.1f;
        n.QueueRedraw();
    }

    // ------------------------------------------------------------------ znajdźki i piedestały
    private void SyncPickup(Pickup pk)
    {
        var n = Get(pk, () =>
        {
            var (tex, size) = pk.Kind switch
            {
                PickupKind.Coin => (Art.Pickup("moneta"), 15f),
                PickupKind.Heart => (Art.Pickup("serduszko"), 20f),
                PickupKind.Chest => (Art.Pickup("skrzynia"), 38f),
                _ => (Art.Pickup("krysztal_xp"), pk.Value < 3 ? 11f : pk.Value < 8 ? 15f : 19f),
            };
            float sc = size / (tex?.GetWidth() ?? 64) * (tex != null && tex.GetWidth() > 100 ? 1.25f : 1.4f);
            var nn = Make(pk, tex, sc, Vector2.Zero, tint: pk.Kind == PickupKind.Xp);
            if (pk.Kind == PickupKind.Xp && pk.Value >= 3)
                nn.Mat.SetShaderParameter("hue", pk.Value >= 8 ? 0.45f : 0.22f);
            nn.ShadowW = size * 0.9f;
            nn.ShadowH = size * 0.35f;
            nn.ShadowY = size * 0.45f;
            return nn;
        });
        n.Seen = true;
        n.Position = new Vector2(pk.X, pk.Y);
        float bob = MathF.Sin(pk.T * 5) * 2;
        n.Body.Position = new Vector2(0, -pk.Z + bob - 4);
        if (pk.Kind == PickupKind.Coin)
            n.Body.Scale = new Vector2(MathF.Abs(n.Body.Scale.Y) * (0.25f + 0.75f * MathF.Abs(MathF.Cos(pk.T * 4))), n.Body.Scale.Y);
        n.QueueRedraw();
    }

    private void SyncPedestal(Pedestal pd)
    {
        var n = Get(pd, () =>
        {
            var baseTex = Art.Prop(pd.Kind == PedestalKind.Shrine ? "filar" : "filar");
            var nn = Make(pd, baseTex, 40f / (baseTex?.GetWidth() ?? 128) * 1.1f, new Vector2(0, -10), tint: true);
            if (pd.Kind == PedestalKind.Shrine)
                nn.Mat.SetShaderParameter("hue", 0.95f);
            Texture2D icon = pd.Kind == PedestalKind.HeartShop ? Art.Pickup("serduszko") : pd.Item != null ? Art.Item(pd.Item.Key) : null;
            nn.Weapon = new Sprite2D { Texture = icon, Scale = Vector2.One * (30f / (icon?.GetWidth() ?? 128)) };
            nn.AddChild(nn.Weapon);
            if (pd.Price > 0 || pd.HpCost > 0)
            {
                var lbl = new Label
                {
                    Text = pd.HpCost > 0 ? $"-{pd.HpCost}% HP" : $"{pd.Price}",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Position = new Vector2(-40, 18),
                    Size = new Vector2(80, 20),
                    Scale = new Vector2(0.5f, 0.5f),
                    PivotOffset = new Vector2(40, 0),
                };
                lbl.AddThemeFontSizeOverride("font_size", 26);
                lbl.AddThemeColorOverride("font_color", pd.HpCost > 0 ? new Color("ff5a64") : new Color("ffd23f"));
                lbl.AddThemeColorOverride("font_outline_color", UiTheme.Ink);
                lbl.AddThemeConstantOverride("outline_size", 8);
                nn.AddChild(lbl);
            }
            nn.ShadowW = 34;
            nn.ShadowH = 10;
            nn.ShadowY = 8;
            return nn;
        });
        n.Seen = true;
        n.Position = new Vector2(pd.X, pd.Y);
        float bob = MathF.Sin(_t * 2.5f + pd.Id) * 3;
        n.Weapon.Position = new Vector2(0, -34 + bob);
        n.QueueRedraw();
    }

    private void SyncShopkeeper()
    {
        const string key = "shopkeeper";
        var n = Get(key, () =>
        {
            bool own = ResourceLoader.Exists("res://art/characters/sklepikarz.svg");
            var tex = Art.Character(own ? "sklepikarz" : "zlodziej");
            var nn = Make(key, tex, own ? 0.4f : 0.36f, new Vector2(0, -21), tint: true);
            if (!own) nn.Mat.SetShaderParameter("hue", 0.12f);
            nn.ShadowW = 40;
            nn.ShadowH = 14;
            nn.ShadowY = 18;
            nn.Tag = "SKLEP";
            nn.TagColor = new Color("ffd23f");
            nn.BarY = -46;
            return nn;
        });
        n.Seen = true;
        n.Position = RunSim.ShopkeeperPos;
        n.Body.Scale = new Vector2(-0.36f, 0.36f * (1 + 0.03f * MathF.Sin(_t * 2)));
        n.QueueRedraw();
    }
}
