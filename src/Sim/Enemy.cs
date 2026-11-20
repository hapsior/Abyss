using System;
using System.Collections.Generic;
using Godot;

namespace Otchlan.Sim;

/// <summary>
/// Bazowy wróg. Port klasy Enemy z wersji 1.x z dodatkami 1.1: linia strzału (has_los / los_thick),
/// szukanie pozycji do strzału (seek_firing_spot), wyprzedzanie celu, żetony ataku, czas ochronny po pojawieniu się.
///
/// Nowe typy: klasa dziedzicząca, nadpisz <see cref="Setup"/> i <see cref="Ai"/>; dopisz do <see cref="EnemyFactory"/>.
/// Statystyki (hp, promień, prędkość...) są w data/enemies.json.
/// </summary>
public class Enemy
{
    private static int _nextId = 1;

    public readonly int Id = _nextId++;
    public string Key;
    public string Name;
    public Color Color;
    public bool Elite, MiniBoss, Minion;
    public float MaxHp, Hp, R, DmgScale, ContactDmg, BulletDmg, Spd, Xp, Coin, KnockResist = 1;
    public bool Flying, Ghost;
    public virtual bool IsBoss => false;

    public float X, Y, Vx, Vy, Kvx, Kvy;
    public float Flash, FlashCd, PoisonT, PoisonDps, BurnT, BurnDps, DotTick, SlowT, OrbCd, ThornCd;
    public float T, Timer, Face, NavT;
    public Vector2 Nav;
    public bool Alive = true, Invuln;
    public string State = "idle";
    public float Fr = 1, Bspd = 1, Lead;
    public int Extra;
    public float Alpha = 1;
    public float Grace;
    public Enemy Summoner;

    // --- wygląd (ustawiany przez SI, czytany przez widok) ---
    public float Lift, SquashX = 1, SquashY = 1, Tilt, Charge;
    /// <summary>Kąt broni/celowania do narysowania (np. laser snajpera); NaN = brak.</summary>
    public float AimVisual = float.NaN;
    public Vector2? LaserEnd;

    // --- SI 1.1 ---
    public PlayerSim Tgt;
    public float TgtT, LosT;
    private bool _los;
    public List<Vector2> Path;
    public float PathT;
    public string HeldToken;

    public RunSim G;

    public virtual void Init(RunSim g, EnemyDef def, float x, float y, bool elite, bool minion, bool miniBoss = false)
    {
        G = g;
        Key = def.Key;
        Name = def.Name;
        Color = new Color(def.Color);
        Elite = elite || miniBoss;
        MiniBoss = miniBoss;
        Minion = minion;
        Flying = def.Flying;
        Ghost = def.Ghost;
        KnockResist = def.KnockResist;
        Xp = def.Xp;
        Coin = def.Coin;
        var diff = g.Diff;
        int depth = g.Depth;
        float hpScale = Balance.EnemyHpScale(depth) * diff.EnemyHp * g.SealMod("enemy_hp") * g.CoopHpMult();
        MaxHp = Hp = def.Hp * hpScale * (Elite ? Balance.EliteHp : 1) * (miniBoss ? 3.0f : 1);
        R = def.Radius * (miniBoss ? 1.6f : Elite ? 1.25f : 1);
        DmgScale = Balance.EnemyDmgScale(depth) * diff.EnemyDmg * g.SealMod("enemy_dmg") * (Elite ? 1.15f : 1);
        ContactDmg = def.Contact * DmgScale;
        BulletDmg = 8 * DmgScale;
        Spd = def.Speed * Balance.EnemySpeedScale(depth) * g.SealMod("enemy_speed") * (Elite ? 1.1f : 1);
        X = x;
        Y = y;
        T = g.CosRange(0, 10);
        Timer = g.R.Range(0.5f, 1.5f);
        Fr = diff.FireRate * Balance.EnemyFireScale(depth);
        Extra = diff.ExtraBullets + (Elite ? 1 : 0);
        Bspd = diff.BulletSpeed;
        Lead = diff.Lead;
        Grace = Balance.SpawnGrace;
        Setup(g);
    }

    public virtual void Setup(RunSim g) { }

    // ------------------------------------------------------------------ cel
    /// <summary>Gracz, na którego poluje ten wróg (w co-op: bliższy, z histerezą żeby nie skakał co klatkę).</summary>
    public PlayerSim Target(RunSim g)
    {
        TgtT -= G?.FrameDt ?? 0;
        if (Tgt == null || !Tgt.Active || TgtT <= 0)
        {
            Tgt = g.TargetPlayerFor(this, Tgt);
            TgtT = 1.2f;
        }
        return Tgt;
    }

    public Vector2 TargetPos(RunSim g) => g.TargetPos(this);
    public float AngleTo(RunSim g) { var t = TargetPos(g); return MathF.Atan2(t.Y - Y, t.X - X); }
    public float DistTo(RunSim g) { var t = TargetPos(g); return MathF.Sqrt((t.X - X) * (t.X - X) + (t.Y - Y) * (t.Y - Y)); }

