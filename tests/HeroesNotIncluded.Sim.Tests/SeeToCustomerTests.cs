using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Finding the customer you owe a visit: where they are through <see cref="PerceptionView.Awaiting"/>,
/// and a walk to them (<see cref="SeeToCustomerCommand"/>) that collects their thing or hands it back.
/// </summary>
public class SeeToCustomerTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private static (Simulation Sim, Entity Me, Order Order, Entity Sword) TookARepair()
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade("Blacksmith")], []));
        sim.ApplyNow(new StartRoundCommand());
        sim.Round.Practice = true; // nobody else rides in
        var me = Players.CharacterOf(sim, Ana);
        sim.ApplyNow(new PracticeCommand(me, PracticeAction.SendCustomers, (int)CustomerKind.Dull));
        Order? order = null;
        for (var t = 0; t < 12 * 12 && order == null; t++)
        {
            sim.TickMany(SimClock.TicksPerHour / 12);
            order = sim.Shops.All.FirstOrDefault(o => o.IsOpen && o.Kind.Kind == ServiceKind.Repair);
        }
        Assert.NotNull(order);
        sim.ApplyNow(new TakeOrderCommand(me, order.Id));
        return (sim, me, order, order.Subject);
    }

    /// <summary>Somewhere a good way off from the customer, on their level.</summary>
    private static void WalkAway(Simulation sim, Entity me, Entity from)
    {
        var at = sim.World.Get<Position>(from).Cell;
        var cell = sim.SpiralFrom(at, 14).Last(c => c.Z == at.Z && sim.Map.Passable(c) && Ai.Navigation.PlanRoute(sim, me, c, at, PathEnd.Touch) != null);
        sim.World.Get<Position>(me).Cell = cell;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(cell);
    }

    private static void TickUntil(Simulation sim, Func<bool> done, int maxTicks = SimClock.TicksPerHour)
    {
        for (var t = 0; t < maxTicks && !done(); t++)
            sim.Tick();
    }

    [Fact]
    public void TheWalkCollectsTheBladeThenTakesItBackAndIsPaid()
    {
        var (sim, me, order, sword) = TookARepair();
        WalkAway(sim, me, order.Poster);
        Assert.False(Handing.InReach(sim, me, order.Poster));

        // Where they are, to the one who took it on: to collect.
        var errand = Perception.For(sim, me).Awaiting().Single();
        Assert.Equal(order.Poster, errand.Who);
        Assert.False(errand.Returning);
        Assert.Equal(sim.LocationOf(order.Poster), errand.At);

        sim.ApplyNow(new SeeToCustomerCommand(me, order.Id));
        TickUntil(sim, () => Inventory.OnPerson(sim, me, sword));
        Assert.True(Inventory.OnPerson(sim, me, sword), $"{sim.World.Get<Mind>(me).Job?.Report} / {sim.Ai.LastFailure.GetValueOrDefault(me)}");
        errand = Perception.For(sim, me).Awaiting().Single();
        Assert.True(errand.Returning);
        Assert.False(errand.Ready, "still dull");

        // Seen to, and from across town again: the walk back hands it over, and they pay.
        WeaponWear.SetDullness(sim, sword, 0f);
        Assert.True(Perception.For(sim, me).Awaiting().Single().Ready);
        WalkAway(sim, me, order.Poster);
        var purse = Coin.Count(sim, me);
        sim.ApplyNow(new SeeToCustomerCommand(me, order.Id));
        TickUntil(sim, () => !order.IsOpen);
        Assert.Equal(OrderStatus.Done, order.Status);
        Assert.Equal(purse + order.Price, Coin.Count(sim, me));
        Assert.Equal(sword, sim.World.Get<Equipment>(order.Poster).Weapon);
        Assert.Empty(Perception.For(sim, me).Awaiting());
    }

    [Fact]
    public void OnlyTheOneSeeingToItIsSentOrToldWhere()
    {
        var (sim, me, order, _) = TookARepair();
        var other = sim.Creatures().First(e => e != me && sim.World.Has<Trade>(e) && !sim.World.Has<Adventurer>(e));
        Assert.Empty(Perception.For(sim, other).Awaiting());

        sim.World.Add(other, new Avatar());
        sim.ApplyNow(new SeeToCustomerCommand(other, order.Id));
        Assert.Null(sim.World.Get<Mind>(other).Job as SeeToCustomerJob);
        sim.ApplyNow(new SeeToCustomerCommand(me, order.Id + 1000));
        Assert.Null(sim.World.Get<Mind>(me).Job as SeeToCustomerJob);
    }
}
