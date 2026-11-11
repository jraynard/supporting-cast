using Brutal.Numerics;
using Narsil.Rendering;
using SupportingCast.Rendering;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;

namespace SupportingCast.Tests;

public class AnimationTests
{
    // Manifest parsing and frame timing are engine behavior, tested in Narsil.Tests.

    [Theory]
    [InlineData("downed", true, false, false, false, false, 0, false, false, false, false)]
    [InlineData("sleep", false, true, false, false, false, 0, false, false, false, true)]
    [InlineData("breathe", false, false, true, true, false, 0, false, false, false, false)]
    [InlineData("breathe_windup", false, false, false, true, false, 0, false, false, false, false)]
    [InlineData("takeoff", false, false, false, false, true, 1, false, false, false, true)]
    [InlineData("land", false, false, false, false, true, -1, true, false, false, true)]
    [InlineData("glide", false, false, false, false, true, 0, false, true, false, true)]
    [InlineData("fly", false, false, false, false, true, 0, false, false, false, true)]
    [InlineData("work", false, false, false, false, false, 0, false, false, true, false)]
    [InlineData("walk", false, false, false, false, false, 0, false, false, false, true)]
    [InlineData("idle", false, false, false, false, false, 0, false, false, false, false)]
    [InlineData("hatch", false, false, false, false, false, 0, false, false, false, true, true)]
    public void ChoosesTagByPriority(string expected, bool downed, bool asleep, bool breathing, bool windup, bool airborne, int vertical, bool landing, bool soaring, bool working, bool moving, bool hatching = false)
    {
        var inputs = new AnimInputs(false, downed, asleep, breathing, windup, airborne, vertical, landing, soaring, working, moving, hatching);
        Assert.Equal(expected, CreatureAnimator.ChooseTag(inputs));
    }

    [Fact]
    public void ADragonBeingCaredForShowsItUnlessItsAsleepOrOnTheMove()
    {
        AnimInputs Care(bool asleep = false, bool moving = false, bool airborne = false) =>
            new(false, false, asleep, false, false, airborne, 0, false, false, false, moving, Care: "play");
        Assert.Equal("play", CreatureAnimator.ChooseTag(Care()));
        Assert.Equal("sleep", CreatureAnimator.ChooseTag(Care(asleep: true)));
        Assert.Equal("walk", CreatureAnimator.ChooseTag(Care(moving: true)));
        Assert.Equal("fly", CreatureAnimator.ChooseTag(Care(airborne: true)));
        foreach (var tag in new[] { "pet", "play", "groom" })
            Assert.Equal("idle", CreatureAnimator.Fallback(tag));
    }

