using Brutal.Numerics;
using SupportingCast.Rendering;
using SupportingCast.Sim.Components;

namespace SupportingCast.Tests;

/// <summary>Dragon elements: each one's starting palette and flame colour (<see cref="DragonLooks"/>).</summary>
public class DragonLooksTests
{
    [Fact]
    public void EveryElementIsOfferedOnceWithItsOwnFlameColour()
    {
        Assert.Equal(Enum.GetValues<DragonElement>().Order(), DragonLooks.Elements.Select(e => e.Element).Order());
        var flames = Enum.GetValues<DragonElement>().Select(DragonLooks.FlameColor).ToList();
        Assert.Equal(flames.Count, flames.Distinct().Count());
    }

    [Fact]
    public void ANewDragonHasBlackHornsAndItsElementsColoursAllOnTheSwatches()
    {
        foreach (var (element, _) in DragonLooks.Elements)
        {
            var look = DragonLooks.ForElement(element);
            Assert.Equal(element, look.Element);
            Assert.Equal(DragonLooks.BlackHorns, look.Horns);
            Assert.Contains(look.Scales, DragonLooks.ScaleColors);
            Assert.Contains(look.Belly, DragonLooks.BellyColors);
            Assert.Contains(look.Wings, DragonLooks.WingColors);
            Assert.Contains(look.Horns, DragonLooks.HornColors);
            Assert.Contains(look.Eyes, DragonLooks.EyeColors);
            Assert.Equal(DragonLooks.FlameColor(element), DragonLooks.ColorOf(look, "flame"));
        }
        Assert.Equal(DragonLooks.ScaleColors.Length, DragonLooks.ScaleColors.Distinct().Count());
    }

    [Fact]
    public void TheFlameFollowsTheElementNotAnyPick()
    {
        var ice = DragonLooks.ForElement(DragonElement.Ice) with { Scales = 0xFF0000, Eyes = 0x00FF00 };
        Assert.Equal(DragonLooks.FlameColor(DragonElement.Ice), DragonLooks.ColorOf(ice, "flame"));
    }

    [Fact]
    public void RandomizedDragonsAreAlwaysAValidElementLook()
    {
        var rng = new Random(5);
        for (var i = 0; i < 200; i++)
        {
            var look = DragonLooks.Random(rng);
            Assert.True(Enum.IsDefined(look.Element));
            Assert.Equal(look.Element, look.Valid().Element);
        }
        Assert.Equal(DragonElement.Fire, new DragonLook { Element = (DragonElement)99 }.Valid().Element);
    }

    [Fact]
    public void FlameColoursAreTheElementDefsOwn()
    {
        // The renderer and creator draw from DragonLooks; what an element is lives in dragons.json.
        var defs = Sim.Defs.DefDatabase.LoadDirectory(DevPaths.Defs!);
        foreach (var element in Enum.GetValues<DragonElement>())
            Assert.Equal(defs.Element(element).Rgb, DragonLooks.FlameColor(element));
    }

    [Fact]
    public void ADragonWithoutALookWearsItsElementsColours()
    {
        var defs = Sim.Defs.DefDatabase.LoadDirectory(DevPaths.Defs!);
        var sim = Sim.Simulation.CreateStandard(defs, 20, 20, 1, 4);
        var fire = Sim.Systems.Dragons.Spawn(sim, null, DragonElement.Fire, "A", new int3(5, 5, 1));
        var ice = Sim.Systems.Dragons.Spawn(sim, null, DragonElement.Ice, "B", new int3(8, 5, 1));
        Assert.Equal(DragonElement.Fire, DragonLooks.Of(sim, fire).Element);
        Assert.Equal(DragonElement.Ice, DragonLooks.Of(sim, ice).Element);
        Assert.Equal(DragonLooks.ForElement(DragonElement.Ice).Scales, DragonLooks.Of(sim, ice).Scales);
    }
}
