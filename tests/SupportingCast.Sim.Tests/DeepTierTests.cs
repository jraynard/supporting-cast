using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Tests;

/// <summary>The bottom of the ladder (docs/plans/mining-progression.md): ember crystal in the basalt, lime mortar, the dragon blade.</summary>
public class DeepTierTests
{
    [Theory]
    [InlineData(1234UL)]
    [InlineData(99UL)]
    public void EmberCrystalLiesInTheDeepBasalt(ulong seed)
    {
        var (sim, _) = TestWorld.Millbrook(seed);
        var ember = sim.Defs.Block("EmberCrystal");
        var cells = Enumerable.Range(0, sim.Map.Count).Where(i => sim.Map.Block[i] == ember).ToList();
        Assert.True(cells.Count > 3, $"{cells.Count} ember crystal on seed {seed}");
        Assert.All(cells, i => Assert.True(sim.Map.CellOf(i).Z <= 1, "only in the bottom basalt"));
        Assert.Equal(5, (int)ember.DigTier); // steel
    }

    [Fact]
    public void OnlyLimestoneBurnsToLime()
    {
        var sim = TestWorld.Create();
        var recipe = sim.Defs.Recipe("BurnLime");
        Assert.True(Crafting.Accepts(recipe.Ingredients[0], null, sim.Thing("Stone"), sim.Defs.Material("Limestone")));
        Assert.False(Crafting.Accepts(recipe.Ingredients[0], null, sim.Thing("Stone"), sim.Defs.Material("Granite")));
    }

    [Fact]
    public void MortarMakesTheToughestWarmestStoneWall()
    {
        var defs = TestWorld.LoadDefs();
        var mortared = defs.Thing("MortaredWall");
        var cut = defs.Thing("CutStoneWall");
        Assert.True(mortared.MaxHp > cut.MaxHp);
        Assert.True(mortared.Building!.Insulation > cut.Building!.Insulation);
        Assert.Contains(mortared.Building.Cost, c => c.ThingDef.Name == "Lime");
    }

    [Fact]
    public void RockBesideHiddenOreIsStained()
    {
        var defs = TestWorld.LoadDefs();
        foreach (var ore in defs.AllBlocks.Where(b => b.Ore))
            Assert.True(ore.Stain is { Length: 3 }, $"{ore.Name} has no stain");
    }
}
