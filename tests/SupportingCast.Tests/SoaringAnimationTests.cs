using Brutal.Numerics;
using Narsil.Rendering;
using SupportingCast.Rendering;
using SupportingCast.Sim;
using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Tests;

/// <summary>A soaring dragon flaps as well as glides (the glide frames alone are a held pose: wings looked frozen).</summary>
public class SoaringAnimationTests
{
    [Fact]
    public void ASoaringDragonBeatsItsWingsBetweenGlides()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var sheet = AtlasPacker.Pack(Path.Combine(data, "textures")).Sheets["creatures/dragon_adult"];
        var defs = DefDatabase.LoadDirectory(Path.Combine(data, "defs"));
        var sim = Simulation.CreateStandard(defs, 40, 40, 1, 12);
        sim.RebuildDerivedIfNeeded();
        var dragon = sim.SpawnCreature(defs.Creature("Dragon"), "Ember", new int3(20, 20, 1), ageDays: 20);
        var route = Navigation.SoaringRoute(sim, dragon, new int3(20, 20, 1), 10);
        Assert.NotNull(route);
        AiSystem.Order(sim, dragon, new SoarJob(route!));

        var anim = new CreatureAnimator();
        var cam = new Camera3D { Zoom = 30f };
        var tags = new HashSet<string>();
        var flapFrames = new HashSet<int>();
        for (var f = 0; f < 60 * 6; f++) // 6 s, one and a half soaring cycles
        {
            anim.Advance(1 / 60f);
            sim.Tick();
            var pose = anim.Pose(sim, dragon, sheet, cam);
            tags.Add(pose.Tag);
            if (pose.Tag == "fly")
                flapFrames.Add(Array.IndexOf(sheet.Frames, pose.Frame));
        }
        Assert.Contains("glide", tags);
        Assert.Contains("fly", tags);
        Assert.True(flapFrames.Count >= 4, $"a full wingbeat plays (frames {string.Join(",", flapFrames)})");
    }
}
