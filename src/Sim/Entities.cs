using System;
using System.Collections.Generic;
using Godot;

namespace Otchlan.Sim;

// ====================================================================== POCISK
public sealed class Bullet
{
    public float X, Y, Vx, Vy, Dmg, R;
    public Color Color;
    public bool Friendly;
    public float Range, Dist;
    public string Style;           // gracz: orb/arrow/disc/magic/dagger/drop/dark; wróg: orb/needle/fire/skull
    public int Pierce, Bounce;
    public float Homing;
    /// <summary>Naprowadzanie działa dopiero po przeleceniu tylu jednostek (celowanie ręczne: do punktu celowania)
    /// i tylko na wrogów w stożku ±HomeCone wokół kierunku lotu.</summary>
    public float HomeAfter, HomeCone = MathF.PI;
    public bool Spectral, Crit, Alive = true, Fx, IsSplit;
    public HashSet<int> Hit;
    public float Life = 9, Accel, Turn, MinSpeed = 40, MaxSpeed = 900, Knock = 1, TrailT;
    /// <summary>Czas życia pocisku w sekundach (bot „ludzki” reaguje dopiero po czasie reakcji).</summary>
    public float Age;
    public string Source;
    public PlayerSim Owner;
    /// <summary>Pocisk wroga spowalnia trafionego gracza (szron, zarodniki).</summary>
    public float Chill;

    public Bullet(float x, float y, float vx, float vy, float dmg, float r, Color color, bool friendly, float range = 600,
        string style = "orb", int pierce = 0, int bounce = 0, float homing = 0, bool spectral = false, bool crit = false,
        bool fx = false, float life = 9, float accel = 0, float turn = 0, float knock = 1, string source = null)
    {
        X = x; Y = y; Vx = vx; Vy = vy; Dmg = dmg; R = r; Color = color; Friendly = friendly; Range = range;
        Style = style; Pierce = pierce; Bounce = bounce; Homing = homing; Spectral = spectral; Crit = crit;
        Fx = fx; Life = life; Accel = accel; Turn = turn; Knock = knock; Source = source;
        Hit = friendly ? new HashSet<int>() : null;
    }

    public float Speed => MathF.Sqrt(Vx * Vx + Vy * Vy);
    public float Angle => MathF.Atan2(Vy, Vx);

    public void Kill(RunSim g, bool pop = true)
    {
        if (!Alive) return;
        Alive = false;
        if (pop) g.Fx.Burst(X, Y, Color, 4, 90, 3, 0.25f);
    }

    public void Update(float dt, RunSim g)
    {
        Age += dt;
        if (Friendly && Homing > 0 && Dist >= HomeAfter)
        {
            var tgt = g.NearestEnemyAhead(X, Y, Angle, HomeCone, 380, Hit);
            if (tgt != null)
            {
                float desired = MathF.Atan2(tgt.Y - Y, tgt.X - X), cur = Angle;
                float diff = Mathf.Wrap(desired - cur, -Mathf.Pi, Mathf.Pi);
                float step = Homing * dt;
                cur += C.Clamp(diff, -step, step);
                float sp = Speed;
                Vx = MathF.Cos(cur) * sp; Vy = MathF.Sin(cur) * sp;
            }
        }
        if (Turn != 0)
        {
            float a = Angle + Turn * dt, sp = Speed;
            Vx = MathF.Cos(a) * sp; Vy = MathF.Sin(a) * sp;
        }
        if (Accel != 0)
        {
            float sp = Speed;
            if (sp > 0)
            {
                float k = C.Clamp(sp + Accel * dt, MinSpeed, MaxSpeed) / sp;
                Vx *= k; Vy *= k;
            }
        }
        float ox = X, oy = Y;
        X += Vx * dt;
        Y += Vy * dt;
        if (Friendly)
        {
            Dist += Speed * dt;
            if (Dist > Range) { Kill(g); return; }
        }
        else
        {
            Life -= dt;
            if (Life <= 0) { Kill(g, false); return; }
        }
        bool hitWall = false;
        if (X < C.RX0 + R) { X = C.RX0 + R; Vx = MathF.Abs(Vx); hitWall = true; }
        else if (X > C.RX1 - R) { X = C.RX1 - R; Vx = -MathF.Abs(Vx); hitWall = true; }
        if (Y < C.RY0 + R) { Y = C.RY0 + R; Vy = MathF.Abs(Vy); hitWall = true; }
        else if (Y > C.RY1 - R) { Y = C.RY1 - R; Vy = -MathF.Abs(Vy); hitWall = true; }
        if (hitWall)
        {
            if (Bounce > 0)
            {
                Bounce--;
                if (Friendly) Hit = new HashSet<int>();
            }
            else
            {
                g.Fx.Spark(X, Y, Color, 3, 150, MathF.Atan2(-Vy, -Vx));
                Kill(g, false);
                return;
            }
        }
        if (!Spectral)
        {
            var ob = g.Room.TileAt(X, Y);
            if (ob is { Solid: true })
            {
                if (Friendly) g.DamageObstacle(ob, Dmg);
                if (Bounce > 0)
                {
                    Bounce--;
                    var oc = g.Room.TileAt(ox, Y);
                    var orow = g.Room.TileAt(X, oy);
                    if (oc is not { Solid: true }) Vx = -Vx;
                    else if (orow is not { Solid: true }) Vy = -Vy;
                    else { Vx = -Vx; Vy = -Vy; }
                    X = ox; Y = oy;
                }
                else
                {
                    g.Fx.Burst(X, Y, Color, 4, 100, 3, 0.25f);
                    Kill(g, false);
                }
            }
        }
    }
}

