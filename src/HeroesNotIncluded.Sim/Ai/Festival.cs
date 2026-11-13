using HeroesNotIncluded.Sim.Round;

namespace HeroesNotIncluded.Sim.Ai;

/// <summary>
/// The season's last evening (<see cref="RoundPhase.Festival"/>): the town and whoever's visiting
/// leave off work and gather in the square, milling about the well until the reckoning.
/// </summary>
public static class Festival
{
    /// <summary>How far from the well the crowd spreads.</summary>
    public const int Spread = 6;

    /// <summary>Off to the square and about it, if the festival's on; null otherwise.</summary>
    public static Job? Gathering(Simulation sim, Entity pawn)
    {
        if (sim.Round.Phase != RoundPhase.Festival || sim.Round.Square is not { } square)
            return null;
        return AiQueries.RandomReachableNear(sim, pawn, square, Spread) is { } spot
            ? new WanderJob(spot, sim.Rand.Range(240, 720))
            : null;
    }
}
