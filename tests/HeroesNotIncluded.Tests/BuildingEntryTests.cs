using Brutal.Numerics;
using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Tests;

/// <summary>
/// <see cref="BuildingEntry"/>: a click on a roof walks you inside (a playtester clicked the
/// middle of the inn and nothing happened), and standing at a door keeps its building open to view.
/// </summary>
public class BuildingEntryTests
{
    [Fact]
    public void AClickOnTheInnsRoofWalksInside()
    {
        var (sim, _) = CoopSessionTests.Millbrook(7, 2);
        var map = sim.Map;
        var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        var plan = sim.World.Get<PresetBuilding>(inn);
        var roof = RoofCellNearest(sim, inn, plan.Anchor);

        var inside = BuildingEntry.Inside(sim, roof, Entity.Null);
        Assert.NotNull(inside);
        var c = inside.Value;
        Assert.Equal(plan.Anchor.Z, c.Z);
        Assert.Equal(inn, BuildingPlanIndex.For(sim).At(c));
        Assert.True(map.Standable(map.Idx(c)));
        Assert.True(map.Structure[map.Idx(c)].IsNull);

        // With the inn open to view (you're in it) its roof isn't drawn: the click means itself.
        Assert.Null(BuildingEntry.Inside(sim, roof, inn));
        // Nor does a click on its floor or out in the street change.
        Assert.Null(BuildingEntry.Inside(sim, c, Entity.Null));
    }

    [Fact]
    public void StandingAtTheShrinesDoorOpensIt()
    {
        var (sim, _) = CoopSessionTests.Millbrook(7, 2);
        var map = sim.Map;
        var shrine = ShopSystem.BuildingOf(sim, sim.Defs.Building("Shrine"));
        var index = BuildingPlanIndex.For(sim);
        int3? outside = null;
        foreach (var part in Buildings.Parts(sim, shrine))
        {
            if (sim.World.Get<Thing>(part).Def.Building is not { IsDoor: true })
                continue;
            var door = sim.World.Get<Thing>(part).Cell;
            foreach (var d in new[] { new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 1, 0), new int3(0, -1, 0) })
            {
                var c = door + d;
                if (map.InBounds(c) && index.At(c).IsNull && map.Standable(map.Idx(c)))
                    outside = c;
            }
        }
        Assert.NotNull(outside);
        Assert.Equal(shrine, BuildingEntry.DoorBeside(sim, outside.Value));

        // Two steps further out, the door's out of reach.
        var away = outside.Value;
        var step = away - DoorCell(sim, shrine);
        Assert.Equal(Entity.Null, BuildingEntry.DoorBeside(sim, away + step + step));
    }

    private static int3 DoorCell(Simulation sim, Entity building) =>
        Buildings.Parts(sim, building).Select(p => sim.World.Get<Thing>(p)).First(t => t.Def.Building is { IsDoor: true }).Cell;

    /// <summary>A cell of the building's roof, nearest over its middle.</summary>
    private static int3 RoofCellNearest(Simulation sim, Entity building, int3 middle)
    {
        var map = sim.Map;
        var index = BuildingPlanIndex.For(sim);
        int3? best = null;
        var bestDistance = int.MaxValue;
        for (var z = middle.Z + 1; z < map.Depth; z++)
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var c = new int3(x, y, z);
            var floor = map.Floor[map.Idx(c)];
            if (floor.IsNull || !sim.World.Get<Thing>(floor).Def.IsRoof || index.At(c) != building)
                continue;
            var d = (x - middle.X) * (x - middle.X) + (y - middle.Y) * (y - middle.Y);
            if (d < bestDistance)
                (best, bestDistance) = (c, d);
        }
        Assert.NotNull(best);
        return best.Value;
    }
}
