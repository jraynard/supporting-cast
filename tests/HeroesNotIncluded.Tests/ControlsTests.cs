using Narsil.Platform;

namespace HeroesNotIncluded.Tests;

public class ControlsTests
{
    [Fact]
    public void DefaultsNeverClash()
    {
        var map = new ActionMap(Controls.Defs) { Overlap = Controls.Overlaps };
        var clashes = map.Conflicts().Select(c => $"{c.Binding} is both {c.A} and {c.B}").ToList();
        Assert.True(clashes.Count == 0, string.Join("\n", clashes));
    }

    [Fact]
    public void EveryActionAnswersToSomething()
    {
        foreach (var def in Controls.Defs)
            Assert.True(def.Keys.Length + def.Pad.Length > 0, $"{def.Id} has no default binding");
    }

    [Fact]
    public void KeyboardDefaultsAreTheOldKeys()
    {
        var map = new ActionMap(Controls.Defs);
        Assert.Equal("F", map.Glyph(Act.Use, InputDevice.KeyboardMouse));
        Assert.Equal("R", map.Glyph(Act.Draw, InputDevice.KeyboardMouse));
        Assert.Equal("Q", map.Glyph(Act.Guard, InputDevice.KeyboardMouse));
        Assert.Equal("Space", map.Glyph(Act.Dash, InputDevice.KeyboardMouse));
        Assert.Equal("LMB", map.Glyph(Act.Strike, InputDevice.KeyboardMouse));
        Assert.Equal("RT", map.Glyph(Act.Strike, InputDevice.Gamepad));
        Assert.Equal("LB", map.Glyph(Act.Guard, InputDevice.Gamepad));
    }

    [Fact]
    public void BindingsRoundTripThroughSettings()
    {
        var s = new Settings { Bindings = new() { [Act.Guard] = ["Key:E", "Pad:LB"] } };
        var back = Settings.Deserialize(Settings.Serialize(s));
        Assert.Equal(["Key:E", "Pad:LB"], back.Bindings![Act.Guard]);

        // A copy doesn't share them: the settings window's draft edits its own.
        var copy = s.Clone();
        copy.Bindings![Act.Guard] = ["Key:G"];
        Assert.Equal("Key:E", s.Bindings[Act.Guard][0]);
    }

    [Fact]
    public void HandEditedBindingsAreCleaned()
    {
        var s = Settings.Deserialize("""{ "Bindings": { "guard": [ "Key:E", "Key:Nope" ], "flying": [ "Key:Space" ], "use": [ "Key:F", "Pad:A" ] } }""");
        Assert.Equal(["Key:E"], s.Bindings![Act.Guard]);
        Assert.False(s.Bindings.ContainsKey("flying"));
        Assert.False(s.Bindings.ContainsKey(Act.Use)); // just the defaults: nothing to keep
        Assert.Null(Settings.Deserialize("""{ "Bindings": { "use": [ "Key:F", "Pad:A" ] } }""").Bindings);
    }
}
