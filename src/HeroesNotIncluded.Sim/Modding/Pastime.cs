using HeroesNotIncluded.Sim.Ai;

namespace HeroesNotIncluded.Sim.Modding;

/// <summary>
/// Something the town does with its free time that a mod brings (the musician's audience: listening, dancing):
/// a door into the brains, so a trade's leisure lives in that trade's code. Villagers ask once their work's done
/// (never in place of it), adventurers before they lounge about their haunt. Its job should be
/// <see cref="Job.Interruptible"/>, so work, danger or sleep take them away from it.
/// </summary>
public interface IPastime
{
    /// <summary>A pastime for <paramref name="pawn"/> now, or null. Called often: cheap when there's nothing to do, and no <c>sim.Rand</c> then.</summary>
    Job? JobFor(Simulation sim, Entity pawn);
}

/// <summary>The pastimes registered (<see cref="SimRegistry.Pastime"/>), asked in order.</summary>
public static class Pastimes
{
    /// <summary>The first registered pastime's job for <paramref name="pawn"/>, or null (always, with none registered).</summary>
    public static Job? JobFor(Simulation sim, Entity pawn)
    {
        foreach (var p in sim.Pastimes)
            if (p.JobFor(sim, pawn) is { } job)
                return job;
        return null;
    }
}
