using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Shops;

// What players do with orders and coin. All act as the player's own character, and do nothing
// if the order has gone stale (it closed, the other person walked off).

/// <summary>How close two people must be to hand something over.</summary>
public static class Handing
{
    public const int Reach = 2;

    /// <summary>Can <paramref name="from"/> hand something to <paramref name="to"/> right now?</summary>
    public static bool InReach(Simulation sim, Entity from, Entity to)
    {
        var w = sim.World;
        if (from == to || !w.IsAlive(to) || !w.TryGet<Creature>(to, out var c) || c.Dead || !w.Has<Carrying>(to)
            || !w.TryGet<Position>(from, out var a) || !w.TryGet<Position>(to, out var b))
            return false;
        return a.Cell.Z == b.Cell.Z && Map.Distance(a.Cell, b.Cell) <= Reach;
    }
}

/// <summary>
/// Hand <paramref name="Count"/> of a stack you have in hand or pack to someone in reach: coin to
/// pay them, food to the hungry adventurer who ordered it (which serves the order).
/// </summary>
public sealed record GiveCommand(Entity Avatar, Entity To, Entity Item, int Count) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (!InventoryCommand.Ready(sim, Avatar) || !InventoryCommand.IsItem(sim, Item) || Count <= 0
            || !Systems.Inventory.OnPerson(sim, Avatar, Item) || !Handing.InReach(sim, Avatar, To))
            return;
        var w = sim.World;
        // Only what's loose in hand or pack: gear has to come off first.
        if (w.Get<Carrying>(Avatar).Item != Item && !Systems.Inventory.InOwnPack(sim, Avatar, Item))
            return;
        var taken = Systems.Inventory.TakeFromPerson(sim, Avatar, Item, Math.Min(Count, w.Get<Stack>(Item).Count));
        sim.Shops.Received(sim, Avatar, To, taken);
    }
}

/// <summary>
/// Go to the customer of an order you're seeing to, wherever they've got to: they hand their thing
/// over when you're in reach, or, if you have it, you hand it back (<see cref="SeeToCustomerJob"/>).
/// Walking off (any movement key) calls it off.
/// </summary>
public sealed record SeeToCustomerCommand(Entity Avatar, int Order) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (!InventoryCommand.Ready(sim, Avatar) || sim.Shops.Find(Order) is not { IsOpen: true } order || order.Assignee != Avatar
            || !ShopSystem.Delivers(order.Kind.Kind) || !sim.World.IsAlive(order.Poster) || sim.World.Has<Carried>(Avatar))
            return;
        AiSystem.Order(sim, Avatar, new SeeToCustomerJob(Order));
    }
}

/// <summary>Say you'll see to an order you know of: a mend, a strongbox, a pour.</summary>
public sealed record TakeOrderCommand(Entity Avatar, int Order) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar) && SpeechSystem.CanAct(sim, Avatar))
            sim.Shops.Accept(sim, Avatar, Order);
    }
}

/// <summary>"Sorry, we're out": turn away an order you can't fill (<see cref="ShopSystem.TurnAway"/>).</summary>
public sealed record TurnAwayCommand(Entity Avatar, int Order) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar) && SpeechSystem.CanAct(sim, Avatar))
            sim.Shops.TurnAway(sim, Avatar, Order);
    }
}

/// <summary>
/// Change your shop's sign: what you charge for a service, and whether you're offering it. Only
/// the keeper (whose trade works from that kind of building) can.
/// </summary>
public sealed record SetPriceCommand(Entity Avatar, Entity Shop, Defs.ServiceKind Service, int Price, bool Offered) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar))
            ShopPrices.Set(sim, Avatar, Shop, Service, Price, Offered);
    }
}

/// <summary>
/// Put a piece on your shop's wares shelf up for sale at a price (or change it), or take it off sale.
/// Adventurers who'd be better off with it, and think it worth the price, line up to buy it.
/// </summary>
public sealed record SetItemPriceCommand(Entity Avatar, Entity Item, int Price, bool ForSale) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar))
            Goods.Set(sim, Avatar, Item, Price, ForSale);
    }
}

/// <summary>
/// Put a piece of your own, in hand or in your pack, on your shop's wares shelf in reach: it's for
/// sale at once (<see cref="Goods.Shelved"/>). The inventory's "Put on the wares shelf".
/// </summary>
public sealed record ShelveCommand(Entity Avatar, Entity Item) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!InventoryCommand.Ready(sim, Avatar) || !InventoryCommand.IsItem(sim, Item) || !Goods.IsGear(w.Get<Thing>(Item).Def)
            || Goods.MyDisplay(sim, Avatar) is not { IsNull: false } shelf || !Inventory.CanReachContainer(sim, Avatar, shelf))
            return;
        if (Inventory.InOwnPack(sim, Avatar, Item))
        {
            Inventory.StoreFromPack(sim, Avatar, shelf, Item);
            return;
        }
        ref var hands = ref w.Get<Carrying>(Avatar);
        if (hands.Item != Item || sim.ContainerRoom(shelf, w.Get<Thing>(Item).Def, w.Get<Thing>(Item).Material) < w.Get<Stack>(Item).Count)
            return;
        hands.Item = Entity.Null;
        if (!sim.Store(shelf, Item).IsNull)
            throw new InvalidOperationException("room was counted, so it must fit");
        if (w.IsAlive(Item))
            Goods.Shelved(sim, Avatar, Item);
    }
}

/// <summary>Pin a commission to the notice board: so many of a thing, for a price (<see cref="Commissions"/>). You have to be at the board.</summary>
public sealed record PostCommissionCommand(Entity Avatar, Defs.ThingDef Wants, int Count, int Price, int Hours) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar))
            Commissions.Post(sim, Avatar, Wants, Count, Price, Hours);
    }
}

/// <summary>Read the notice board you're standing at: every commission up on it is known to you.</summary>
public sealed record ReadBoardCommand(Entity Avatar) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar))
            Commissions.Read(sim, Avatar);
    }
}

/// <summary>Take a commission of yours down off the board.</summary>
public sealed record CancelCommissionCommand(Entity Avatar, int Order) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar))
            Commissions.Cancel(sim, Avatar, Order);
    }
}
