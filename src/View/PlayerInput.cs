using System;
using System.Collections.Generic;
using Godot;
using Otchlan.Core;
using Otchlan.Input;
using Otchlan.Sim;

namespace Otchlan.View;

/// <summary>
/// Źródło wejścia jednego gracza. Każde źródło czyta tylko swoje urządzenie (klawiatura+mysz, prawa strona
/// klawiatury albo konkretny pad), więc w co-op naciśnięcia się nie mieszają. Przypisania pochodzą z InputMap.
/// </summary>
public abstract class InputSource
{
    protected readonly Dictionary<string, bool> Prev = new();

    public abstract InputDevice Device { get; }
    public abstract SimInput Read(RunScene scene, PlayerSim p);

    protected bool JustPressed(string action, bool now)
    {
        Prev.TryGetValue(action, out var was);
        Prev[action] = now;
        return now && !was;
    }

    /// <summary>Automatyczne celowanie w najbliższego wroga (zwraca false, gdy nie ma wrogów).</summary>
    protected static bool AutoAim(RunSim g, PlayerSim p, SimInput inp)
    {
        var e = g.NearestEnemy(p.X, p.Y, 900);
        if (e == null) return false;
        inp.Aim = MathF.Atan2(e.Y - p.Y, e.X - p.X);
        inp.Target = new Vector2(e.X, e.Y);
        return true;
    }
}

/// <summary>Klawiatura + mysz (gracz 1). Strzałki strzelają w kierunku jak w Isaacu.</summary>
public sealed class KeyboardMouseSource : InputSource
{
    public override InputDevice Device => InputDevice.KeyboardMouse;
    public bool AutoAimOn;

    public static bool KbPressed(string action)
    {
        if (!InputMap.HasAction(action)) return false;
        foreach (var e in InputMap.ActionGetEvents(action))
        {
            if (e is InputEventKey k && Godot.Input.IsPhysicalKeyPressed(k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode))
                return true;
            if (e is InputEventMouseButton mb && Godot.Input.IsMouseButtonPressed(mb.ButtonIndex))
                return true;
        }
        return false;
    }

    public override SimInput Read(RunScene scene, PlayerSim p)
    {
        var g = scene.Sim;
        var inp = new SimInput();
        var mv = new Vector2((KbPressed(Act.MoveRight) ? 1 : 0) - (KbPressed(Act.MoveLeft) ? 1 : 0),
            (KbPressed(Act.MoveDown) ? 1 : 0) - (KbPressed(Act.MoveUp) ? 1 : 0));
        inp.Move = mv.LengthSquared() > 1 ? mv.Normalized() : mv;
        var mouse = scene.MouseWorld();
        inp.Target = mouse;
        inp.Aim = MathF.Atan2(mouse.Y - p.Y, mouse.X - p.X);
        inp.Fire = KbPressed(Act.Fire);
        var shoot = new Vector2((KbPressed(Act.ShootRight) ? 1 : 0) - (KbPressed(Act.ShootLeft) ? 1 : 0),
            (KbPressed(Act.ShootDown) ? 1 : 0) - (KbPressed(Act.ShootUp) ? 1 : 0));
        if (shoot.LengthSquared() > 0)
        {
            inp.Aim = shoot.Angle();
            inp.Fire = true;
        }
        else if (AutoAimOn && !inp.Fire && AutoAim(g, p, inp))
            inp.Fire = true;
        inp.Dash = JustPressed(Act.Dash, KbPressed(Act.Dash));
        inp.Ability = JustPressed(Act.Ability, KbPressed(Act.Ability));
        inp.Interact = JustPressed(Act.Interact, KbPressed(Act.Interact));
        return inp;
    }
}

/// <summary>Prawa strona klawiatury (gracz 2 bez pada): strzałki – ruch, IJKL / numeryczne – strzał, inaczej auto-celowanie.</summary>
public sealed class KeyboardRightSource : InputSource
{
    public override InputDevice Device => InputDevice.KeyboardMouse;

    public override SimInput Read(RunScene scene, PlayerSim p)
    {
        var inp = new SimInput();
        var mv = new Vector2((KeyboardMouseSource.KbPressed(Act.P2Right) ? 1 : 0) - (KeyboardMouseSource.KbPressed(Act.P2Left) ? 1 : 0),
            (KeyboardMouseSource.KbPressed(Act.P2Down) ? 1 : 0) - (KeyboardMouseSource.KbPressed(Act.P2Up) ? 1 : 0));
        inp.Move = mv.LengthSquared() > 1 ? mv.Normalized() : mv;
        var shoot = new Vector2((KeyboardMouseSource.KbPressed(Act.P2ShootRight) ? 1 : 0) - (KeyboardMouseSource.KbPressed(Act.P2ShootLeft) ? 1 : 0),
            (KeyboardMouseSource.KbPressed(Act.P2ShootDown) ? 1 : 0) - (KeyboardMouseSource.KbPressed(Act.P2ShootUp) ? 1 : 0));
        inp.Aim = p.Aim;
        if (shoot.LengthSquared() > 0)
        {
            inp.Aim = shoot.Angle();
            inp.Fire = true;
        }
        else if (AutoAim(scene.Sim, p, inp))
            inp.Fire = true;
        inp.Dash = JustPressed(Act.P2Dash, KeyboardMouseSource.KbPressed(Act.P2Dash));
        inp.Ability = JustPressed(Act.P2Ability, KeyboardMouseSource.KbPressed(Act.P2Ability));
        inp.Interact = JustPressed(Act.P2Interact, KeyboardMouseSource.KbPressed(Act.P2Interact));
        return inp;
    }
}

