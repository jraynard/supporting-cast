using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Taking an order with the work key (<see cref="AvatarActionKind.TakeOrder"/>): beside a customer
/// waiting at your shop, F says you'll see to it, and their thing changes hands. A Steam Deck
/// playtester couldn't find the shop panel's button, and a pad can't press it.
/// </summary>
public class TakeOrderActionTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>A smith's practice round with a repair asked, and the smith stood in handing reach of the customer.</summary>
    private static (Simulation Sim, Entity Me, Order Order) SmithBesideACustomer()
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
        var at = sim.World.Get<Position>(order.Poster).Cell;
        var beside = sim.SpiralFrom(at, 2).First(c => c != at && c.Z == at.Z && sim.Map.Passable(c) && Map.Distance(c, at) <= Handing.Reach);
        sim.World.Get<Position>(me).Cell = beside;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(beside);
        if (!order.KnownBy.Contains(me))
            order.KnownBy.Add(me); // heard it asked, whether or not the smith was at the forge just then
        return (sim, me, order);
    }

    [Fact]
    public void TheWorkKeyBesideAWaitingCustomerTakesTheirOrder()
    {
        var (sim, me, order) = SmithBesideACustomer();
        var cell = sim.World.Get<Position>(order.Poster).Cell;

        var action = AvatarSystem.ActionAt(sim, me, cell);
        Assert.Equal(AvatarActionKind.TakeOrder, action.Kind);
        Assert.Contains(order.PosterName, action.Label);

        sim.ApplyNow(new AvatarWorkCommand(me, true, cell));
        Assert.Equal(me, order.Assignee);

        // Taken, it's no longer offered: the next F is whatever's there besides.
        Assert.NotEqual(AvatarActionKind.TakeOrder, AvatarSystem.ActionAt(sim, me, cell).Kind);

        // And in reach, their blade comes over (the shop's next look round, before the test's unsteered smith wanders off).
        Assert.True(sim.Shops.TryHandOver(sim, order));
        Assert.True(Inventory.OnPerson(sim, me, order.Subject));
    }

    [Fact]
    public void NotOfferedOutOfReachOrForOrdersNeverHeard()
    {
        var (sim, me, order) = SmithBesideACustomer();
        var cell = sim.World.Get<Position>(order.Poster).Cell;

        order.KnownBy.RemoveAll(e => e == me);
        Assert.NotEqual(AvatarActionKind.TakeOrder, AvatarSystem.ActionAt(sim, me, cell).Kind);
        order.KnownBy.Add(me);

        var far = sim.SpiralFrom(cell, 14).Last(c => c.Z == cell.Z && sim.Map.Passable(c));
        sim.World.Get<Position>(me).Cell = far;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(far);
        Assert.Null(ShopSystem.TakeableAt(sim, me, cell));
        sim.ApplyNow(new AvatarWorkCommand(me, true, cell));
        Assert.True(order.Assignee.IsNull);
    }
}
