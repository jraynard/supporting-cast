using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Speech;

namespace SupportingCast.Sim.Errands;

// What players do with errands and coin. All act as the player's own character, and do nothing
// if the request has gone stale (the errand closed, the other person walked off).

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
        sim.Errands.Received(sim, Avatar, To, taken);
    }
}

/// <summary>Ask for something in your own words, for a price, said aloud where you stand.</summary>
public sealed record PostErrandCommand(Entity Avatar, string Text, int Price, int Hours) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar) && SpeechSystem.CanAct(sim, Avatar))
            sim.Errands.PostRequest(sim, Avatar, Text, Price, Hours);
    }
}

/// <summary>Say you'll see to an errand you know of.</summary>
public sealed record AcceptErrandCommand(Entity Avatar, int Errand) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar) && SpeechSystem.CanAct(sim, Avatar))
            sim.Errands.Accept(sim, Avatar, Errand);
    }
}

/// <summary>Your request is done: pay whoever took it on, if they're in reach.</summary>
public sealed record CompleteErrandCommand(Entity Avatar, int Errand) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (!InventoryCommand.Ready(sim, Avatar) || sim.Errands.Find(Errand) is not { IsOpen: true } e
            || e.Poster != Avatar || e.Kind.Kind != Defs.ErrandKind.Request || !Handing.InReach(sim, Avatar, e.Assignee))
            return;
        sim.Errands.Complete(sim, e, e.Assignee);
    }
}

/// <summary>Call off a request of yours.</summary>
public sealed record CancelErrandCommand(Entity Avatar, int Errand) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar))
            sim.Errands.Cancel(sim, Avatar, Errand);
    }
}
