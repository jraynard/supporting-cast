using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Systems;

/// <summary>
/// The event director: after a grace period it sends raids from the map edge every few days,
/// sized to how strong the village has grown. Off by default (<see cref="Difficulty"/> null), so
/// tests, the bench and the demos stay scripted; the game picks a difficulty when an Adventure starts.
/// Its decisions use a random stream of its own, so switching it on doesn't reshuffle the rest of
/// the sim until a raid actually arrives.
/// </summary>
public sealed class Storyteller(ulong seed) : ISimSystem, Saving.ISaveState
{
    public string Name => "Storyteller";
    public int Interval => SimClock.TicksPerHour;

    private readonly SimRandom _rand = new(seed ^ 0x5707_7E11_E5u);

    /// <summary>Null: off, nothing ever happens.</summary>
    public DifficultyDef? Difficulty { get; private set; }

    /// <summary>When the next raid is due; -1 if none is planned.</summary>
    public long NextRaidTick { get; private set; } = -1;

    public int RaidsSent { get; private set; }

    /// <summary>When the next wild event (<see cref="WildEventKind"/>) is due; -1 if none is planned.</summary>
    public long NextEventTick { get; private set; } = -1;

    public int EventsSent { get; private set; }

    public void Save(Saving.SaveWriter w, Simulation sim)
    {
        var (a, b, c, d) = _rand.State;
        w.UInt(a); w.UInt(b); w.UInt(c); w.UInt(d);
        w.Def(Difficulty);
        w.Long(NextRaidTick);
        w.Int(RaidsSent);
        w.Long(NextEventTick);
        w.Int(EventsSent);
    }

    public void Load(Saving.SaveReader r, Simulation sim)
    {
        _rand.State = (r.UInt(), r.UInt(), r.UInt(), r.UInt());
        Difficulty = r.DefOrNull<DifficultyDef>();
        NextRaidTick = r.Long();
        RaidsSent = r.Int();
        NextEventTick = r.Long();
        EventsSent = r.Int();
    }

    public void SetDifficulty(Simulation sim, DifficultyDef? difficulty)
    {
        Difficulty = difficulty;
        NextRaidTick = difficulty is { Peaceful: false }
            ? sim.Clock.Tick + SimClock.DaysToTicks(difficulty.GraceDays * _rand.Range(0.85f, 1.15f))
            : -1;
        // No draw from the stream here, so the raids keep the timing they always had.
        NextEventTick = difficulty != null ? sim.Clock.Tick + SimClock.DaysToTicks(difficulty.EventsMeanDays * 0.9f) : -1;
    }

    public void Update(Simulation sim)
    {
        // A round's crises are the Director's (Director/Director.cs).
        if (Difficulty == null || sim.Round.Phase != Round.RoundPhase.None)
            return;
        // Not while a fight is on or the last raiders are still about: try again in a couple of hours.
        var busy = sim.Creatures().Any(e => Factions.Attacker(sim.World.Get<Creature>(e).Faction));
        if (NextRaidTick >= 0 && sim.Clock.Tick >= NextRaidTick)
        {
            if (busy)
                NextRaidTick = sim.Clock.Tick + SimClock.HoursToTicks(2);
            else
            {
                SendRaid(sim);
                busy = true;
                NextRaidTick = sim.Clock.Tick + SimClock.DaysToTicks(Difficulty.MeanDaysBetween * _rand.Range(0.6f, 1.4f));
            }
        }
        if (NextEventTick >= 0 && sim.Clock.Tick >= NextEventTick)
        {
            if (busy)
                NextEventTick = sim.Clock.Tick + SimClock.HoursToTicks(3);
            else
            {
                SendWildEvent(sim, PickEvent(sim));
                NextEventTick = sim.Clock.Tick + SimClock.DaysToTicks(Difficulty.EventsMeanDays * _rand.Range(0.6f, 1.4f));
            }
        }
    }

    /// <summary>
    /// What happens next: predators (not on Peaceful, not in the grace days, if any kind is affordable)
    /// or a migration.
    /// </summary>
    public WildEventKind PickEvent(Simulation sim)
    {
        var predators = Difficulty is { Peaceful: false } d && sim.Clock.Day > d.GraceDays
            && WildEvents.PredatorKinds(sim, PredatorPoints(sim)).Count > 0 ? 3f : 0f;
        const float migration = 2f;
        return _rand.Range(0f, predators + migration) < predators ? WildEventKind.Predators : WildEventKind.Migration;
    }

    /// <summary>A pack is bought with part of the raid points: animals come in smaller numbers than bandits.</summary>
    public float PredatorPoints(Simulation sim) => RaidPoints(sim) * 0.6f;

    /// <summary>Send a wild event now (also the debug panel's). Returns whether anything came.</summary>
    public bool SendWildEvent(Simulation sim, WildEventKind kind)
    {
        var sent = kind switch
        {
            WildEventKind.Predators => WildEvents.SendPredators(sim, _rand, PredatorPoints(sim)).Count > 0,
            WildEventKind.Migration => WildEvents.SendMigration(sim, _rand).Count > 0,
            _ => false,
        };
        if (sent)
            EventsSent++;
        return sent;
    }

    /// <summary>How big a raid the village has earned (<see cref="Raids.Points"/>), at this difficulty.</summary>
    public float RaidPoints(Simulation sim) => Raids.Points(sim) * (Difficulty?.PointsFactor ?? 1f);

    /// <summary>Pick raiders worth up to <paramref name="points"/> (<see cref="Raids.Choose"/>).</summary>
    public List<RaiderKindDef> Choose(Simulation sim, float points, int maxRaiders) => Raids.Choose(sim, _rand, points, maxRaiders);

    /// <summary>Send a raid now (also the debug panel's "Raid now"). Returns the raiders, none if there was nowhere to come from.</summary>
    public List<Entity> SendRaid(Simulation sim)
    {
        var raiders = Raids.Send(sim, _rand, RaidPoints(sim), Difficulty?.MaxRaiders ?? 5);
        if (raiders.Count > 0)
            RaidsSent++;
        return raiders;
    }
}
