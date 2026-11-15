namespace HeroesNotIncluded.Sim.Round;

/// <summary>
/// What kind of round the host chose in the lobby. It's sim state, set by a command and saved, so
/// every peer plays by the same rules: friendly fire is settled where a blow lands
/// (<see cref="Combat.Reach.Harms"/>), never filtered on one client.
/// </summary>
/// <param name="FriendlyFire">The town's blows cut the town's own people (players and townsfolk alike). Off: they pass through.</param>
/// <param name="Traitors">Some players are secretly against the town. Always off until Whispers (M4) brings them.</param>
public readonly record struct RoundOptions(bool FriendlyFire, bool Traitors)
{
    /// <summary>A new round: nobody's blade hurts a friend, and no traitors.</summary>
    public static RoundOptions Default => new(false, false);
}