    // ------------------------------------------------------------------ strzały
    /// <summary>Wystrzeliwuje pocisk wroga. W czasie ochronnym po pojawieniu się zwraca null (nie strzela).</summary>
    public Bullet Fire(RunSim g, float angle, float speed, float r = 7, Color? color = null, float dmgMult = 1, float accel = 0,
        float turn = 0, float life = 8, float? x = null, float? y = null, float maxSpeed = 0, string style = "orb")
    {
        if (Grace > 0 && !IsBoss)
            return null;
        float sp = speed * Bspd;
        var b = new Bullet(x ?? X, y ?? Y, MathF.Cos(angle) * sp, MathF.Sin(angle) * sp, BulletDmg * dmgMult, r,
            color ?? C.EnemyBullet, false, life: life, accel: accel * Bspd, turn: turn, style: style, source: Key);
        if (maxSpeed > 0) b.MaxSpeed = maxSpeed * Bspd;
        g.Bullets.Add(b);
        return b;
    }

    public void FireSpread(RunSim g, float angle, int n, float step, float speed, float r = 7, Color? color = null, float dmgMult = 1, string style = "orb")
    {
        for (int i = 0; i < n; i++)
            Fire(g, angle + (i - (n - 1) / 2f) * step, speed, r, color, dmgMult, style: style);
    }

    public void FireRing(RunSim g, int n, float speed, float offset = 0, float r = 7, Color? color = null, float dmgMult = 1,
        float accel = 0, float turn = 0, string style = "orb")
    {
        for (int i = 0; i < n; i++)
            Fire(g, offset + i * C.Tau / n, speed, r, color, dmgMult, accel, turn, style: style);
    }

    /// <summary>Kąt strzału z wyprzedzeniem ruchu celu (siła wyprzedzenia zależy od trudności).</summary>
    public float AimAt(RunSim g, float bulletSpeed)
    {
        var t = TargetPos(g);
        var p = Target(g);
        if (p == null || Lead <= 0)
            return MathF.Atan2(t.Y - Y, t.X - X);
        float d = MathF.Sqrt((t.X - X) * (t.X - X) + (t.Y - Y) * (t.Y - Y));
        float tt = d / MathF.Max(60, bulletSpeed * Bspd);
        float px = t.X + p.Vx * tt * Lead, py = t.Y + p.Vy * tt * Lead;
        return MathF.Atan2(py - Y, px - X);
    }

    // ------------------------------------------------------------------ ruch
    public void Chase(RunSim g, float speed, float dt)
    {
        NavT -= dt;
        if (NavT <= 0)
        {
            NavT = 0.15f;
            Nav = g.ChaseDir(this);
        }
        Vx = Nav.X * speed;
        Vy = Nav.Y * speed;
    }

    public void Flee(RunSim g, float speed)
    {
        var t = TargetPos(g);
        float dx = X - t.X, dy = Y - t.Y, d = MathF.Max(1, MathF.Sqrt(dx * dx + dy * dy));
        Vx = dx / d * speed;
        Vy = dy / d * speed;
    }

    public void MoveTo(Vector2 dest, float speed)
    {
        float dx = dest.X - X, dy = dest.Y - Y, d = MathF.Sqrt(dx * dx + dy * dy);
        if (d > 4)
        {
            Vx = dx / d * speed;
            Vy = dy / d * speed;
        }
    }

    // ------------------------------------------------------------------ SI 1.1: linia strzału
    /// <summary>Czy wróg widzi cel „grubą” linią (pocisk przeleci obok przeszkód)? Odświeżane co 0.2 s.</summary>
    public bool HasLos(RunSim g, bool refresh = false)
    {
        if (Ghost)
            return true;
        LosT -= g.FrameDt;
        if (refresh || LosT <= 0)
        {
            LosT = 0.2f;
            var t = TargetPos(g);
            _los = g.Room.LosThick(X, Y, t.X, t.Y);
        }
        return _los;
    }

    /// <summary>
    /// Idzie do najbliższego pola, z którego widać cel w zasięgu [minD, maxD] (BFS po siatce).
    /// Dzięki temu strzelec wychyla się zza kamienia, zamiast strzelać w przeszkodę.
    /// Zwraca true, gdy już stoi w dobrym miejscu.
    /// </summary>
    public bool SeekFiringSpot(RunSim g, float dt, float minD, float maxD, float speed)
    {
        PathT -= dt;
        if (Path == null || PathT <= 0)
        {
            PathT = 0.5f;
            Path = g.FiringPath(this, minD, maxD);
        }
        if (Path == null || Path.Count == 0)
        {
            Chase(g, speed, dt);
            return false;
        }
        var next = Path[0];
        float dx = next.X - X, dy = next.Y - Y, d = MathF.Sqrt(dx * dx + dy * dy);
        if (d < 10)
        {
            Path.RemoveAt(0);
            return Path.Count == 0;
        }
        Vx = dx / d * speed;
        Vy = dy / d * speed;
        return false;
    }

