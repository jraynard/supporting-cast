using DragonWorld.Sim.Components;

namespace DragonWorld.Sim.Scenarios;

/// <summary>
/// Leaving a map on dragonback: once an opening's story lets you go (<see cref="QuestStage.TakeWing"/>),
/// the player mounts their own dragon and flies off. The game then packs the party
/// (<see cref="Travel.Pack"/>, no wagon) and sets it down on a fresh map, as the prologue's road does.
/// </summary>
public static class Departure
{
    /// <summary>Does the story let the avatar leave this map by air (whether or not they're ready)?</summary>
    public static bool StoryAllows(Simulation sim, Entity avatar) =>
        sim.World.IsAlive(avatar) && sim.World.TryGet<Quest>(avatar, out var q) && q.Stage == QuestStage.TakeWing;

    /// <summary>Why the avatar can't fly off right now, or null if they can.</summary>
    public static string? CantFlyOff(Simulation sim, Entity avatar)
    {
        var w = sim.World;
        if (!StoryAllows(sim, avatar))
            return "It isn't time to leave yet.";
        if (w.Get<Creature>(avatar) is { Able: false })
            return "You're in no state to travel.";
        // Escape is a dash for it: off the ground mid-fight, leaping onto its back from beside it.
        var dash = sim.Story.Start == StartKind.Escape;
        if (sim.Combat.Active && !dash)
            return "Not in the middle of a fight.";
        var dragon = Companions.DragonOf(sim, avatar);
        if (dragon.IsNull)
            return "You have no dragon to fly with.";
        var name = w.Get<Creature>(dragon).Name;
        if (w.Has<Chained>(dragon))
            return $"{name} is still in chains.";
        if (!w.Get<Creature>(dragon).Able)
            return $"{name} can't get up.";
        var riding = w.TryGet<Riding>(avatar, out var r) && r.Mount == dragon;
        if (!riding && !(dash && Ai.Pathfinder.Touches(w.Get<Position>(avatar).Cell, w.Get<Position>(dragon).Cell)))
            return dash ? $"get beside {name}" : $"Mount {name} first.";
        if (Ai.Navigation.FlyingStage(sim, dragon) == null)
            return $"{name} can't fly that far right now.";
        return null;
    }
}
