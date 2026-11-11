using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Systems;
using static SupportingCast.Sim.Tests.TestWorld;

namespace SupportingCast.Sim.Tests;

/// <summary>Grown dragons working for the village: hauling several stacks a trip.</summary>
public class DragonWorkTests
{
    private const int Stones = 4 * 40;

    /// <summary>A warm day, a stockpile in the corner, a pile of stone stacks and an obedient adult that only hauls.</summary>
    private static (Simulation Sim, Entity Dragon) Quarry()
    {
        var sim = Create();
        sim.Climate.BaseTemperature = 25;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 25f);
        for (var y = 3; y < 8; y++)
        for (var x = 3; x < 8; x++)
            sim.Map.SetZone(sim.Map.Idx(C(x, y)), 1);
        foreach (var at in (ReadOnlySpan<int3>)[C(28, 28), C(29, 28), C(28, 30), C(30, 30)])
            sim.SpawnItem(sim.Thing("Stone"), 40, at);
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Vesper", C(20, 20), ageDays: 14);
        sim.World.Get<Dragon>(dragon).Affinity = 2f;
        var work = sim.World.Get<Mind>(dragon).WorkPriority;
        Array.Fill(work, 0);
        work[(int)WorkType.Haul] = 1;
        return (sim, dragon);
    }

    private static int StoneIn(Simulation sim, bool stockpiled) =>
        sim.SpawnedItems().Where(e => sim.World.Get<Thing>(e).Def.Name == "Stone" && AiQueries.InStockpile(sim, sim.World.Get<Thing>(e).Cell) == stockpiled)
            .Sum(e => sim.World.Get<Stack>(e).Count);

    [Fact]
    public void AnAdultHaulsSeveralStacksInOneTrip()
    {
        var (sim, dragon) = Quarry();
        var most = 0;
        for (var t = 0; t < SimClock.HoursToTicks(2) && StoneIn(sim, stockpiled: true) < Stones; t++)
        {
            Array.Fill(sim.World.Get<Needs>(dragon).Values, 1f);
            sim.Tick();
            var load = sim.World.Get<Carrying>(dragon).Item;
            if (!load.IsNull)
                most = Math.Max(most, sim.World.Get<Stack>(load).Count);
        }
        Assert.Equal(Stones, StoneIn(sim, stockpiled: true));
        Assert.Equal(Stones, most); // all four stacks at once, well past the stack limit
        Assert.True(most > sim.Thing("Stone").StackLimit);
    }

    [Fact]
    public void AnInterruptedHaulPutsTheWholeLoadDown()
    {
        var (sim, dragon) = Quarry();
        for (var t = 0; t < SimClock.HoursToTicks(1); t++)
        {
            sim.Tick();
            var load = sim.World.Get<Carrying>(dragon).Item;
            if (!load.IsNull && sim.World.Get<Stack>(load).Count > sim.Thing("Stone").StackLimit)
                break;
        }
        Assert.False(sim.World.Get<Carrying>(dragon).Item.IsNull, "the dragon should be carrying a big load");

        AiSystem.InterruptAll(sim);
        Assert.True(sim.World.Get<Carrying>(dragon).Item.IsNull);
        Assert.Equal(Stones, StoneIn(sim, stockpiled: false) + StoneIn(sim, stockpiled: true));
        Assert.All(sim.SpawnedItems(), e => Assert.True(sim.World.Get<Stack>(e).Count <= sim.World.Get<Thing>(e).Def.StackLimit));
    }

    [Fact]
    public void YoungDragonsCarryOneStackAtATime()
    {
        var defs = LoadDefs();
        var dragon = defs.Creature("Dragon");
        Assert.Equal(1, dragon.LifeStages.First(s => s.Name == "Juvenile").CarryStacks);
        Assert.True(dragon.LifeStages.First(s => s.Name == "Adult").CarryStacks > 1);
        Assert.Equal(1, defs.Creature("Human").StageForAge(9000).CarryStacks);
    }
}
