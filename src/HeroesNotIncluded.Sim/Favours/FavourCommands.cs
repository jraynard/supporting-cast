using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Speech;

namespace HeroesNotIncluded.Sim.Favours;

// What players do with each other's favours. All act as the player's own character, and do
// nothing if the favour has gone stale (closed, or the other person walked off).

/// <summary>Pin a favour to the notice board in your own words, for a price: you have to be at the board.</summary>
public sealed record PostFavourCommand(Entity Avatar, string Text, int Price, int Hours) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar) && SpeechSystem.CanAct(sim, Avatar))
            sim.Favours.Post(sim, Avatar, Text, Price, Hours);
    }
}

/// <summary>Pin an errand to the board: so many of a thing, brought to you, for a price.</summary>
public sealed record PostErrandCommand(Entity Avatar, Defs.ThingDef Wants, int Count, int Price, int Hours) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar))
            sim.Favours.PostErrand(sim, Avatar, Wants, Count, Price, Hours);
    }
}

/// <summary>Read the notice board you're standing at: every open favour on it is known to you.</summary>
public sealed record ReadBoardCommand(Entity Avatar) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar))
            sim.Favours.Read(sim, Avatar);
    }
}

/// <summary>Hand over an errand's goods to whoever asked, and be paid.</summary>
public sealed record DeliverFavourCommand(Entity Avatar, int Favour) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar))
            sim.Favours.Deliver(sim, Avatar, Favour);
    }
}

/// <summary>Say you'll see to a favour you know of.</summary>
public sealed record AcceptFavourCommand(Entity Avatar, int Favour) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar) && SpeechSystem.CanAct(sim, Avatar))
            sim.Favours.Accept(sim, Avatar, Favour);
    }
}

/// <summary>Your favour is done: pay whoever took it on, if they're in reach.</summary>
public sealed record CompleteFavourCommand(Entity Avatar, int Favour) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar))
            sim.Favours.Complete(sim, Avatar, Favour);
    }
}

/// <summary>Call off a favour of yours.</summary>
public sealed record CancelFavourCommand(Entity Avatar, int Favour) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar))
            sim.Favours.Cancel(sim, Avatar, Favour);
    }
}