    [Fact]
    public void PeopleSheetsHaveTheRidePoseInEveryView()
    {
        foreach (var body in new[] { "male", "female" })
        {
            var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(DevPaths.Defs!, "..", "textures", "creatures", $"person_{body}.anim.json")));
            var tags = json.RootElement.GetProperty("tags"u8).ToString();
            foreach (var view in new[] { "ride", "ride_down", "ride_downside", "ride_upside", "ride_up" })
                Assert.Contains($"\"{view}\"", tags);
        }
    }

    [Fact]
    public void MissingTagsFallBack()
    {
        SheetFrame F() => new(default, default, 0.1f);
        var sheet = new SpriteSheet
        {
            Name = "test",
            FrameSize = new int2(32, 32),
            Pivot = new float2(16, 31),
            Points = new Dictionary<string, float2>(),
            Frames = [F()],
            Anims = new Dictionary<string, SheetAnim>
            {
                ["idle"] = new("idle", [F()], true, -1),
                ["fly"] = new("fly", [F()], true, -1),
            },
        };
        Assert.Equal("fly", Resolved(sheet, "glide"));
        Assert.Equal("fly", Resolved(sheet, "takeoff"));
        Assert.Equal("idle", Resolved(sheet, "breathe"));  // breathe → windup → idle
        Assert.Equal("idle", Resolved(sheet, "downed"));   // downed → sleep → idle
        Assert.Equal("idle", Resolved(sheet, "work"));

        static string Resolved(SpriteSheet s, string tag)
        {
            Assert.NotNull(CreatureAnimator.Resolve(s, tag, out var resolved));
            return resolved;
        }
    }

    [Theory]
    [InlineData(1f, 0f, "downside")]     // straight across: turned toward the viewer (no profiles)
    [InlineData(-1f, 0.2f, "downside")]
    [InlineData(0.7f, 0.7f, "upside")]   // a grid diagonal up the screen: turned away
    [InlineData(0.7f, -0.7f, "downside")]
    [InlineData(0.2f, 1f, "up")]         // walking up the screen: we see their back
    [InlineData(0f, -1f, "down")]        // towards the viewer
    public void ViewFollowsTheScreenDirection(float side, float away, string expected) =>
        Assert.Equal(expected, CreatureAnimator.ViewOf(side, away));

    [Fact]
    public void FourWaySheetsHaveTheirOwnFramesAndOthersDont()
    {
        SheetFrame F() => new(default, default, 0.1f);
        SheetAnim A(string n) => new(n, [F()], true, -1);
        var person = new SpriteSheet
        {
            Name = "person", FrameSize = new int2(32, 48), Pivot = new float2(16, 46), Points = new Dictionary<string, float2>(), Frames = [F()],
            Anims = new Dictionary<string, SheetAnim> { ["walk"] = A("walk"), ["walk_up"] = A("walk_up"), ["walk_side"] = A("walk_side") },
        };
        var dragon = new SpriteSheet
        {
            Name = "dragon", FrameSize = new int2(96, 80), Pivot = new float2(46, 77), Points = new Dictionary<string, float2>(), Frames = [F()],
            Anims = new Dictionary<string, SheetAnim> { ["walk"] = A("walk") },
        };
        Assert.Equal("walk_up", CreatureAnimator.Directional(person, "walk", "up")!.Name);
        Assert.Null(CreatureAnimator.Directional(person, "walk", "down")); // no such frames: the plain tag shows
        Assert.Null(CreatureAnimator.Directional(dragon, "walk", "side"));
    }

    /// <summary>The shipped art covers every def and every animation the creatures use.</summary>
    [Fact]
    public void ShippedArtPassesAudit()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var packed = AtlasPacker.Pack(Path.Combine(data, "textures"));
        var report = ArtAudit.Run(DefDatabase.LoadDirectory(Path.Combine(data, "defs")), packed.Sprites, packed.Sheets);
        Assert.Empty(report.Errors);
        Assert.Empty(report.Warnings);
        // Sheets resolve by name to their first frame, so static-sprite code still works.
        Assert.Equal(packed.Sheets["creatures/dragon_adult"].Frames[0].Sprite, packed.Sprites["creatures/dragon_adult"]);
    }

    /// <summary>
    /// The base sheet looks the same with arms crossed and a hand on the hip (the arms are in the
    /// skin layer), so those frames share one atlas sprite; the layers must still follow the stance.
    /// </summary>
    [Fact]
    public void LayersFollowTheStanceEvenWhenTheBaseFramesMatch()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var sheets = AtlasPacker.Pack(Path.Combine(data, "textures")).Sheets;
        var look = new Appearance { Body = BodyType.Female };
        var body = sheets[HumanLook.BaseSheet(look)];
        var skin = sheets[HumanLook.SkinSheet(look)];
        foreach (var view in (string[])["down", "downside", "upside", "up"])
        {
            var crossed = body.Find(HumanLook.IdleTag(body, view, Stance.Crossed))!.Frames[0];
            var hip = body.Find(HumanLook.IdleTag(body, view, Stance.Hip))!.Frames[0];
            Assert.Equal(crossed.Sprite, hip.Sprite);   // what makes a value search go wrong
            Assert.NotEqual(HumanLook.LayerFrame(body, skin, crossed)!.Value.Sprite, HumanLook.LayerFrame(body, skin, hip)!.Value.Sprite);
        }
    }

    /// <summary>name, name_v2, name_v3... are grouped as variants of name, base first and in number order.</summary>
    [Fact]
    public void GroupsSpriteVariants()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var packed = AtlasPacker.Pack(Path.Combine(data, "textures"));
        var grass = packed.Variants["terrain/grass"];
        Assert.True(grass.Length >= 2);
        Assert.Equal(packed.Sprites["terrain/grass"], grass[0]);
        Assert.Equal(packed.Sprites["terrain/grass_v2"], grass[1]);
        Assert.Equal(3, packed.Variants["things/oak"].Length);
        Assert.False(packed.Variants.ContainsKey("terrain/slope_grass")); // no variants: callers get a list of one
    }
}