// ====================================================================== ZNAJDŹKI
public enum PickupKind { Coin, Xp, Heart, Chest }

public sealed class Pickup
{
    public PickupKind Kind;
    public float X, Y, Vx, Vy, Value, T, Z, Vz;
    public bool Alive = true, Magnet;
    public int Id;

    public Pickup(PickupKind kind, float x, float y, Random cos, float value = 1, bool pop = true)
    {
        Kind = kind; X = x; Y = y; Value = value;
        float a = (float)cos.NextDouble() * C.Tau;
        float s = pop ? 60 + (float)cos.NextDouble() * 140 : 0;
        Vx = MathF.Cos(a) * s; Vy = MathF.Sin(a) * s;
        T = (float)cos.NextDouble() * 10;
        Vz = pop ? 120 + (float)cos.NextDouble() * 100 : 0;
    }

    public void Update(float dt, RunSim g)
    {
        T += dt;
        var p = g.NearestPlayer(X, Y);
        if (p == null) return;
        float dx = p.X - X, dy = p.Y - Y, d = MathF.Max(1, MathF.Sqrt(dx * dx + dy * dy));
        bool attract = Kind is PickupKind.Xp or PickupKind.Coin && (g.Room.Cleared || d < p.Pickup);
        if (Kind == PickupKind.Heart && d < p.Pickup * 0.6f && p.Hp < p.MaxHp)
            attract = true;
        if (attract && Z <= 0)
            Magnet = true;
        if (Magnet)
        {
            float sp = 420 + MathF.Max(0, 400 - d) * 1.5f, k = MathF.Min(1, dt * 12);
            Vx += (dx / d * sp - Vx) * k;
            Vy += (dy / d * sp - Vy) * k;
        }
        else
        {
            float k = MathF.Max(0, 1 - 5 * dt);
            Vx *= k; Vy *= k;
        }
        float ox = X, oy = Y;
        X += Vx * dt; Y += Vy * dt;
        // swobodnie toczące się znajdźki (serca, skrzynki) nie wjeżdżają w przeszkody
        if (!Magnet && g.Room.TileAt(X, Y) is { Solid: true })
        {
            X = ox; Y = oy;
            Vx = Vy = 0;
        }
        if (Vz != 0 || Z > 0)
        {
            Vz -= 700 * dt;
            Z += Vz * dt;
            if (Z <= 0)
            {
                Z = 0;
                Vz = MathF.Abs(Vz) > 80 ? -Vz * 0.35f : 0;
            }
        }
        X = C.Clamp(X, C.RX0 + 8, C.RX1 - 8);
        Y = C.Clamp(Y, C.RY0 + 8, C.RY1 - 8);
        if (d < p.R + 12 && Z < 8 && g.CollectPickup(this, p))
            Alive = false;
    }
}

// ====================================================================== PIEDESTAŁ
public enum PedestalKind { Item, Shop, HeartShop, Shrine }

