using Brutal.Numerics;
using Narsil.Platform;

namespace DragonWorld.Tests;

public class SettingsTests
{
    [Fact]
    public void RoundTripsThroughJson()
    {
        var s = new Settings { WindowMode = WindowMode.Fullscreen, Width = 2560, Height = 1440, Monitor = "DP-1", VSync = false, MaxFps = 144, UiScale = 1.25f, PauseOnDanger = false, CameraPanSpeed = 2f };
        var json = Settings.Serialize(s);
        Assert.Contains("\"Fullscreen\"", json); // enums as names, so the file is hand-editable
        Assert.Equal(json, Settings.Serialize(Settings.Deserialize(json)));
    }

    [Fact]
    public void BadOrPartialFilesFallBackToDefaults()
    {
        Assert.Equal(Settings.Serialize(new Settings()), Settings.Serialize(Settings.Deserialize("{ not json")));
        var partial = Settings.Deserialize("""{ "UiScale": 9, "Width": 10, "WindowMode": "Borderless" }""");
        Assert.Equal(Settings.MaxUiScale, partial.UiScale);
        Assert.Equal(320, partial.Width);
        Assert.Equal(WindowMode.Borderless, partial.WindowMode);
        Assert.True(partial.VSync);
    }

    [Fact]
    public void BorderlessKeepsTheWindowedSize()
    {
        var s = new Settings { Width = 1280, Height = 720 };
        s.Display = new DisplaySettings { Mode = WindowMode.Borderless, Size = new int2(3840, 2160) };
        Assert.Equal(WindowMode.Borderless, s.WindowMode);
        Assert.Equal((1280, 720), (s.Width, s.Height));
    }
}