    // ------------------------------------------------------------------ żetony ataku
    /// <summary>Prosi o żeton ataku ("shot" / "charge"). Ogranicza liczbę wrogów atakujących naraz (czytelność walki).</summary>
    public bool TakeToken(RunSim g, string kind) => g.TakeToken(this, kind);
    public void ReleaseToken(RunSim g) => g.ReleaseToken(this);

    // ------------------------------------------------------------------ pętla
    public virtual void Update(float dt, RunSim g)
    {
        T += dt;
        Grace = MathF.Max(0, Grace - dt);
        Flash = MathF.Max(0, Flash - dt);
        FlashCd = MathF.Max(0, FlashCd - dt);
        OrbCd = MathF.Max(0, OrbCd - dt);
        ThornCd = MathF.Max(0, ThornCd - dt);
        if (PoisonT > 0 || BurnT > 0)
        {
            DotTick -= dt;
            if (DotTick <= 0)
            {
                DotTick = 0.5f;
                float d = 0;
                if (PoisonT > 0) d += PoisonDps * 0.5f;
                if (BurnT > 0) d += BurnDps * 0.5f;
                g.DamageEnemy(this, d, color: PoisonT > 0 ? new Color("96f05a") : new Color("ff9632"), small: true, dot: true, source: "dot");
                if (!Alive) return;
            }
            PoisonT -= dt;
            BurnT -= dt;
            if (BurnT > 0 && g.Cos.NextDouble() < dt * 14)
                g.Fx.Burst(X + g.CosRange(-R, R) * 0.6f, Y - R * 0.3f, new Color("ff8c28"), 1, 40, 4, 0.4f, grav: -140, glow: true);
            if (PoisonT > 0 && g.Cos.NextDouble() < dt * 6)
                g.Fx.Burst(X + g.CosRange(-R, R) * 0.6f, Y - R * 0.5f, new Color("8ce650"), 1, 20, 3, 0.5f, grav: -60);
        }
        float slow = 1;
        if (SlowT > 0)
        {
            SlowT -= dt;
            slow = 0.55f;
        }
        Vx = Vy = 0;
        Ai(dt, g);
        if (!Alive) return;
        X += (Vx * slow + Kvx) * dt;
        Y += (Vy * slow + Kvy) * dt;
        float k = MathF.Max(0, 1 - 9 * dt);
        Kvx *= k;
        Kvy *= k;
        if (Flying || Ghost)
        {
            X = C.Clamp(X, C.RX0 + R, C.RX1 - R);
            Y = C.Clamp(Y, C.RY0 + R, C.RY1 - R);
        }
        else
        {
            var p = g.Room.ResolveCircle(X, Y, R);
            X = p.X;
            Y = p.Y;
        }
        var t = TargetPos(g);
        Face = MathF.Atan2(t.Y - Y, t.X - X);
    }

    /// <summary>Domyślna SI: pościg za graczem.</summary>
    public virtual void Ai(float dt, RunSim g) => Chase(g, Spd, dt);

    public void Knock(float kx, float ky)
    {
        Kvx += kx * KnockResist;
        Kvy += ky * KnockResist;
    }

    public virtual void OnDeath(RunSim g) { }

    /// <summary>Stan do zapisu gry nie jest potrzebny: po wczytaniu pokój walki zaczyna się od nowa.</summary>
    public override string ToString() => $"{Key}#{Id} hp={Hp:0}/{MaxHp:0} ({X:0},{Y:0})";
}

/// <summary>Oznacza klasę wroga lub bossa kluczem z data/enemies.json / data/bosses.json.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class EnemyTypeAttribute : Attribute
{
    public string Key { get; }
    public EnemyTypeAttribute(string key) => Key = key;
}

/// <summary>Tworzy wrogów i bossów po kluczu. Klasy są wyszukiwane automatycznie po atrybucie [EnemyType("klucz")].</summary>
public static class EnemyFactory
{
    private static Dictionary<string, Type> _types;

    private static Dictionary<string, Type> Types
    {
        get
        {
            if (_types != null) return _types;
            _types = new Dictionary<string, Type>();
            foreach (var t in typeof(Enemy).Assembly.GetTypes())
                if (typeof(Enemy).IsAssignableFrom(t) && !t.IsAbstract &&
                    t.GetCustomAttributes(typeof(EnemyTypeAttribute), false) is { Length: > 0 } a)
                    _types[((EnemyTypeAttribute)a[0]).Key] = t;
            return _types;
        }
    }

    public static bool Has(string key) => Types.ContainsKey(key);

    public static Enemy Create(string key) => Types.TryGetValue(key, out var t) ? (Enemy)Activator.CreateInstance(t)! : new Enemy();
}