/// <summary>Przedmiot na piedestale. Gracz musi podejść i nacisnąć przycisk interakcji (nic nie podnosi się samo).</summary>
public sealed class Pedestal
{
    public float X, Y;
    public ItemDef Item;
    public int Price, Group, HpCost;
    public float Heal;
    public PedestalKind Kind;
    public bool Alive = true;
    public float T, Cool;
    public int Id;

    public Pedestal(float x, float y, ItemDef item, int price, PedestalKind kind, int group = 0, float heal = 0, int hpCost = 0)
    {
        X = x; Y = y; Item = item; Price = price; Kind = kind; Group = group; Heal = heal; HpCost = hpCost;
    }

    public const float InteractRange = 52;
}

// ====================================================================== EFEKTY
public abstract class Effect
{
    public bool Alive = true;
    public bool TopLayer;
    public float X, Y;
    public abstract void Update(float dt, RunSim g);
}

public sealed class PoisonCloud : Effect
{
    public float R, Dps, T, Dur, Tick;
    public Color Color;
    public bool Slow;

    public PoisonCloud(float x, float y, float r, float dps, float duration, Color? color = null, bool slow = true)
    {
        X = x; Y = y; R = r; Dps = dps; T = Dur = duration; Color = color ?? new Color("78e650"); Slow = slow;
    }

    public override void Update(float dt, RunSim g)
    {
        T -= dt;
        if (T <= 0) { Alive = false; return; }
        Tick -= dt;
        if (Tick <= 0)
        {
            Tick = 0.35f;
            foreach (var e in g.Enemies.ToArray())
                if ((e.X - X) * (e.X - X) + (e.Y - Y) * (e.Y - Y) < (R + e.R) * (R + e.R))
                {
                    g.DamageEnemy(e, Dps * 0.35f, color: Color, small: true, source: "poison");
                    if (Slow) e.SlowT = MathF.Max(e.SlowT, 0.6f);
                }
        }
        if (g.Cos.NextDouble() < dt * 14)
        {
            float a = (float)g.Cos.NextDouble() * C.Tau, d = (float)g.Cos.NextDouble() * R;
            g.Fx.Burst(X + MathF.Cos(a) * d, Y + MathF.Sin(a) * d, Color, 1, 20, 4, 0.6f, grav: -40);
        }
    }
}

/// <summary>Wroga strefa ognia – rani graczy.</summary>
public sealed class FirePool : Effect
{
    public float R, Dmg, T, Dur, Warm = 0.5f;

    public FirePool(float x, float y, float r, float dmg, float duration)
    {
        X = x; Y = y; R = r; Dmg = dmg; T = Dur = duration;
    }

    public override void Update(float dt, RunSim g)
    {
        T -= dt; Warm -= dt;
        if (T <= 0) { Alive = false; return; }
        if (Warm <= 0)
            foreach (var p in g.ActivePlayers())
                if ((p.X - X) * (p.X - X) + (p.Y - Y) * (p.Y - Y) < (R + p.R * 0.5f) * (R + p.R * 0.5f))
                    p.Hurt(Dmg, g, X, Y, "fire");
        if (g.Cos.NextDouble() < dt * 10)
        {
            float a = (float)g.Cos.NextDouble() * C.Tau, d = (float)g.Cos.NextDouble() * R;
            g.Fx.Burst(X + MathF.Cos(a) * d, Y + MathF.Sin(a) * d, new Color("ff9628"), 1, 30, 4, 0.5f, grav: -80, glow: true);
        }
    }
}

public sealed class BlackHole : Effect
{
    public float Dmg, T, Dur, Radius, Tick;

    public BlackHole(float x, float y, float dmg, float duration = 2.6f, float radius = 230)
    {
        X = x; Y = y; Dmg = dmg; T = Dur = duration; Radius = radius;
    }

    public override void Update(float dt, RunSim g)
    {
        T -= dt;
        if (T <= 0)
        {
            Alive = false;
            g.Fx.Ring(X, Y, new Color("be64ff"), 90, 0.3f);
            return;
        }
        Tick -= dt;
        foreach (var e in g.Enemies)
        {
            if (e.IsBoss) continue;
            float dx = X - e.X, dy = Y - e.Y, d = MathF.Max(1, MathF.Sqrt(dx * dx + dy * dy));
            if (d < Radius)
            {
                float pull = 260 * (1 - d / Radius) + 60;
                e.Kvx += dx / d * pull * dt * 4;
                e.Kvy += dy / d * pull * dt * 4;
            }
        }
        if (Tick <= 0)
        {
            Tick = 0.25f;
            foreach (var e in g.Enemies.ToArray())
                if ((e.X - X) * (e.X - X) + (e.Y - Y) * (e.Y - Y) < 70 * 70)
                    g.DamageEnemy(e, Dmg, color: new Color("c882ff"), small: true, source: "blackhole");
        }
    }
}

