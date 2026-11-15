using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Ai;

/// <summary>
/// A player's hands come before a villager's errand. A stack an AI villager has only claimed to
/// fetch (to haul it to store, or to feed a fire with) is the player's to take, and the villager
/// gives the errand up. Players found the barn's wood always spoken for by someone walking to it,
/// and the take refused without a word.
/// </summary>
public static class Claims
{
    /// <summary>Whether <paramref name="avatar"/> may take <paramref name="item"/>: nobody's, theirs, or only claimed for a fetch.</summary>
    public static bool Free(Simulation sim, Entity item, Entity avatar)
    {
        var by = sim.Reservations.ReservedBy(item);
        return by.IsNull || by == avatar || Yields(sim, by);
    }

    /// <summary>Clear another's fetching claim on <paramref name="item"/> before a player takes it (see <see cref="Free"/>).</summary>
    public static void TakeOver(Simulation sim, Entity item, Entity avatar)
    {
        var by = sim.Reservations.ReservedBy(item);
        if (by.IsNull || by == avatar || !Yields(sim, by))
            return;
        var mind = sim.World.Get<Mind>(by);
        // Already on its way with what it took from the stack (a fire's load, one stack hauled):
        // only the claim on what's left goes. A big hauler still gathering puts its load down.
        if (mind.Job is not BulkHaulJob && sim.World.Get<Carrying>(by).HasItem)
            sim.Reservations.Release(item);
        else
            AiSystem.EndJob(sim, by, mind, false);
    }

    private static bool Yields(Simulation sim, Entity by) =>
        !sim.World.Has<Avatar>(by) && sim.World.TryGet<Mind>(by, out var mind) && mind.Job is HaulJob or BulkHaulJob or RefuelJob;
}
