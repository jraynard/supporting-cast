using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Each workplace keeps its own fuel: a day and a half's at the deal, its fires fed from it alone, the carter bringing more to order.</summary>
public class FuelStoreTests
{
    private static int Count(Simulation sim, Entity store, string def) =>
        sim.StoredIn(store).Where(e => sim.World.Get<Thing>(e).Def.Name == def).Sum(e => sim.World.Get<Stack>(e).Count);

    private static Entity Store(Simulation sim, string building, string def)
    {
        var b = Shops.ShopSystem.BuildingOf(sim, sim.Defs.Building(building));
        return FuelStores.In(sim, b).First(e => sim.World.Get<Thing>(e).Def.Name == def);
    }

    [Fact]
    public void EachWorkplaceOpensWithADayAndAHalfOfItsOwnFuel()
    {
        var sim = Scenarios.VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        Scenarios.VillageRound.Setup(sim);
        var days = sim.Defs.Season.WorkplaceFuelDays;
        Assert.Equal((int)MathF.Ceiling((12 + 6) * days), Count(sim, Store(sim, "Inn", "LogBasket"), "Firewood")); // the kitchen's fireplace and oven
        Assert.Equal((int)MathF.Ceiling(12 * days), Count(sim, Store(sim, "Inn", "LogRack"), "Firewood")); // the common room's fireplace
        Assert.Equal((int)MathF.Ceiling(12 * days), Count(sim, Store(sim, "Shrine", "LogRack"), "Firewood")); // the hearth
        Assert.Equal(sim.Defs.Season.SmithyCharcoal, Count(sim, Store(sim, "Smithy", "FuelBin"), "Charcoal"));
        Assert.Equal(sim.Defs.Season.SmithyFirewood, Count(sim, Store(sim, "Smithy", "FuelBin"), "Firewood")); // for the kiln
    }

    [Fact]
    public void AWorkplacesFireIsntFedFromTheBarnButFromItsOwnRack()
    {
        var (sim, _, _) = ExpeditionTests.Round("Blacksmith"); // the inn kept by villagers
        var w = sim.World;
        var rack = Store(sim, "Inn", "LogRack");
        var fire = w.Query<Thing>().First(e => w.Get<Thing>(e).Def.Name == "Fireplace" && FuelStores.For(sim, e) == rack);
        foreach (var log in sim.StoredIn(rack).ToList())
            sim.DestroyThing(log);
        w.Get<Fueled>(fire).Fuel = 0;
        sim.TickMany(SimClock.TicksPerHour * 3);
        Assert.Equal(0f, w.Get<Fueled>(fire).Fuel); // the barn's firewood is the homes'

        sim.Store(rack, sim.CreateItem(sim.Defs.Thing("Firewood"), 16, sim.Defs.Material("Pine")));
        sim.TickMany(SimClock.TicksPerHour * 3);
        Assert.True(w.Get<Fueled>(fire).Fuel > 0, "fed from its own rack");
    }

    [Fact]
    public void TheCarterTopsUpAnAiRunShopsRackOvernight()
    {
        var (sim, _, _) = ExpeditionTests.Round("Blacksmith"); // the inn run by the AI: its standing orders go out each evening
        var rack = Store(sim, "Inn", "LogRack");
        foreach (var log in sim.StoredIn(rack).ToList())
            sim.DestroyThing(log);
        var day = sim.Clock.Day;
        while (sim.Clock.Day == day || sim.Clock.HourOfDay < 7)
            sim.Tick();
        Assert.True(Count(sim, rack, "Firewood") >= 8, $"the carter brought firewood to the rack: {Count(sim, rack, "Firewood")}");
    }
}
