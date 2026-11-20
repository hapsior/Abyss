using Godot;
using Otchlan.Core;
using Otchlan.Input;

namespace Otchlan.Tests;

public static class CoreTests
{
    [Test]
    public static void RngIsDeterministic()
    {
        var a = new Rng(12345);
        var b = new Rng(12345);
        for (int i = 0; i < 1000; i++)
            Assert.Equal(a.NextUInt(), b.NextUInt());
    }

    [Test]
    public static void RngStateRoundtrip()
    {
        var a = new Rng(777);
        for (int i = 0; i < 50; i++) a.NextFloat();
        var b = new Rng(a.GetState());
        for (int i = 0; i < 100; i++)
            Assert.Equal(a.RangeInt(0, 1000), b.RangeInt(0, 1000));
    }

    [Test]
    public static void RngRangesStayInBounds()
    {
        var r = new Rng(1);
        for (int i = 0; i < 10000; i++)
        {
            float f = r.NextFloat();
            Assert.True(f >= 0 && f < 1, $"NextFloat poza [0,1): {f}");
            int n = r.RangeInt(-3, 5);
            Assert.True(n >= -3 && n <= 5, $"RangeInt poza zakresem: {n}");
        }
    }

    [Test]
    public static void SettingsRoundtrip()
    {
        var s = new Settings { MaxFps = 144, VSync = false, WindowMode = WindowModeSetting.Okno, SfxVolume = 0.3f };
        Assert.True(JsonStore.Save("test_settings.json", s));
        var l = JsonStore.Load<Settings>("test_settings.json");
        JsonStore.Delete("test_settings.json");
        Assert.Equal(144, l.MaxFps);
        Assert.Equal(false, l.VSync);
        Assert.Equal(WindowModeSetting.Okno, l.WindowMode);
        Assert.Near(0.3, l.SfxVolume, 1e-6);
    }

    [Test]
    public static void GlyphsFollowDevice()
    {
        InputManager.RegisterActions();
        var kb = InputManager.GlyphFor(Act.Interact, InputDevice.KeyboardMouse);
        Assert.True(kb.IsKey, "na klawiaturze interakcja to klawisz");
        Assert.Equal("E", kb.KeyLabel);
        var pad = InputManager.GlyphFor(Act.Interact, InputDevice.Gamepad);
        Assert.Equal("res://art/glyphs/xbox_x.svg", pad.Texture);
        Assert.Equal("res://art/glyphs/xbox_a.svg", InputManager.GlyphFor(Act.Dash, InputDevice.Gamepad).Texture);
        Assert.Equal("res://art/glyphs/mouse_left.svg", InputManager.GlyphFor(Act.Fire, InputDevice.KeyboardMouse).Texture);
        Assert.Equal("res://art/glyphs/xbox_rt.svg", InputManager.GlyphFor(Act.Fire, InputDevice.Gamepad).Texture);
    }
}
