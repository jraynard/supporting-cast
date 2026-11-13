using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Speech;

namespace HeroesNotIncluded.Sim.Director;

/// <summary>
/// Bandits up the road. The town meets it if every raider is dead, down or fled before the
/// crisis runs out of time, and nobody of the village died meanwhile. Raiders still about when
/// it does take what they came for and go.
/// </summary>
public sealed class RaidCrisis : ICrisis
{
    public CrisisKind Kind => CrisisKind.Raid;
    public long Started { get; private init; }

    private long _deadline;
    private int _deadBefore;
    private readonly List<Entity> _raiders = [];
    public IReadOnlyList<Entity> Raiders => _raiders;

    internal static RaidCrisis? Begin(Simulation sim, SimRandom rand, DifficultyDef? difficulty, float strength) =>
        Start(sim, rand, Raids.Send(sim, rand, PointsFor(sim, difficulty, strength), difficulty?.MaxRaiders ?? 5), strength);

    /// <summary>A raid warned of (<see cref="Plan"/>): the band and the road as the town was told.</summary>
    internal static RaidCrisis? Begin(Simulation sim, SimRandom rand, PlannedRaid plan) =>
        Start(sim, rand, Raids.SendPlanned(sim, rand, plan.Kinds, plan.Side), plan.Strength);

    /// <summary>Size a raid and pick its road now, to be sent later as it was told.</summary>
    internal static PlannedRaid Plan(Simulation sim, SimRandom rand, DifficultyDef? difficulty, float strength)
    {
        var kinds = Raids.Choose(sim, rand, PointsFor(sim, difficulty, strength), difficulty?.MaxRaiders ?? 5);
        return new PlannedRaid(rand.NextInt(4), strength, kinds);
    }

    private static float PointsFor(Simulation sim, DifficultyDef? difficulty, float strength) =>
        Raids.Points(sim) * sim.Defs.Season.RaidPointsFactor * (difficulty?.PointsFactor ?? 1f) * strength;

    private static RaidCrisis? Start(Simulation sim, SimRandom rand, List<Entity> raiders, float strength)
    {
        if (raiders.Count == 0)
            return null;
        var crisis = new RaidCrisis
        {
            Started = sim.Clock.Tick,
            _deadline = sim.Clock.Tick + SimClock.HoursToTicks(sim.Defs.Season.CrisisHours),
            _deadBefore = VillageDead(sim),
        };
        crisis._raiders.AddRange(raiders);
        RaiseTheAlarm(sim, sim.World.Get<Position>(raiders[0]).Cell);
        sim.Militia.Muster(sim); // not before: until they're here, the town goes about its day
        // A proper raid cuts the town off behind it: the bridge comes down (Tinkering.Repairs).
        if (strength >= 1f && rand.Chance(sim.Defs.Season.RaidBreaksBridge))
            sim.Repairs.BreakBridge(sim, rand);
        return crisis;
    }

    /// <summary>
    /// Whoever of the village is nearest the road they came by shouts it, so the town hears it from
    /// someone, and the bell goes: that's heard everywhere, so it's a message without a place.
    /// </summary>
    private static void RaiseTheAlarm(Simulation sim, int3 from)
    {
        var w = sim.World;
        sim.Messages.Add(MessageKind.Danger, $"The bell rings out: raiders on the {Raids.Direction(sim.Map, from)} road!", null);
        var lookout = sim.Creatures()
            .Where(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human, Dead: false } && SpeechSystem.CanAct(sim, e))
            .OrderBy(e => Maps.Map.Distance(w.Get<Position>(e).Cell, from))
            .FirstOrDefault(Entity.Null);
        if (!lookout.IsNull)
            sim.Speech.Say(sim, lookout, $"Raiders! On the {Raids.Direction(sim.Map, from)} road!", Volume.Shout);
    }

    private static int VillageDead(Simulation sim)
    {
        var n = 0;
        foreach (var e in sim.World.Query<Creature>())
            if (sim.World.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human, Dead: true })
                n++;
        return n;
    }

    /// <summary>Raiders still on the map and on their feet.</summary>
    private List<Entity> StillAbout(Simulation sim)
    {
        var w = sim.World;
        return _raiders.Where(e => w.IsAlive(e) && w.TryGet<Creature>(e, out var c) && !c.Dead && !c.Downed && !w.Has<Leaving>(e)).ToList();
    }

    public bool? Check(Simulation sim)
    {
        var about = StillAbout(sim);
        if (about.Count == 0)
            return VillageDead(sim) == _deadBefore;
        if (sim.Clock.Tick < _deadline)
            return null;
        foreach (var e in about)
            sim.World.Add(e, new Leaving());
        return false;
    }

    public string Outcome(bool met) => met
        ? "The raiders are driven off."
        : "The raiders make off with what they came for.";

    public void Save(SaveWriter w)
    {
        w.Long(Started);
        w.Long(_deadline);
        w.Int(_deadBefore);
        w.Int(_raiders.Count);
        foreach (var e in _raiders)
            w.Entity(e);
    }

    internal static RaidCrisis Read(SaveReader r)
    {
        var crisis = new RaidCrisis { Started = r.Long(), _deadline = r.Long(), _deadBefore = r.Int() };
        for (var n = r.Count(1000); n > 0; n--)
            crisis._raiders.Add(r.Entity());
        return crisis;
    }
}

/// <summary>A raid the town's been warned of: the road it'll come by (a map side) and the band, fixed when told.</summary>
public sealed record PlannedRaid(int Side, float Strength, List<RaiderKindDef> Kinds)
{
    internal void Save(SaveWriter w)
    {
        w.Int(Side);
        w.Float(Strength);
        w.Int(Kinds.Count);
        foreach (var k in Kinds)
            w.Def(k);
    }

    internal static PlannedRaid Read(SaveReader r)
    {
        var side = Math.Clamp(r.Int(), 0, 3);
        var strength = r.Float();
        var kinds = new List<RaiderKindDef>();
        for (var n = r.Count(64); n > 0; n--)
            kinds.Add(r.Def<RaiderKindDef>());
        return new PlannedRaid(side, strength, kinds);
    }
}