/// <summary>Pad o danym numerze: lewa gałka – ruch, prawa – celowanie i strzał, RT – strzał, A/B/X – zryw/umiejętność/interakcja.</summary>
public sealed class GamepadSource : InputSource
{
    public int Joypad;
    public bool AutoAimOn;
    private float _lastAim;

    public GamepadSource(int joypad) => Joypad = joypad;
    public override InputDevice Device => InputDevice.Gamepad;

    private bool PadPressed(string action)
    {
        if (!InputMap.HasAction(action)) return false;
        foreach (var e in InputMap.ActionGetEvents(action))
        {
            if (e is InputEventJoypadButton jb && Godot.Input.IsJoyButtonPressed(Joypad, jb.ButtonIndex))
                return true;
            if (e is InputEventJoypadMotion jm && Godot.Input.GetJoyAxis(Joypad, jm.Axis) * MathF.Sign(jm.AxisValue) > 0.5f)
                return true;
        }
        return false;
    }

    private static Vector2 Stick(int dev, JoyAxis ax, JoyAxis ay, float dead)
    {
        var v = new Vector2(Godot.Input.GetJoyAxis(dev, ax), Godot.Input.GetJoyAxis(dev, ay));
        float l = v.Length();
        if (l < dead) return Vector2.Zero;
        return v / l * MathF.Min(1, (l - dead) / (1 - dead));
    }

    public override SimInput Read(RunScene scene, PlayerSim p)
    {
        var inp = new SimInput();
        var ls = Stick(Joypad, JoyAxis.LeftX, JoyAxis.LeftY, 0.2f);
        var dpad = new Vector2((Godot.Input.IsJoyButtonPressed(Joypad, JoyButton.DpadRight) ? 1 : 0) - (Godot.Input.IsJoyButtonPressed(Joypad, JoyButton.DpadLeft) ? 1 : 0),
            (Godot.Input.IsJoyButtonPressed(Joypad, JoyButton.DpadDown) ? 1 : 0) - (Godot.Input.IsJoyButtonPressed(Joypad, JoyButton.DpadUp) ? 1 : 0));
        inp.Move = ls.LengthSquared() > 0 ? ls : dpad.Normalized();
        var rs = Stick(Joypad, JoyAxis.RightX, JoyAxis.RightY, 0.3f);
        if (rs.LengthSquared() > 0)
        {
            _lastAim = rs.Angle();
            inp.Fire = true;
        }
        else if (inp.Move.LengthSquared() > 0.04f && !PadPressed(Act.Fire))
            _lastAim = Mathf.LerpAngle(_lastAim, inp.Move.Angle(), 0.15f);
        inp.Aim = _lastAim;
        if (PadPressed(Act.Fire)) inp.Fire = true;
        if (AutoAimOn && rs.LengthSquared() == 0 && AutoAim(scene.Sim, p, inp))
        {
            _lastAim = inp.Aim;
            inp.Fire = true;
        }
        inp.Target = new Vector2(p.X + MathF.Cos(inp.Aim) * 220, p.Y + MathF.Sin(inp.Aim) * 220);
        inp.Dash = JustPressed(Act.Dash, PadPressed(Act.Dash));
        inp.Ability = JustPressed(Act.Ability, PadPressed(Act.Ability));
        inp.Interact = JustPressed(Act.Interact, PadPressed(Act.Interact));
        return inp;
    }

    public bool PausePressed() => JustPressed(Act.Pause, PadPressed(Act.Pause));
}

/// <summary>Gracz 1 w trybie solo: klawiatura+mysz i dowolny pad naraz – liczy się ostatnio używane urządzenie.</summary>
public sealed class SoloSource : InputSource
{
    private readonly KeyboardMouseSource _kb = new();
    private readonly GamepadSource _pad = new(0);

    public override InputDevice Device => Game.I.InputMgr.LastDevice;

    public override SimInput Read(RunScene scene, PlayerSim p)
    {
        var im = Game.I.InputMgr;
        bool auto = Game.I.Settings.AutoAimP1 || scene.AutoAimToggled;
        _kb.AutoAimOn = auto;
        _pad.AutoAimOn = auto;
        var k = _kb.Read(scene, p);
        if (im.LastDevice == InputDevice.Gamepad && im.LastJoypad >= 0)
        {
            _pad.Joypad = im.LastJoypad;
            var g = _pad.Read(scene, p);
            // klawiatura też działa (np. Esc), ale pad ma pierwszeństwo przy ruchu i celowaniu
            g.Dash |= k.Dash;
            g.Ability |= k.Ability;
            g.Interact |= k.Interact;
            return g;
        }
        _pad.Read(scene, p); // aktualizuje stany „wciśnięte przed chwilą”
        return k;
    }
}

/// <summary>Sterowanie przez bota (autotest, zrzuty ekranu, symulacje).</summary>
public sealed class BotSource : InputSource
{
    private Tests.Bot _bot;
    public override InputDevice Device => InputDevice.Gamepad;

    public override SimInput Read(RunScene scene, PlayerSim p)
    {
        _bot ??= new Tests.Bot(scene.Sim, p, (ulong)(p.Idx + 11));
        return _bot.Input();
    }
}
