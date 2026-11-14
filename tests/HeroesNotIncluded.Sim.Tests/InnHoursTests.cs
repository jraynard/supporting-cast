using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// From the first playtests: customers kept out of the kitchen, the town's hours kept (nothing
/// asked in the small hours, patience waiting for the morning), and the mealtime rushes.
/// </summary>
public class InnHoursTests
{
    private static void TickTill(Simulation sim, float hour)
    {
        for (var i = 0; i < 24 * 60 && (int)(sim.Clock.HourOfDay * 60) != (int)(hour * 60); i++)
            sim.TickMany(SimClock.TicksPerHour / 60);
    }

    /// <summary>A round with a party that stays in town: no quests on the sign, and days to stay.</summary>
    private static (Simulation Sim, List<Entity> Party) Staying()
    {
        var (sim, _, party) = ExpeditionTests.Round("Cook", tillTheyAsk: false);
        var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        if (!sim.World.Has<ShopPrices>(inn))
            sim.World.Add(inn, ShopPrices.Defaults(sim.Defs));
        sim.World.Get<ShopPrices>(inn).Offered[(int)ServiceKind.Quest] = false;
        foreach (var e in party)
            sim.World.Get<Adventurer>(e).LeaveTick = sim.Clock.Tick + SimClock.TicksPerDay * 4;
        return (sim, party);
    }

    private static int3 InnCell(Simulation sim, int col, int row)
    {
        var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        var b = sim.World.Get<PresetBuilding>(inn);
        return Buildings.CellOf(sim.Map, b.Def, b.Anchor, b.Facing, col, row);
    }

    [Fact]
    public void TheKitchenIsStaffOnlyAndIdlersKeepOutOfIt()
    {
        var (sim, _, party) = ExpeditionTests.Round("Cook");
        Assert.True(AiQueries.StaffOnly(sim, InnCell(sim, 4, 2)));
        Assert.False(AiQueries.StaffOnly(sim, InnCell(sim, 4, 7)));
        var center = InnCell(sim, 6, 6);
        var rand = new SimRandom(3);
        for (var i = 0; i < 200; i++)
            if (AiQueries.RandomReachableNear(sim, party[0], center, 4, rand, publicOnly: true) is { } spot)
                Assert.False(AiQueries.StaffOnly(sim, spot), $"{spot} is in the kitchen");
    }

    [Fact]
    public void NothingIsAskedInTheSmallHoursButARoomOrABadHurt()
    {
        var (sim, party) = Staying();
        TickTill(sim, ShopSystem.SmallHoursFrom + 0.1f);
        var before = sim.Shops.All.Select(o => o.Id).ToHashSet();
        foreach (var e in party)
        {
            sim.World.Get<Adventurer>(e).NextDrink = sim.Clock.Tick; // thirsty, and hungry
            sim.World.Get<Needs>(e).Values[sim.Defs.Need("Food").Index] = 0.3f;
        }
        TickTill(sim, ShopSystem.SmallHoursUntil - 0.25f);
        var asked = sim.Shops.All.Where(o => !before.Contains(o.Id)).ToList();
        Assert.All(asked, o => Assert.Contains(o.Kind.Kind, new[] { ServiceKind.Lodging, ServiceKind.Healing }));
    }

    [Fact]
    public void PatienceWaitsForTheMorning()
    {
        var (sim, party) = Staying();
        TickTill(sim, ShopSystem.SmallHoursFrom - 0.5f);
        var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        var order = sim.Shops.Post(sim, party[0], sim.Defs.Service(ServiceKind.Meal), "Food!", 3, sim.Clock.Tick + SimClock.HoursToTicks(2), where: inn);
        TickTill(sim, ShopSystem.SmallHoursUntil - 0.5f);
        Assert.True(order.IsOpen || order.Status == OrderStatus.Done, $"{order.Status}");
    }

    [Fact]
    public void AtBreakfastThePeckishOrderAMeal()
    {
        var (sim, party) = Staying();
        TickTill(sim, ShopSystem.BreakfastFromHour - 0.25f);
        Assert.False(ShopSystem.Mealtime(sim));
        foreach (var e in party)
            sim.World.Get<Needs>(e).Values[sim.Defs.Need("Food").Index] = 0.8f; // not hungry, but ready for it
        var before = sim.Shops.All.Select(o => o.Id).ToHashSet();
        TickTill(sim, ShopSystem.BreakfastUntilHour - 0.1f); // any who ate as the night ended can ask again by now
        Assert.True(ShopSystem.Mealtime(sim));
        Assert.Contains(sim.Shops.All, o => !before.Contains(o.Id) && o.Kind.Kind == ServiceKind.Meal);
    }

    [Fact]
    public void AMealOnlyHalfWantedIsShruggedOff()
    {
        var (sim, _, party) = ExpeditionTests.Round("Cook");
        var diner = party[0];
        sim.World.Get<Needs>(diner).Values[sim.Defs.Need("Food").Index] = 0.8f;
        var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        var order = sim.Shops.Post(sim, diner, sim.Defs.Service(ServiceKind.Meal), "Food!", 3, sim.Clock.Tick + 60, where: inn);
        var before = sim.World.Get<Adventurer>(diner).Satisfaction;
        for (var i = 0; i < 20 && order.IsOpen; i++)
            sim.TickMany(SimClock.TicksPerHour / 12);
        if (order.Status == OrderStatus.Expired)
            Assert.True(sim.World.Get<Adventurer>(diner).Satisfaction > before - 0.5f);
    }
}
