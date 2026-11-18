using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Tinkering;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Carpentry;

/// <summary>
/// A party wagon's woodwork, the carpenter's (its wheel is the tinker's, <see cref="Wagons"/>): a new
/// axle hewn and its hubs driven on (<see cref="JoinSession"/>), a plank nailed across a sprung board
/// (<see cref="NailSession"/>).
/// </summary>
public static class WagonJoinery
{
    /// <summary>Why this person can't peg a plank across this wagon's sprung board now; null if they can.</summary>
    public static string? BoardProblem(Simulation sim, Entity worker, Entity wagon)
    {
        if (!Wagons.IsWagon(sim, wagon) || (sim.World.Get<Wagon>(wagon).Damage & WagonDamage.Board) == 0)
            return "its boards are sound";
        if (!AvatarSystem.InReach(sim, sim.World.Get<Position>(worker).Cell, wagon))
            return "you need to be at the wagon";
        if (!NailSession.HasHammer(sim, worker))
            return "you need a hammer to nail it";
        return PlanksOn(sim, worker) < Wagons.BoardPlanks ? $"it takes {Wagons.BoardPlanks} plank" : null;
    }

    private static bool IsPlank(Simulation sim, Entity e) => sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Name == Carpenters.PlanksDef;

    /// <summary>Planks someone has on them (hands and pack).</summary>
    public static int PlanksOn(Simulation sim, Entity who) =>
        Core.CraftCommandRules.Sources(sim, who).Where(e => IsPlank(sim, e) && Inventory.OnPerson(sim, who, e)).Sum(e => sim.World.Get<Stack>(e).Count);

    public static void StartAxle(Simulation sim, Entity worker, Entity wagon)
    {
        if (Wagons.AxleProblem(sim, worker, wagon) is { } why)
        {
            sim.Messages.Tell(worker, MessageKind.Info, $"Can't fit an axle: {why}.", sim.LocationOf(worker));
            return;
        }
        WorkSystem.Start(sim, worker, new JoinSession { Wagon = wagon, Work = JoinWork.Axle, Joints = Wagons.AxleEnds });
    }

    public static void StartBoard(Simulation sim, Entity worker, Entity wagon)
    {
        if (BoardProblem(sim, worker, wagon) is { } why)
        {
            sim.Messages.Tell(worker, MessageKind.Info, $"Can't mend the board: {why}.", sim.LocationOf(worker));
            return;
        }
        WorkSystem.Start(sim, worker, new NailSession { Wagon = wagon, Nails = Wagons.BoardPegs });
    }

    /// <summary>A plank's pegged across the sprung board, as well as it was pegged.</summary>
    public static void BoardPegged(Simulation sim, Entity worker, Entity wagon, float quality)
    {
        if (!Wagons.IsWagon(sim, wagon) || PlanksOn(sim, worker) < Wagons.BoardPlanks)
            return;
        var plank = Core.CraftCommandRules.Sources(sim, worker).First(e => IsPlank(sim, e) && Inventory.OnPerson(sim, worker, e));
        sim.DestroyThing(Inventory.TakeFromPerson(sim, worker, plank, Wagons.BoardPlanks));
        ref var wg = ref sim.World.Get<Wagon>(wagon);
        wg.Damage &= ~WagonDamage.Board;
        wg.JoinedBy = worker;
        var how = quality >= 0.75f ? "Tight as the day it was built." : quality >= 0.4f ? "It'll hold." : "It creaks, but it'll hold.";
        sim.Messages.Add(MessageKind.Good, $"{sim.World.Get<Creature>(worker).Name} nails a plank across the wagon's sprung board. {how}", sim.LocationOf(worker));
    }
}

/// <summary>Peg a plank across a party wagon's sprung board, from a plank you carry (<see cref="JoinSession"/>).</summary>
public sealed record PegBoardCommand(Entity Avatar, Entity Wagon) : ISimCommand, Core.IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (Core.InventoryCommand.Ready(sim, Avatar) && Wagons.IsWagon(sim, Wagon) && !sim.World.Has<Work.WorkSession>(Avatar))
            WagonJoinery.StartBoard(sim, Avatar, Wagon);
    }
}

/// <summary>Fit a new axle to a party wagon, hewn from a log you carry (<see cref="JoinSession"/>).</summary>
public sealed record FitAxleCommand(Entity Avatar, Entity Wagon) : ISimCommand, Core.IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (Core.InventoryCommand.Ready(sim, Avatar) && Wagons.IsWagon(sim, Wagon) && !sim.World.Has<Work.WorkSession>(Avatar))
            WagonJoinery.StartAxle(sim, Avatar, Wagon);
    }
}

