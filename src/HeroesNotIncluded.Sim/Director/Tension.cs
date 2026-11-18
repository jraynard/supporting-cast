using HeroesNotIncluded.Sim.Afterlife;
using HeroesNotIncluded.Sim.Components;

namespace HeroesNotIncluded.Sim.Director;

/// <summary>
/// The Director's reading of how the town is doing, as a game master glances round the table
/// (docs/DIRECTOR-PLAN.md). Read from the sim's own state, never from <c>Perception</c> or
/// <c>Map.Light</c>, so it's the same on every peer. Nobody in town sees it.
/// </summary>
/// <param name="Harm">How much is going wrong right now, 0 (nothing) to 1: the hurt and the recently dead, fire, breaches, crises under way.</param>
/// <param name="Ahead">Renown against where a town on course would be by now, as a share of the target: below 0 behind, above ahead (-1..1).</param>
/// <param name="Dread">Dread as a share of its limit (0..1).</param>
public readonly record struct Tension(float Harm, float Ahead, float Dread)
{
    /// <summary>Game hours a death weighs on the town.</summary>
    public const float DeathHours = 6;

    /// <summary>
    /// What to do about it, -1 (ease off: later, gentler) to 1 (push: sooner, harder). A town on
    /// course with nothing wrong is 0, and the season goes as planned. Dread only counts past a
    /// quarter of its limit: a little is the season doing its job.
    /// </summary>
    public float Push => Math.Clamp(2f * Ahead - 2f * Math.Max(0f, Dread - 0.25f) - Harm, -1f, 1f);

    public static Tension Read(Simulation sim, int activeCrises)
    {
        var w = sim.World;
        var now = sim.Clock.Tick;
        var downed = 0;
        foreach (var e in sim.Creatures())
            if (w.Get<Creature>(e) is { Downed: true } c && (c.Faction == Faction.Village || w.Has<Adventurer>(e)))
                downed++;
        var dead = 0;
        var recent = SimClock.HoursToTicks(DeathHours);
        foreach (var e in w.Query<Death>())
            if (now - w.Get<Death>(e).Tick < recent && w.Get<Creature>(e) is var c && (c.Faction == Faction.Village || w.Has<Adventurer>(e)))
                dead++;
        var harm = 0.15f * downed + 0.2f * dead + sim.Fire.BurningTiles.Count / 30f + 0.15f * sim.Repairs.Breaches.Count + 0.4f * activeCrises;

        var season = sim.Defs.Season;
        var target = Math.Max(1f, Reckoning.RenownTarget(sim));
        var start = sim.Round.StartTick;
        var festival = sim.Round.FestivalTick(sim);
        var progress = start < 0 || festival <= start ? 0f : Math.Clamp((float)(now - start) / (festival - start), 0f, 1f);
        var expected = target * season.Pacing.RenownAtFestival * progress;
        var ahead = Math.Clamp((sim.Reckoning.Renown - expected) / target, -1f, 1f);

        var dread = Math.Clamp(sim.Reckoning.Dread / Math.Max(1f, Reckoning.DreadLimit(sim)), 0f, 1f);
        return new Tension(Math.Clamp(harm, 0f, 1f), ahead, dread);
    }

    public override string ToString() => $"harm {Harm:0.00}, ahead {Ahead:+0.00;-0.00}, dread {Dread:0.00}: push {Push:+0.00;-0.00}";
}
