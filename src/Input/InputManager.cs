using System;
using System.Collections.Generic;
using Godot;

namespace Otchlan.Input;

public enum InputDevice
{
    KeyboardMouse,
    Gamepad,
}

/// <summary>Nazwy akcji wejścia (InputMap). Domyślne przypisania w <see cref="InputManager.RegisterActions"/>.</summary>
public static class Act
{
    public const string MoveLeft = "move_left", MoveRight = "move_right", MoveUp = "move_up", MoveDown = "move_down";
    public const string ShootLeft = "shoot_left", ShootRight = "shoot_right", ShootUp = "shoot_up", ShootDown = "shoot_down";
    public const string Fire = "fire", Dash = "dash", Ability = "ability", Interact = "interact";
    public const string Stats = "stats", Pause = "pause", AutoAim = "autoaim";
    public const string Reroll = "reroll", Banish = "banish";
    public const string TabPrev = "tab_prev", TabNext = "tab_next";
    public const string Accept = "ui_accept", Cancel = "ui_cancel";

    // Gracz 2 na klawiaturze (prawa strona), gdy nie ma pada.
    public const string P2Left = "p2_left", P2Right = "p2_right", P2Up = "p2_up", P2Down = "p2_down";
    public const string P2ShootLeft = "p2_shoot_left", P2ShootRight = "p2_shoot_right", P2ShootUp = "p2_shoot_up", P2ShootDown = "p2_shoot_down";
    public const string P2Dash = "p2_dash", P2Ability = "p2_ability", P2Interact = "p2_interact";

    // Pseudo-akcje używane tylko w podpowiedziach (nie ma ich w InputMap).
    public const string GlyphMove = "@move", GlyphAim = "@aim";
}

/// <summary>
/// Śledzi, czym gracz ostatnio sterował (klawiatura+mysz czy pad), i informuje UI,
/// żeby podpowiedzi pokazywały właściwe przyciski (Xbox albo klawisze).
/// </summary>
public partial class InputManager : Node
{
    public InputDevice LastDevice { get; private set; } = InputDevice.KeyboardMouse;
    public int LastJoypad { get; private set; } = -1;

    /// <summary>Wywoływane przy zmianie urządzenia – podpowiedzi przerysowują się.</summary>
    public event Action<InputDevice> DeviceChanged;