/// <summary>Rzucona kolba alchemika – leci łukiem i tworzy trującą kałużę.</summary>
public sealed class Flask : Effect
{
    public float X0, Y0, X1, Y1, T, TT = 0.45f, Dps, Radius, Duration;

    public Flask(float x0, float y0, float x1, float y1, float dps, float radius, float duration)
    {
        X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; Dps = dps; Radius = radius; Duration = duration; TopLayer = true;
        X = x0; Y = y0;
    }

    public float Progress => T / TT;

    public override void Update(float dt, RunSim g)
    {
        T += dt;
        X = X0 + (X1 - X0) * Progress;
        Y = Y0 + (Y1 - Y0) * Progress;
        if (T >= TT)
        {
            Alive = false;
            g.Fx.Sound("poison");
            g.Fx.Sound("break");
            g.Fx.Burst(X1, Y1, new Color("96ff64"), 18, 220, 5, 0.5f);
            g.Fx.Ring(X1, Y1, new Color("96ff64"), Radius, 0.3f);
            g.Effects.Add(new PoisonCloud(X1, Y1, Radius, Dps, Duration));
        }
    }
}

/// <summary>Ostrzeżenie (czerwony okrąg) przed atakiem obszarowym; po czasie wywołuje akcję.</summary>
public sealed class Telegraph : Effect
{
    public float R, T, Dur;
    public Action<RunSim> Callback;
    public Color Color;

    public Telegraph(float x, float y, float r, float duration, Action<RunSim> callback, Color? color = null)
    {
        X = x; Y = y; R = r; T = Dur = duration; Callback = callback; Color = color ?? new Color("ff3c3c");
    }

    public override void Update(float dt, RunSim g)
    {
        T -= dt;
        if (T <= 0)
        {
            Alive = false;
            Callback(g);
        }
    }
}

/// <summary>
/// Wroga strefa na ziemi (zarodniki, szron, kolce): po rozgrzaniu (Warm) rani graczy w środku co Tick sekund
/// i opcjonalnie ich spowalnia. Rysowana kolorem <see cref="Color"/>.
/// </summary>
public sealed class HazardZone : Effect
{
    public float R, Dmg, T, Dur, Warm, TickEvery = 0.5f, Slow;
    private float _tick;
    public Color Color;
    public string Kind;

    public HazardZone(float x, float y, float r, float dmg, float duration, Color color, float warm = 0.4f, float slow = 0, string kind = "spores")
    {
        X = x; Y = y; R = r; Dmg = dmg; T = Dur = duration; Color = color; Warm = warm; Slow = slow; Kind = kind;
    }

    public override void Update(float dt, RunSim g)
    {
        T -= dt;
        Warm -= dt;
        if (T <= 0) { Alive = false; return; }
        if (Warm > 0) return;
        _tick -= dt;
        foreach (var p in g.ActivePlayers())
        {
            bool inside = (p.X - X) * (p.X - X) + (p.Y - Y) * (p.Y - Y) < (R + p.R * 0.5f) * (R + p.R * 0.5f);
            if (!inside) continue;
            if (Slow > 0) p.SlowT = MathF.Max(p.SlowT, Slow);
            if (_tick <= 0 && Dmg > 0) p.Hurt(Dmg, g, X, Y, Kind);
        }
        if (_tick <= 0) _tick = TickEvery;
        if (g.Cos.NextDouble() < dt * 8)
        {
            float a = (float)g.Cos.NextDouble() * C.Tau, d = (float)g.Cos.NextDouble() * R;
            g.Fx.Burst(X + MathF.Cos(a) * d, Y + MathF.Sin(a) * d, Color, 1, 25, 4, 0.6f, grav: -50);
        }
    }
}

// ====================================================================== PRZYWOŁAŃCE GRACZY
/// <summary>Totem Burzy Szamana: stoi w miejscu i co <see cref="Every"/> s razi piorunem łańcuchowym najbliższych wrogów.</summary>
public sealed class Totem : Effect
{
    public readonly PlayerSim Owner;
    public float T, Dur, Dmg, Cd = 0.3f, Every = 0.6f, Pulse;

