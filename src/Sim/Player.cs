using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;

namespace Otchlan.Sim;

/// <summary>Wejście jednego gracza na jedną klatkę (z klawiatury/myszy, pada albo bota).</summary>
public sealed class SimInput
{
    public Vector2 Move;
    public float Aim;
    public bool Fire, Dash, Ability, Interact;
    public Vector2 Target = C.RoomCenter;
}

/// <summary>Postać gracza: statystyki, ruch, strzał, zryw, umiejętności. Port player.py z wersji 1.x + co-op.</summary>
public sealed class PlayerSim
{
    // ------------------------------------------------------------------ statystyki (nazwy jak w data/*.json, snake_case → PascalCase)
    public float MaxHp = 100, Damage = 10, DmgMult = 1, FireRate = 3, FireMult = 1, ShotSpeed = 520, Range = 450, Speed = 235;
    public float Crit = 0.05f, CritMult = 2, Armor, Regen, Lifesteal, Luck, Pickup = 110, Dodge;
    public float Projectiles = 1, Pierce, Bounce, Homing, Poison, Burn, Slow, Chain, Explode, Split, Spectral, Backshot;
    public float ShotSize = 1, Knockback = 1, DashCharges = 1, DashCd = 1.1f, AbilityCdMult = 1, AbilityPower = 1;
    public float Thorns, CoinMult = 1, Orbitals, Familiars, Revives, Eshield, Blackhole, PriceMult = 1, RoomShield;
    public float Rage, PoisonBurst, XpMult = 1;
    /// <summary>Przedmioty specjalne: reset umiejętności po pokoju, róg wojenny (szał na start walki), leczenie z monet.</summary>
    public float RoomAbility, WarHorn, CoinHeal;

    public static readonly Dictionary<string, FieldInfo> StatFields = BuildStatMap();

