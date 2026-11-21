using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// The town's own wants (<see cref="Commissions.Wants"/>): AI tradespeople running short of something
/// another shop makes send a commission up to the notice board, for the player who keeps that shop;
/// and a bot keeping it makes the lot and brings it.
/// </summary>
public class TownWantsTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private static Simulation Round(string trade = "Carpenter")
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(trade)], []));
        sim.ApplyNow(new StartRoundCommand());
        return sim;
    }

    private static Entity Holder(Simulation sim, string trade) =>
        sim.World.Query<Trade>().First(e => sim.World.Get<Trade>(e).Def.Name == trade && !Players.Steered(sim, e));

    /// <summary>Take every one of a thing out of a building (its chests and its floor).</summary>
    private static void Empty(Simulation sim, Entity building, string def)
    {
        var w = sim.World;
        foreach (var part in Buildings.Parts(sim, building))
            foreach (var item in sim.StoredIn(part).ToList())
                if (w.Get<Thing>(item).Def.Name == def)
                    sim.DestroyThing(item);
        foreach (var item in sim.AvailableItems().ToList())
            if (w.Get<Thing>(item).Def.Name == def && w.Get<Thing>(item).Spawned && Buildings.At(sim, w.Get<Thing>(item).Cell) == building)
                sim.DestroyThing(item);
    }

    /// <summary>The AI tinker, out of planks with silver to spare: it wants some.</summary>
    private static Entity ShortOfPlanks(Simulation sim)
    {
        var tinker = Holder(sim, "Tinker");
        Empty(sim, ShopSystem.WorkplaceOf(sim, sim.World.Get<Trade>(tinker).Def), "Planks");
        Inventory.Stow(sim, tinker, sim.CreateItem(sim.Thing(Coin.Silver), 20));
        return tinker;
    }

    [Fact]
    public void AnAiTinkerShortOfPlanksCommissionsThemOnceFromThePlayersWoodshop()
    {
        var sim = Round();
        var tinker = ShortOfPlanks(sim);
        Commissions.Wants(sim);
        Commissions.Wants(sim);
        var wanted = sim.Shops.All.Where(e => e.IsOpen && e.Commission && e.Poster == tinker && e.Wants?.Name == "Planks").ToList();
        Assert.Single(wanted);
        Assert.True(wanted[0].Price > 0);
        Assert.Equal(sim.Defs.Building("Woodshop"), sim.World.Get<PresetBuilding>(wanted[0].Where).Def);
        Assert.Contains(Players.CharacterOf(sim, Ana), wanted[0].KnownBy); // the carpenter hears of it
        // A player's own trade sends nothing up for them.
        var me = Players.CharacterOf(sim, Ana);
        Assert.DoesNotContain(sim.Shops.All, e => e.Commission && e.Poster == me);
    }

    [Fact]
    public void NothingGoesUpWhenNoPlayerMakesIt()
    {
        var sim = Round("Blacksmith");
        ShortOfPlanks(sim);
        Commissions.Wants(sim);
        Assert.DoesNotContain(sim.Shops.All, e => e.Commission);
    }

    [Fact]
    public void ABotCarpenterMakesTheLotAndBringsIt()
    {
        var sim = Round();
        var me = Players.CharacterOf(sim, Ana);
        var tinker = ShortOfPlanks(sim);
        Commissions.Wants(sim);
        var order = sim.Shops.All.Single(e => e.IsOpen && e.Commission && e.Poster == tinker);
        var bot = new Bots.BotPlayer(() => sim, Ana, c => sim.ApplyNow(c), 7) { Temper = Bots.BotTemperament.Steady };
        var silver = Coin.Count(sim, me);
        var doing = new List<string>();
        for (var t = 0; t < SimClock.TicksPerDay && order.IsOpen; t++)
        {
            sim.Tick();
            bot.Tick();
            if (t % (SimClock.TicksPerHour / 12) == 0 && (doing.Count == 0 || !doing[^1].EndsWith(bot.Doing)))
                doing.Add($"{sim.Clock.HourOfDay:F2}h {bot.Doing}");
        }
        Assert.True(order.Status == OrderStatus.Done, $"{order.Status}, {order.Delivered} of {order.Count}\n" + string.Join("\n", doing.TakeLast(30)));
        Assert.Equal(me, order.Assignee);
        Assert.True(Coin.Count(sim, me) >= silver + order.Price);
    }
}