    public Totem(PlayerSim owner, float x, float y, float dmg, float duration)
    {
        Owner = owner; X = x; Y = y; Dmg = dmg; Dur = duration;
    }

    public override void Update(float dt, RunSim g)
    {
        T += dt;
        Pulse = MathF.Max(0, Pulse - dt * 4);
        if (T >= Dur)
        {
            Alive = false;
            g.Fx.Burst(X, Y - 20, new Color("8affc8"), 14, 180, 5, 0.5f);
            return;
        }
        Cd -= dt;
        if (Cd > 0) return;
        if (g.NearestEnemy(X, Y, 320) == null) { Cd = 0.1f; return; }
        Cd = Every;
        Pulse = 1;
        g.ChainLightning(X, Y - 34, Dmg, 3, 320, 220, owner: Owner, source: "totem");
    }
}

/// <summary>Wieżyczka Inżyniera: celuje w najbliższego widocznego wroga i strzela pociskami właściciela (z jego ulepszeniami).</summary>
public sealed class Sentry : Effect
{
    public readonly PlayerSim Owner;
    public float T, Dur, DmgMult, Cd = 0.4f, Aim, Recoil;

    public Sentry(PlayerSim owner, float x, float y, float duration, float dmgMult)
    {
        Owner = owner; X = x; Y = y; Dur = duration; DmgMult = dmgMult; Aim = owner.Aim;
    }

    public override void Update(float dt, RunSim g)
    {
        T += dt;
        Recoil = MathF.Max(0, Recoil - dt * 6);
        if (T >= Dur)
        {
            Alive = false;
            g.Fx.Burst(X, Y, new Color("ffb050"), 12, 180, 5, 0.5f);
            g.Fx.Sound("break", 0.4f);
            return;
        }
        Cd -= dt;
        Enemy best = null;
        float bd = 520 * 520;
        foreach (var e in g.Enemies)
        {
            if (!e.Alive || e.Invuln) continue;
            float d = (e.X - X) * (e.X - X) + (e.Y - Y) * (e.Y - Y);
            if (d < bd && g.Room.Los(X, Y, e.X, e.Y)) { bd = d; best = e; }
        }
        if (best == null) return;
        float want = MathF.Atan2(best.Y - Y, best.X - X);
        Aim += Mathf.Wrap(want - Aim, -MathF.PI, MathF.PI) * MathF.Min(1, dt * 14);
        if (Cd > 0) return;
        Cd = 0.25f / MathF.Max(0.3f, Owner.FireMult);
        Recoil = 1;
        var b = Owner.MakeBullet(g, Aim, DmgMult, ox: X, oy: Y - 10);
        b.Source = "sentry";
        g.Bullets.Add(b);
        g.Fx.Spark(X + MathF.Cos(Aim) * 26, Y - 10 + MathF.Sin(Aim) * 26, new Color("ffb050"), 2, 160, Aim, 0.5f, 0.1f);
        g.Fx.Sound("shoot", 0.25f, 0.05f);
    }
}

// ====================================================================== TOWARZYSZ
public sealed class Familiar
{
    public int Idx;
    public float X, Y, Cd, T;

    public Familiar(int idx, Random cos)
    {
        Idx = idx;
        Cd = 0.2f + (float)cos.NextDouble() * 0.6f;
        T = (float)cos.NextDouble() * 10;
    }

    public void Update(float dt, RunSim g, PlayerSim owner, float tx, float ty)
    {
        T += dt;
        float k = MathF.Min(1, dt * 6);
        X += (tx - X) * k;
        Y += (ty - Y) * k;
        Cd -= dt;
        if (Cd > 0) return;
        var e = g.NearestEnemy(X, Y, 520);
        if (e != null)
        {
            Cd = 0.75f;
            float a = MathF.Atan2(e.Y - Y, e.X - X);
            float dmg = MathF.Max(4, owner.Damage * owner.DmgMult * 0.5f);
            g.Bullets.Add(new Bullet(X, Y, MathF.Cos(a) * 480, MathF.Sin(a) * 480, dmg, 5, new Color("aadcff"), true, 520, "orb", source: "fam") { Owner = owner });
        }
        else
            Cd = 0.2f;
    }
}