    private static Dictionary<string, FieldInfo> BuildStatMap()
    {
        var map = new Dictionary<string, FieldInfo>();
        foreach (var f in typeof(PlayerSim).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (f.FieldType != typeof(float)) continue;
            // PascalCase → snake_case
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < f.Name.Length; i++)
            {
                char ch = f.Name[i];
                if (char.IsUpper(ch) && i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(ch));
            }
            map[sb.ToString()] = f;
        }
        return map;
    }

    public float GetStat(string name) => StatFields.TryGetValue(name, out var f) ? (float)f.GetValue(this)! : 0;

    public void SetStat(string name, float v)
    {
        if (StatFields.TryGetValue(name, out var f))
            f.SetValue(this, v);
        else
            GD.PushWarning($"Nieznana statystyka: {name}");
    }

    // ------------------------------------------------------------------ stan
    public readonly CharacterDef Def;
    public readonly int Idx;
    public string Key => Def.Key;
    public float Hp;
    public float X = C.RCX, Y = C.RCY, R = 17, Vx, Vy, Aim;
    public float FireCd, IFrames, DashT, DashRecharge, DashDx = 1, DashDy, AbilityT, AbilityCd, RageT, InvisT;
    /// <summary>Spowolnienie gracza (szron, zarodniki) – czas w sekundach.</summary>
    public float SlowT;
    public int DashLeft;
    public int Level = 1;
    public float Xp;
    public readonly List<string> Items = new();
    public bool ShieldUp, EshieldUp;
    public float EshieldT, BhT = 6, OrbAngle, Flash, T, LsCd;
    public readonly List<Familiar> Fams = new();
    public readonly List<Vector2> Trail = new();
    public readonly List<(float x, float y, float life)> Ghosts = new();
    public bool Moving, Dead, Downed;
    public float ReviveP, DownedDashCd;
    public Vector2? Decoy;
    public int PendingLevelUps;
    public readonly HashSet<string> Banished = new();
    public int BanishesLeft = Balance.BanishesPerRun;
    public readonly List<string> UpgradeLog = new();
    public bool HitThisRoom, HitThisFloor;

    public bool Active => !Dead && !Downed;

    public static readonly (Color color, string tag)[] Tags = { (new Color("5aaaff"), "P1"), (new Color("ff963c"), "P2") };
    public const float ReviveTime = 2.0f, ReviveRange = 70;

    public PlayerSim(CharacterDef def, int idx = 0)
    {
        Def = def;
        Idx = idx;
        foreach (var (k, v) in def.Stats)
            SetStat(k, v);
        AbilityCd = def.AbilityCd;
        Hp = MaxHp;
        DashLeft = (int)DashCharges;
        ClampStats();
    }

    // ------------------------------------------------------------------ statystyki pochodne
    public void ClampStats()
    {
        Armor = C.Clamp(Armor, -0.5f, 0.7f);
        Dodge = C.Clamp(Dodge, 0, 0.6f);
        Crit = C.Clamp(Crit, 0, 1);
        MaxHp = MathF.Max(10, MathF.Round(MaxHp));
        Hp = MathF.Min(Hp, MaxHp);
        Speed = C.Clamp(Speed, 120, 520);
        FireMult = MathF.Max(0.2f, FireMult);
        DashCharges = MathF.Floor(DashCharges);
        Projectiles = MathF.Floor(Projectiles);
        Range = C.Clamp(Range, 150, 1400);
        ShotSpeed = C.Clamp(ShotSpeed, 200, 1400);
        AbilityCdMult = MathF.Max(0.25f, AbilityCdMult);
        DashCd = MathF.Max(0.3f, DashCd);
    }

    public float XpNeeded() => Balance.XpNeeded(Level);

    public void AddXp(float v, RunSim g)
    {
        Xp += v * XpMult * (g.Coop ? Balance.CoopXp : 1);
        while (Xp >= XpNeeded())
        {
            Xp -= XpNeeded();
            Level++;
            g.OnLevelUp(this);
        }
    }

    public float EffDamage()
    {
        float d = Damage * DmgMult;
        if (Rage > 0) d *= 1 + 0.8f * (1 - Hp / MathF.Max(1, MaxHp));
        if (RageT > 0) d *= 1.4f;
        return d;
    }

    public float FireInterval()
    {
        float fr = FireRate * FireMult;
        if (RageT > 0) fr *= 1.6f;
        return MathF.Max(0.05f, 1f / fr);
    }

    public float MoveSpeed()
    {
        float s = Speed;
        if (RageT > 0) s *= 1.4f;
        if (InvisT > 0) s *= 1.4f;
        if (SlowT > 0) s *= 0.6f;
        return s;
    }

    public void ApplyItem(ItemDef item, RunSim g)
    {
        Items.Add(item.Key);
        foreach (var fx in item.Fx)
        {
            float cur = GetStat(fx.Stat);
            switch (fx.Op)
            {
                case "+":
                    SetStat(fx.Stat, cur + fx.Value);
                    if (fx.Stat == "max_hp" && fx.Value > 0) Hp += fx.Value;
                    break;
                case "*": SetStat(fx.Stat, cur * fx.Value); break;
                case "=": SetStat(fx.Stat, fx.Value); break;
            }
            if (fx.Stat == "dash_charges")
                DashLeft = Math.Min(DashLeft + (int)fx.Value, (int)DashCharges);
        }
        switch (item.Special)
        {
            case "coins15": g.Coins += 15; break;
            case "coins30": g.Coins += 30; break;
            case "chaos": g.GiveRandomItems(2, this); break;
        }
        ClampStats();
        Hp = MathF.Min(Hp, MaxHp);
    }

    // ------------------------------------------------------------------ aktualizacja
    public void Update(float dt, RunSim g, SimInput inp)
    {
        T += dt;
        Flash = MathF.Max(0, Flash - dt);
        if (Downed)
        {
            UpdateDowned(dt, g, inp);
            return;
        }
        IFrames = MathF.Max(0, IFrames - dt);
        SlowT = MathF.Max(0, SlowT - dt);
        AbilityT = MathF.Max(0, AbilityT - dt);
        LsCd = MathF.Max(0, LsCd - dt);
        if (RageT > 0)
        {
            RageT -= dt;
            if (g.Cos.NextDouble() < dt * 25)
                g.Fx.Burst(X + g.CosRange(-14, 14), Y + 10, new Color("ff4632"), 1, 30, 4, 0.5f, grav: -120);
        }
        if (InvisT > 0)
        {
            InvisT -= dt;
            if (InvisT <= 0) Decoy = null;
        }
        if (Regen > 0 && Hp < MaxHp)
            Hp = MathF.Min(MaxHp, Hp + Regen * dt);

        // zryw
        if (DashLeft < DashCharges)
        {
            DashRecharge += dt;
            if (DashRecharge >= DashCd)
            {
                DashRecharge = 0;
                DashLeft++;
            }
        }
        else
            DashRecharge = 0;
        var m = inp.Move;
        if (inp.Dash && DashLeft > 0 && DashT <= 0)
        {
            DashLeft--;
            DashT = 0.17f;
            if (m.LengthSquared() > 0.01f) { var n = m.Normalized(); DashDx = n.X; DashDy = n.Y; }
            else { DashDx = MathF.Cos(Aim); DashDy = MathF.Sin(Aim); }
            g.Fx.Sound("dash", 0.8f);
            g.Fx.Burst(X, Y + 10, new Color("c8c8d2"), 10, 140, 5, 0.4f, kind: "smoke");
            g.Stats.Dashes++;
        }
        if (DashT > 0)
        {
            DashT -= dt;
            float sp = MathF.Max(720, MoveSpeed() * 3.2f);
            Vx = DashDx * sp;
            Vy = DashDy * sp;
            Ghosts.Add((X, Y, 0.25f));
        }
        else
        {
            float tx = m.X * MoveSpeed(), ty = m.Y * MoveSpeed(), k = MathF.Min(1, dt * 14);
            Vx += (tx - Vx) * k;
            Vy += (ty - Vy) * k;
        }
        Moving = MathF.Abs(Vx) + MathF.Abs(Vy) > 30;
        float nx = X + Vx * dt, ny = Y + Vy * dt;
        if (g.CheckDoors(nx, ny, this))
            return;
        var res = g.Room.ResolveCircle(nx, ny, R);
        X = res.X;
        Y = res.Y;
        if (Moving && DashT <= 0 && g.Cos.NextDouble() < dt * 8)
            g.Fx.Burst(X, Y + R - 2, new Color("968c8c"), 1, 20, 3, 0.35f, kind: "smoke");
        for (int i = Ghosts.Count - 1; i >= 0; i--)
        {
            var gh = Ghosts[i];
            gh.life -= dt;
            if (gh.life <= 0) Ghosts.RemoveAt(i);
            else Ghosts[i] = gh;
        }
        Trail.Add(new Vector2(X, Y));
        if (Trail.Count > 90) Trail.RemoveAt(0);

        // celowanie i strzał
        Aim = inp.Aim;
        FireCd -= dt;
        if (inp.Fire && FireCd <= 0)
        {
            Shoot(g);
            FireCd = FireInterval();
        }
        if (FireCd < -1) FireCd = 0;

        if (inp.Ability && AbilityT <= 0)
        {
            UseAbility(g, inp);
            AbilityT = AbilityCd * AbilityCdMult;
            g.Stats.Abilities++;
        }

        // tarcza energii
        if (Eshield > 0 && !EshieldUp)
        {
            EshieldT += dt;
            if (EshieldT >= 12f / (1 + 0.35f * (Eshield - 1)))
            {
                EshieldUp = true;
                EshieldT = 0;
                g.Fx.Sound("shield", 0.6f);
            }
        }

        // czarna dziura
        if (Blackhole > 0 && g.Enemies.Count > 0)
        {
            BhT -= dt;
            if (BhT <= 0)
            {
                BhT = 8f / (1 + 0.3f * (Blackhole - 1));
                var e = g.Enemies[g.R.RangeInt(0, g.Enemies.Count - 1)];
                g.Effects.Add(new BlackHole(e.X, e.Y, MathF.Max(4, EffDamage() * 0.35f)));
                g.Fx.Sound("ability", 0.5f);
            }
        }

        // towarzysze
        while (Fams.Count < (int)Familiars)
            Fams.Add(new Familiar(Fams.Count, g.Cos) { X = X, Y = Y });
        for (int i = 0; i < Fams.Count; i++)
        {
            int idx = Math.Max(0, Trail.Count - 1 - 14 * (i + 1));
            var tp = Trail.Count > 0 ? Trail[idx] : new Vector2(X, Y);
            Fams[i].Update(dt, g, this, tp.X - 18, tp.Y - 26);
        }

        // orbity
        if (Orbitals > 0)
        {
            OrbAngle += dt * 3;
            float od = MathF.Max(5, EffDamage() * 0.6f);
            foreach (var o in OrbitalPositions())
            {
                foreach (var e in g.Enemies.ToArray())
                    if (e.OrbCd <= 0 && (e.X - o.X) * (e.X - o.X) + (e.Y - o.Y) * (e.Y - o.Y) < (e.R + 11) * (e.R + 11))
                    {
                        e.OrbCd = 0.3f;
                        g.DamageEnemy(e, od, color: new Color("8cffff"), small: true, owner: this, source: "orbital");
                    }
                foreach (var b in g.Bullets)
                    if (!b.Friendly && b.Alive && (b.X - o.X) * (b.X - o.X) + (b.Y - o.Y) * (b.Y - o.Y) < (b.R + 10) * (b.R + 10))
                        b.Kill(g);
            }
        }
    }

    public List<Vector2> OrbitalPositions()
    {
        int n = (int)Orbitals;
        var outL = new List<Vector2>(n);
        for (int i = 0; i < n; i++)
        {
            float a = OrbAngle + i * C.Tau / n, rr = 66 + i / 4 * 26;
            outL.Add(new Vector2(X + MathF.Cos(a) * rr, Y + MathF.Sin(a) * rr));
        }
        return outL;
    }

    // ------------------------------------------------------------------ co-op: powalenie i wskrzeszanie
    private void UpdateDowned(float dt, RunSim g, SimInput inp)
    {
        DownedDashCd = MathF.Max(0, DownedDashCd - dt);
        // powalony może zrywem zbić pobliskie pociski wrogów (co 6 s)
        if (inp.Dash && DownedDashCd <= 0)
        {
            DownedDashCd = 6;
            foreach (var b in g.Bullets)
                if (!b.Friendly && (b.X - X) * (b.X - X) + (b.Y - Y) * (b.Y - Y) < 140 * 140)
                    b.Kill(g);
            g.Fx.Ring(X, Y, new Color("aadcff"), 140, 0.35f, 5);
        }
        bool helped = false;
        foreach (var o in g.Players)
            if (o != this && o.Active && (o.X - X) * (o.X - X) + (o.Y - Y) * (o.Y - Y) < ReviveRange * ReviveRange)
                helped = true;
        ReviveP = helped ? ReviveP + dt / ReviveTime : MathF.Max(0, ReviveP - dt / ReviveTime * 0.5f);
        if (ReviveP >= 1)
            Revive(g, 0.35f);
    }

    public void Revive(RunSim g, float frac)
    {
        Downed = false;
        ReviveP = 0;
        Hp = MathF.Max(1, MaxHp * frac);
        IFrames = 2;
        g.Fx.Ring(X, Y, new Color("8cff8c"), 90, 0.5f, 6);
        g.Fx.Text(X, Y - 40, "WSTAŁ!", new Color("8cff8c"), 24);
        g.Fx.Sound("heal");
    }

    // ------------------------------------------------------------------ strzelanie
    public Bullet MakeBullet(RunSim g, float a, float dmgMult = 1, int pierceBonus = 0, float rangeMult = 1, float speedMult = 1,
        bool fx = true, bool small = false)
    {
        float sp = ShotSpeed * speedMult;
        bool crit = g.R.NextFloat() < Crit || InvisT > 0;
        float dmg = EffDamage() * dmgMult;
        if (crit) dmg *= CritMult;
        float r = 6.5f * ShotSize * (small ? 0.6f : 1);
        float mx = X + MathF.Cos(a) * (R + 10), my = Y + MathF.Sin(a) * (R + 10) - 4;
        return new Bullet(mx, my, MathF.Cos(a) * sp, MathF.Sin(a) * sp, dmg, r, Def.BulletCol, true, Range * rangeMult, Def.Bullet,
            pierce: (int)Pierce + pierceBonus, bounce: (int)Bounce, homing: Homing, spectral: Spectral > 0, crit: crit, fx: fx,
            knock: Knockback, source: "player") { Owner = this };
    }

    public void Shoot(RunSim g)
    {
        int n = Math.Max(1, (int)Projectiles);
        float spread = n > 1 ? Mathf.DegToRad(MathF.Min(11 * (n - 1), 70)) : 0;
        float jitter = FireMult * FireRate > 6 ? 0.04f : 0.015f;
        for (int i = 0; i < n; i++)
        {
            float off = n > 1 ? -spread / 2 + spread * i / (n - 1) : 0;
            g.Bullets.Add(MakeBullet(g, Aim + off + g.CosRange(-jitter, jitter)));
        }
        for (int k = 0; k < (int)Backshot; k++)
            g.Bullets.Add(MakeBullet(g, Aim + MathF.PI + (k - (Backshot - 1) / 2) * 0.25f));
        float mx = X + MathF.Cos(Aim) * (R + 14), my = Y + MathF.Sin(Aim) * (R + 14) - 4;
        g.Fx.Spark(mx, my, Def.BulletCol, 3, 200, Aim, 0.6f, 0.12f);
        g.Fx.Sound(Def.Sound, 0.55f, 0.045f);
    }

    // ------------------------------------------------------------------ umiejętności
    public void UseAbility(RunSim g, SimInput inp)
    {
        float pw = AbilityPower;
        g.Fx.Sound("ability", 0.8f);
        switch (Key)
        {
            case "rycerz":
            {
                float rad = 240 * (1 + 0.2f * (pw - 1));
                g.Fx.Ring(X, Y, new Color("bedcff"), rad, 0.4f, 8);
                g.Fx.Ring(X, Y, Colors.White, rad * 0.7f, 0.3f, 4);
                g.Fx.Shake(7);
                foreach (var e in g.Enemies.ToArray())
                {
                    float dx = e.X - X, dy = e.Y - Y, d = MathF.Max(1, MathF.Sqrt(dx * dx + dy * dy));
                    if (d < rad + e.R)
                        g.DamageEnemy(e, EffDamage() * 3 * pw, kx: dx / d * 800, ky: dy / d * 800, owner: this, source: "ability");
                }
                foreach (var b in g.Bullets)
                    if (!b.Friendly && (b.X - X) * (b.X - X) + (b.Y - Y) * (b.Y - Y) < rad * 1.1f * rad * 1.1f)
                        b.Kill(g);
                break;
            }
            case "lowczyni":
            {
                int n = (int)(20 * (1 + 0.5f * (pw - 1)));
                for (int i = 0; i < n; i++)
                    g.Bullets.Add(MakeBullet(g, i * C.Tau / n + Aim, 1.3f * pw, 3, 1.3f, 1.1f));
                g.Fx.Ring(X, Y, new Color("e6ffc8"), 70, 0.3f);
                break;
            }
            case "berserker":
                RageT = 5 * pw;
                Heal(MaxHp * 0.1f, g);
                g.Fx.Ring(X, Y, new Color("ff4632"), 120, 0.4f, 8);
                g.Fx.Shake(5);
                break;
            case "mag":
                g.ChainLightning(X, Y, EffDamage() * 4 * pw, 7 + (int)(2 * (pw - 1)), 650, 340, owner: this);
                g.Fx.Ring(X, Y, new Color("c8a0ff"), 80, 0.3f);
                break;
            case "alchemik":
            {
                var t = inp.Target;
                float dx = t.X - X, dy = t.Y - Y, d = MathF.Sqrt(dx * dx + dy * dy);
                if (d > 400) t = new Vector2(X + dx / d * 400, Y + dy / d * 400);
                t = new Vector2(C.Clamp(t.X, C.RX0 + 30, C.RX1 - 30), C.Clamp(t.Y, C.RY0 + 30, C.RY1 - 30));
                g.Effects.Add(new Flask(X, Y, t.X, t.Y, EffDamage() * 2.4f * pw, 110 * (1 + 0.25f * (pw - 1)), 5));
                break;
            }
            case "zlodziej":
                InvisT = 2.5f * pw;
                IFrames = MathF.Max(IFrames, InvisT);
                Decoy = new Vector2(X, Y);
                g.Fx.Burst(X, Y, new Color("78788c"), 30, 220, 9, 0.9f, kind: "smoke");
                break;
            case "upadly":
            {
                float cost = MathF.Max(1, Hp * 0.08f);
                if (Hp - cost >= 1) Hp -= cost;
                float rad = 260 * (1 + 0.2f * (pw - 1));
                g.Fx.Ring(X, Y, new Color("ff3250"), rad, 0.45f, 10);
                g.Fx.Burst(X, Y, new Color("ff2846"), 40, 500, 6, 0.6f, glow: true);
                g.Fx.Shake(10);
                foreach (var e in g.Enemies.ToArray())
                {
                    float dx = e.X - X, dy = e.Y - Y, d = MathF.Max(1, MathF.Sqrt(dx * dx + dy * dy));
                    if (d < rad + e.R)
                        g.DamageEnemy(e, EffDamage() * 6 * pw, kx: dx / d * 500, ky: dy / d * 500, owner: this, source: "ability");
                }
                break;
            }
            default:
                // nowe postacie (etap 3) dopiszą tu swoje umiejętności
                break;
        }
    }

    // ------------------------------------------------------------------ obrażenia / leczenie
    public bool Hurt(float dmg, RunSim g, float sx, float sy, string source = null)
    {
        if (!Active || IFrames > 0 || DashT > 0 || InvisT > 0)
            return false;
        if (g.God)
        {
            g.Stats.GodHits++;
            g.Stats.GodDmg += dmg;
            IFrames = 1;
            return false;
        }
        if (g.R.NextFloat() < Dodge)
        {
            g.Fx.Text(X, Y - 30, "UNIK", new Color("b4e6ff"), 18);
            IFrames = 0.35f;
            g.Fx.Sound("dodge");
            return false;
        }
        if (ShieldUp || EshieldUp)
        {
            if (ShieldUp) ShieldUp = false;
            else { EshieldUp = false; EshieldT = 0; }
            IFrames = 0.8f;
            g.Fx.Sound("block");
            g.Fx.Ring(X, Y, new Color("78dcff"), 50, 0.3f, 5);
            g.Fx.Text(X, Y - 30, "BLOK", new Color("78dcff"), 18);
            return false;
        }
        int d = Math.Max(1, (int)MathF.Round(dmg * (1 - Armor)));
        Hp -= d;
        IFrames = 1;
        Flash = 0.15f;
        HitThisRoom = HitThisFloor = true;
        g.Fx.Shake(9);
        g.Fx.HurtFlash();
        g.Hitstop = MathF.Max(g.Hitstop, 0.05f);
        g.Fx.Sound("player_hurt", 0.9f);
        g.Fx.Text(X, Y - 34, $"-{d}", new Color("ff505a"), 22);
        g.Fx.Burst(X, Y, new Color("e63c46"), 12, 250, 5, 0.5f);
        g.Stats.DamageTaken += d;
        if (Hp <= 0)
        {
            if (Revives > 0)
            {
                Revives--;
                Hp = MaxHp * 0.5f;
                IFrames = 2.5f;
                g.Fx.Sound("item");
                g.Fx.Ring(X, Y, new Color("ffa03c"), 300, 0.6f, 10);
                g.Fx.Burst(X, Y, new Color("ffaa3c"), 50, 500, 6, 0.8f, glow: true);
                g.Fx.Banner("WSKRZESZENIE!", "Pióro Feniksa spłonęło", new Color("ff963c"));
                g.ClearEnemyBullets();
                Items.Remove("feniks");
            }
            else
            {
                Hp = 0;
                g.OnPlayerDown(this);
            }
        }
        return true;
    }

    public void Heal(float v, RunSim g, bool show = true)
    {
        if (v <= 0 || !Active) return;
        float before = Hp;
        Hp = MathF.Min(MaxHp, Hp + v);
        float gained = Hp - before;
        if (show && gained >= 1)
            g.Fx.Text(X, Y - 36, $"+{(int)gained}", new Color("78ff82"), 18);
    }

    public void OnHit(RunSim g)
    {
        if (Lifesteal > 0 && LsCd <= 0 && g.R.NextFloat() < Lifesteal)
        {
            LsCd = 0.12f;
            Heal(1 + MaxHp * 0.012f, g, false);
            g.Fx.Burst(X, Y - 10, new Color("78ff82"), 2, 60, 3, 0.4f);
        }
    }

    /// <summary>Szacowane DPS (do statystyk i symulacji balansu).</summary>
    public float DpsEstimate() => EffDamage() * (1 + Crit * (CritMult - 1)) * MathF.Max(1, Projectiles) / FireInterval();
}
