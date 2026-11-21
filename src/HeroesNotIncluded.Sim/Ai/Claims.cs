using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Ai;

/// <summary>
/// A player's hands come before a villager's errand. A villager claims what it's going for when it
/// sets out, but until it gets there the thing is anyone's: a player who reaches it first takes it, and
/// the villager gives that errand up (and finds another bucket, another stack). Players found the barn's
/// wood, and the bucket by the well, always spoken for by someone still walking to it, and the take
/// refused without a word. One only fetching (to haul it to store, or to feed a fire) lets it go even there.
/// </summary>
public static class Claims
{
    /// <summary>Whether <paramref name="avatar"/> may take <paramref name="item"/>: nobody's, theirs, or claimed by a villager not yet at it (or only fetching it).</summary>
    public static bool Free(Simulation sim, Entity item, Entity avatar)
    {
        var by = sim.Reservations.ReservedBy(item);
        return by.IsNull || by == avatar || Yields(sim, by, item);
    }

    /// <summary>Clear another's fetching claim on <paramref name="item"/> before a player takes it (see <see cref="Free"/>).</summary>
    public static void TakeOver(Simulation sim, Entity item, Entity avatar)
    {
        var by = sim.Reservations.ReservedBy(item);
        if (by.IsNull || by == avatar || !Yields(sim, by, item))
            return;
        var mind = sim.World.Get<Mind>(by);
        // Already on its way with what it took from the stack (a fire's load, one stack hauled):
        // only the claim on what's left goes. A big hauler still gathering puts its load down.
        if (mind.Job is not BulkHaulJob && sim.World.Get<Carrying>(by).HasItem)
            sim.Reservations.Release(item);
        else
            AiSystem.EndJob(sim, by, mind, false);
    }

    /// <summary>
    /// Does a villager's claim give way to a player: it's the AI's (a player's or a bot's claim holds), and it's
    /// only fetching the thing, or hasn't got to it yet (it's working at it once it's beside it: a log at the block).
    /// </summary>
    private static bool Yields(Simulation sim, Entity by, Entity item)
    {
        var w = sim.World;
        if (w.Has<Avatar>(by) || !w.TryGet<Mind>(by, out var mind) || mind.Job == null)
            return false;
        if (mind.Job is HaulJob or BulkHaulJob or RefuelJob)
            return true;
        var at = w.Get<Position>(by).Cell;
        var thing = sim.LocationOf(item);
        return at != thing && !Pathfinder.Touches(at, thing);
    }
}