    public event Action JoypadsChanged;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        RegisterActions();
        Godot.Input.Singleton.JoyConnectionChanged += OnJoyConnectionChanged;
        if (Godot.Input.GetConnectedJoypads().Count > 0)
            SetDevice(InputDevice.Gamepad, Godot.Input.GetConnectedJoypads()[0]);
    }

    public override void _ExitTree()
    {
        Godot.Input.Singleton.JoyConnectionChanged -= OnJoyConnectionChanged;
    }

    private void OnJoyConnectionChanged(long device, bool connected)
    {
        if (connected)
            SetDevice(InputDevice.Gamepad, (int)device);
        else if (Godot.Input.GetConnectedJoypads().Count == 0)
            SetDevice(InputDevice.KeyboardMouse, -1);
        JoypadsChanged?.Invoke();
    }

    public override void _Input(InputEvent e)
    {
        switch (e)
        {
            case InputEventJoypadButton jb when jb.Pressed:
                SetDevice(InputDevice.Gamepad, jb.Device);
                break;
            case InputEventJoypadMotion jm when Mathf.Abs(jm.AxisValue) > 0.5f:
                SetDevice(InputDevice.Gamepad, jm.Device);
                break;
            case InputEventKey k when k.Pressed:
            case InputEventMouseButton mb when mb.Pressed:
                SetDevice(InputDevice.KeyboardMouse, LastJoypad);
                break;
            case InputEventMouseMotion mm when mm.Relative.Length() > 6:
                SetDevice(InputDevice.KeyboardMouse, LastJoypad);
                break;
        }
    }

    public void SetDevice(InputDevice d, int joypad)
    {
        if (d == InputDevice.Gamepad)
            LastJoypad = joypad;
        if (d == LastDevice)
            return;
        LastDevice = d;
        // przy grze padem chowamy kursor myszy
        if (DisplayServer.GetName() != "headless")
            Godot.Input.MouseMode = d == InputDevice.Gamepad ? Godot.Input.MouseModeEnum.Hidden : Godot.Input.MouseModeEnum.Visible;
        DeviceChanged?.Invoke(d);
    }

    public static IReadOnlyList<int> Joypads => Godot.Input.GetConnectedJoypads();

    // ------------------------------------------------------------------ przypisania
    private static void Bind(string action, params InputEvent[] events)
    {
        if (!InputMap.HasAction(action))
            InputMap.AddAction(action, 0.25f);
        foreach (var e in events)
            InputMap.ActionAddEvent(action, e);
    }

    private static InputEventKey K(Key key) => new() { PhysicalKeycode = key };
    private static InputEventMouseButton M(MouseButton b) => new() { ButtonIndex = b };
    private static InputEventJoypadButton J(JoyButton b) => new() { ButtonIndex = b, Device = -1 };
    private static InputEventJoypadMotion A(JoyAxis axis, float dir) => new() { Axis = axis, AxisValue = dir, Device = -1 };

    private static void EnsurePad(string action, JoyButton button)
    {
        foreach (var e in InputMap.ActionGetEvents(action))
            if (e is InputEventJoypadButton)
                return;
        InputMap.ActionAddEvent(action, J(button));
    }

    public static void RegisterActions()
    {
        if (InputMap.HasAction(Act.MoveLeft))
            return;
        Bind(Act.MoveLeft, K(Key.A), A(JoyAxis.LeftX, -1), J(JoyButton.DpadLeft));
        Bind(Act.MoveRight, K(Key.D), A(JoyAxis.LeftX, 1), J(JoyButton.DpadRight));
        Bind(Act.MoveUp, K(Key.W), A(JoyAxis.LeftY, -1), J(JoyButton.DpadUp));
        Bind(Act.MoveDown, K(Key.S), A(JoyAxis.LeftY, 1), J(JoyButton.DpadDown));
        Bind(Act.ShootLeft, K(Key.Left));
        Bind(Act.ShootRight, K(Key.Right));
        Bind(Act.ShootUp, K(Key.Up));
        Bind(Act.ShootDown, K(Key.Down));
        Bind(Act.Fire, M(MouseButton.Left), A(JoyAxis.TriggerRight, 1));
        Bind(Act.Dash, K(Key.Space), K(Key.Shift), J(JoyButton.A), J(JoyButton.LeftShoulder));
        Bind(Act.Ability, M(MouseButton.Right), K(Key.Q), J(JoyButton.B), J(JoyButton.RightShoulder));
        Bind(Act.Interact, K(Key.E), J(JoyButton.X));
        Bind(Act.Stats, K(Key.Tab), J(JoyButton.Y), J(JoyButton.Back));
        Bind(Act.Pause, K(Key.Escape), K(Key.P), J(JoyButton.Start));
        Bind(Act.AutoAim, K(Key.F));
        Bind(Act.Reroll, K(Key.R), J(JoyButton.X));
        Bind(Act.Banish, K(Key.B), J(JoyButton.Y));
        Bind(Act.TabPrev, K(Key.Q), J(JoyButton.LeftShoulder));
        Bind(Act.TabNext, K(Key.E), J(JoyButton.RightShoulder));

        // Domyślne ui_accept / ui_cancel w Godocie nie zawsze mają przyciski pada – dokładamy A / B.
        EnsurePad(Act.Accept, JoyButton.A);
        EnsurePad(Act.Cancel, JoyButton.B);

        Bind(Act.P2Left, K(Key.Left));
        Bind(Act.P2Right, K(Key.Right));
        Bind(Act.P2Up, K(Key.Up));
        Bind(Act.P2Down, K(Key.Down));
        Bind(Act.P2ShootLeft, K(Key.J), K(Key.Kp4));
        Bind(Act.P2ShootRight, K(Key.L), K(Key.Kp6));
        Bind(Act.P2ShootUp, K(Key.I), K(Key.Kp8));
        Bind(Act.P2ShootDown, K(Key.K), K(Key.Kp5), K(Key.Kp2));
        Bind(Act.P2Dash, K(Key.Ctrl), K(Key.Kp0));
        Bind(Act.P2Ability, K(Key.Shift), K(Key.KpEnter));
        Bind(Act.P2Interact, K(Key.Enter), K(Key.KpAdd));
    }

    // ------------------------------------------------------------------ podpowiedzi
    /// <summary>Opis glifu dla akcji: obrazek pada/myszy albo napis na klawiszu.</summary>
    public readonly record struct Glyph(string Texture, string KeyLabel, bool Wide)
    {
        public bool IsKey => KeyLabel != null;
    }

    public Glyph GlyphFor(string action) => GlyphFor(action, LastDevice);

    public static Glyph GlyphFor(string action, InputDevice device)
    {
        if (action == Act.GlyphMove)
            return device == InputDevice.Gamepad ? Tex("xbox_ls") : new Glyph(null, "WASD", true);
        if (action == Act.GlyphAim)
            return device == InputDevice.Gamepad ? Tex("xbox_rs") : Tex("mouse_move");
        if (!InputMap.HasAction(action))
            return new Glyph(null, "?", false);
        foreach (var e in InputMap.ActionGetEvents(action))
        {
            if (device == InputDevice.Gamepad)
            {
                if (e is InputEventJoypadButton jb)
                    return Tex(PadButtonGlyph(jb.ButtonIndex));
                if (e is InputEventJoypadMotion jm)
                    return Tex(jm.Axis switch
                    {
                        JoyAxis.TriggerRight => "xbox_rt",
                        JoyAxis.TriggerLeft => "xbox_lt",
                        JoyAxis.RightX or JoyAxis.RightY => "xbox_rs",
                        _ => "xbox_ls",
                    });
            }
            else
            {
                if (e is InputEventKey k)
                {
                    var label = KeyLabel(k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode);
                    return new Glyph(null, label, label.Length > 2);
                }
                if (e is InputEventMouseButton mb)
                    return Tex(mb.ButtonIndex == MouseButton.Right ? "mouse_right" : "mouse_left");
            }
        }
        // brak przypisania dla tego urządzenia – pokaż cokolwiek
        return device == InputDevice.Gamepad ? new Glyph(null, "?", false) : GlyphFor(action, InputDevice.Gamepad);
    }

    private static Glyph Tex(string name) => new($"res://art/glyphs/{name}.svg", null, name is "xbox_lb" or "xbox_rb");

    private static string PadButtonGlyph(JoyButton b) => b switch
    {
        JoyButton.A => "xbox_a",
        JoyButton.B => "xbox_b",
        JoyButton.X => "xbox_x",
        JoyButton.Y => "xbox_y",
        JoyButton.LeftShoulder => "xbox_lb",
        JoyButton.RightShoulder => "xbox_rb",
        JoyButton.Start => "xbox_menu",
        JoyButton.Back => "xbox_view",
        JoyButton.LeftStick => "xbox_ls",
        JoyButton.RightStick => "xbox_rs",
        JoyButton.DpadUp => "xbox_dpad_up",
        JoyButton.DpadDown => "xbox_dpad_down",
        JoyButton.DpadLeft => "xbox_dpad_left",
        JoyButton.DpadRight => "xbox_dpad_right",
        _ => "xbox_a",
    };

    public static string KeyLabel(Key key) => key switch
    {
        Key.Space => "Spacja",
        Key.Escape => "Esc",
        Key.Enter => "Enter",
        Key.KpEnter => "Num Enter",
        Key.Tab => "Tab",
        Key.Shift => "Shift",
        Key.Ctrl => "Ctrl",
        Key.Alt => "Alt",
        Key.Backspace => "Backspace",
        Key.Left => "←",
        Key.Right => "→",
        Key.Up => "↑",
        Key.Down => "↓",
        Key.Kp0 => "Num 0",
        // Klawisz fizyczny → napis zgodny z układem klawiatury gracza (AZERTY itd.); bez okna nie da się tego sprawdzić.
        _ => OS.GetKeycodeString(DisplayServer.GetName() == "headless" ? key : DisplayServer.KeyboardGetKeycodeFromPhysical(key)) is { Length: > 0 } s
            ? s
            : key.ToString(),
    };
}
