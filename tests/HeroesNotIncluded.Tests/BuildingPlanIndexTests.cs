using Brutal.Numerics;
using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Tests;

/// <summary><see cref="BuildingPlanIndex"/> gives the sim's own answers, cell for cell, read from an index.</summary>
public class BuildingPlanIndexTests
{
    [Fact]
    public void EveryCellOfMillbrookIsInTheBuildingTheSimSays()
    {
        var (sim, _) = CoopSessionTests.Millbrook(7, 4);
        var map = sim.Map;
        var index = BuildingPlanIndex.For(sim);
        Assert.True(sim.World.Count<PresetBuilding>() > 5);
        var claimed = 0;
        for (var z = 0; z < map.Depth; z++)
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var cell = new int3(x, y, z);
            var at = Buildings.At(sim, cell);
            Assert.Equal(at, index.At(cell));
            claimed += at.IsNull ? 0 : 1;
            Assert.Equal(PlanFloor(sim, cell), index.IsPlanFloor(cell));
        }
        Assert.True(claimed > 100);
    }

    [Fact]
    public void ANewBuildingIsIndexedOnceItsPlaced()
    {
        var (sim, _) = CoopSessionTests.Millbrook(8, 2);
        var w = sim.World;
        var first = w.Query<PresetBuilding>()[0];
        var cell = PlotCell(sim, first);
        Assert.Equal(first, BuildingPlanIndex.For(sim).At(cell));

        // Gone from the world: its plot is nobody's.
        w.Remove<PresetBuilding>(first);
        Assert.Equal(Buildings.At(sim, cell), BuildingPlanIndex.For(sim).At(cell));
        Assert.NotEqual(first, BuildingPlanIndex.For(sim).At(cell));
    }

    /// <summary>The sim's answer: some building's plan has an open floor ('.') there.</summary>
    private static bool PlanFloor(Simulation sim, int3 cell)
    {
        var w = sim.World;
        foreach (var e in w.Each<PresetBuilding>())
        {
            var b = w.Get<PresetBuilding>(e);
            if (Buildings.SymbolAt(b.Def, b.Anchor, b.Facing, cell) == '.')
                return true;
        }
        return false;
    }

    private static int3 PlotCell(Simulation sim, Entity building)
    {
        var map = sim.Map;
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var cell = new int3(x, y, sim.World.Get<PresetBuilding>(building).Anchor.Z);
            if (Buildings.At(sim, cell) == building)
                return cell;
        }
        throw new InvalidOperationException("the building has no plot");
    }
}
