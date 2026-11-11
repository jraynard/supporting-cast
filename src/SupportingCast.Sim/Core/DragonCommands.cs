using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Core;

/// <summary>
/// The player tends their dragon, picked from its care window (docs/plans/dragon-interactions.md):
/// done at once, or worked while F is held (<see cref="DragonCareJob"/>). Hand-feeding uses what's in
/// hand; <paramref name="Trick"/> picks the lesson for Train (else the next one).
/// </summary>
public sealed record DragonCareCommand(Entity Avatar, Entity Dragon, DragonInteractionDef Interaction, TrickDef? Trick = null)
    : ISimCommand, IActorCommand, ISubjectCommand
{
    public Entity Actor => Avatar;
    public Entity Subject => Dragon;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!InventoryCommand.Ready(sim, Avatar) || !w.IsAlive(Dragon) || !w.Has<Dragon>(Dragon))
            return;
        if (!AvatarSystem.InReach(w.Get<Position>(Avatar).Cell, sim.LocationOf(Dragon)) && Interaction.Ticks == 0)
            return;
        ref var carrying = ref w.Get<Carrying>(Avatar);
        var held = carrying.Item;
        var food = !held.IsNull && w.IsAlive(held) ? w.Get<Thing>(held).Def : null;
        if (Bonding.Problem(sim, Avatar, Dragon, Interaction, food) is { } why)
        {
            sim.Messages.Add(MessageKind.Info, $"Can't {Interaction.Label}: {why}.", sim.LocationOf(Avatar));
            return;
        }
        if (Trick != null && Bonding.Peek(sim, Dragon).Knows(Trick))
            return;
        if (Interaction.Kind == CareKind.Feed)
            carrying.Item = Bonding.HandFeed(sim, Avatar, Dragon, held);
        else if (Interaction.Ticks == 0)
            Bonding.Interact(sim, Avatar, Dragon, Interaction);
        else
            AiSystem.Order(sim, Avatar, new DragonCareJob(Dragon, Interaction, Trick));
    }
}

/// <summary>Whistle for your dragon (the toolbar's Call): it comes if it trusts you enough, and always once it knows "come".</summary>
public sealed record CallDragonCommand(Entity Keeper, Entity Dragon) : ISimCommand, IActorCommand, ISubjectCommand
{
    public Entity Actor => Keeper;
    public Entity Subject => Dragon;

    public void Apply(Simulation sim)
    {
        if (sim.World.IsAlive(Keeper) && sim.World.IsAlive(Dragon) && sim.World.Has<Dragon>(Dragon))
            Bonding.Call(sim, Keeper, Dragon);
    }
}

/// <summary>Tell your dragon to stay where it is (it must know "stay"); calling it ends the wait.</summary>
public sealed record DragonStayCommand(Entity Keeper, Entity Dragon) : ISimCommand, IActorCommand, ISubjectCommand
{
    public Entity Actor => Keeper;
    public Entity Subject => Dragon;

    public void Apply(Simulation sim)
    {
        if (sim.World.IsAlive(Keeper) && sim.World.IsAlive(Dragon) && sim.World.Has<Dragon>(Dragon))
            Bonding.Stay(sim, Keeper, Dragon);
    }
}
