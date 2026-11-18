using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Speech;

namespace HeroesNotIncluded.Sim.Director;

/// <summary>
/// Bandits up the road. The town meets it if every raider is dead, down or fled before the
/// crisis runs out of time, and nobody of the village died meanwhile. Raiders still about when
/// it does take what they came for and go. The bell rings, and the town musters, once someone
/// sees them coming (<see cref="Watch"/>) or they cross a trip-bell (<see cref="Tinkering.TripBells"/>),
/// which is far sooner: until then the town goes about its day.
/// </summary>
public sealed class RaidCrisis : ICrisis
{
    public CrisisKind Kind => CrisisKind.Raid;
    public long Started { get; private init; }

    private long _deadline;
    private int _deadBefore;
    private readonly List<Entity> _raiders = [];
    public IReadOnlyList<Entity> Raiders => _raiders;

    /// <summary>The bell's rung for them: the town knows, and musters.</summary>
    public bool Alarmed { get; private set; }

    /// <summary>The trip-bell that rang the alarm, Entity.Null if someone saw them (or it hasn't rung).</summary>
    public Entity RungBy { get; private set; } = Entity.Null;

    /// <summary>Where they came in: the edge of the map, for the road's name.</summary>
    private int3 _from;

    /// <summary>A waking village human this near a raider sees them coming and raises the alarm, cells; a sleeper wakes to them this near.</summary>
    public const int LookoutReach = 14, WakesAt = 5;

    /// <summary>However it goes, the town knows within this long of the raiders coming (the dogs, the noise).</summary>
    public const long KnownAfter = SimClock.TicksPerHour / 2;

    internal static RaidCrisis? Begin(Simulation sim, SimRandom rand, DifficultyDef? difficulty, float strength) =>
        Start(sim, rand, Raids.Send(sim, rand, PointsFor(sim, difficulty, strength), difficulty?.MaxRaiders ?? 5, announce: false), strength);

    /// <summary>A raid warned of (<see cref="Plan"/>): the band and the road as the town was told.</summary>
    internal static RaidCrisis? Begin(Simulation sim, SimRandom rand, PlannedRaid plan, bool byRoad = false) =>
        Start(sim, rand, Raids.SendPlanned(sim, rand, plan.Kinds, plan.Side, byRoad, announce: false), plan.Strength);

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
        crisis._from = sim.World.Get<Position>(raiders[0]).Cell;
        crisis.Watch(sim); // someone may be out on that road already
        // A proper raid cuts the town off behind it: the bridge comes down (Tinkering.Repairs).
        if (strength >= 1f && rand.Chance(sim.Defs.Season.RaidBreaksBridge))
            sim.Repairs.BreakBridge(sim, rand);
        return crisis;
    }

    /// <summary>
    /// Not yet known: does someone of the village see them (a waking villager within
    /// <see cref="LookoutReach"/> of one), or has it been long enough that the town can't not know?
    /// Then the alarm goes up. Called often (<see cref="Tinkering.TripBellSystem"/>).
    /// </summary>
    public void Watch(Simulation sim)
    {
        if (Alarmed)
            return;
        var w = sim.World;
        var about = StillAbout(sim);
        if (about.Count == 0)
            return;
        if (sim.Clock.Tick - Started >= KnownAfter)
        {
            Sound(sim, w.Get<Position>(about[0]).Cell, Entity.Null);
            return;
        }
        var town = sim.Creatures()
            .Where(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human, Dead: false, Downed: false } && w.Has<Position>(e))
            .Select(e => (Cell: w.Get<Position>(e).Cell, Reach: SpeechSystem.CanAct(sim, e) ? LookoutReach : WakesAt)).ToList();
        foreach (var raider in about)
        {
            var at = w.Get<Position>(raider).Cell;
            if (town.Exists(v => Math.Abs(v.Cell.Z - at.Z) <= 2 && Math.Max(Math.Abs(v.Cell.X - at.X), Math.Abs(v.Cell.Y - at.Y)) <= v.Reach))
            {
                Sound(sim, at, Entity.Null);
                return;
            }
        }
    }

    /// <summary>The bell now, whoever's seen them: a drill's raid, staged for the player to meet.</summary>
    public void RaiseNow(Simulation sim)
    {
        if (StillAbout(sim) is [var first, ..])
            Sound(sim, sim.World.Get<Position>(first).Cell, Entity.Null);
    }

    /// <summary>
    /// The alarm goes up: from a trip-bell (<paramref name="bell"/>), or whoever of the village is
    /// nearest shouts it. Either way the town's bell follows, heard everywhere, and the town musters.
    /// </summary>
    internal void Sound(Simulation sim, int3 seen, Entity bell)
    {
        if (Alarmed)
            return;
        Alarmed = true;
        RungBy = bell;
        var road = Raids.Direction(sim.Map, _from);
        if (!bell.IsNull)
            sim.Messages.Add(MessageKind.Danger, $"A trip-bell rings out on the {road} road, and the town's bell answers: raiders!", seen, "Raid!");
        else
            RaiseTheAlarm(sim, seen, road);
        sim.Militia.Muster(sim);
    }

    /// <summary>
    /// Whoever of the village is nearest where they were seen shouts it, so the town hears it from
    /// someone, and the bell goes: that's heard everywhere, so it's a message without a place.
    /// </summary>
    private static void RaiseTheAlarm(Simulation sim, int3 seen, string road)
    {
        var w = sim.World;
        sim.Messages.Add(MessageKind.Danger, $"The bell rings out: raiders on the {road} road!", null, "Raid!");
        // The nearest of the town shouts it, woken by them if they were asleep.
        var lookout = sim.Creatures()
            .Where(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human, Dead: false, Downed: false } && w.Has<Position>(e))
            .OrderBy(e => Maps.Map.Distance(w.Get<Position>(e).Cell, seen))
            .FirstOrDefault(Entity.Null);
        if (!lookout.IsNull)
            Systems.Players.Wake(sim, lookout);
        if (!lookout.IsNull && SpeechSystem.CanAct(sim, lookout))
            sim.Speech.Say(sim, lookout, $"Raiders! On the {road} road!", Volume.Shout);
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
        w.Bool(Alarmed);
        w.Entity(RungBy);
        w.Int(_from.X);
        w.Int(_from.Y);
        w.Int(_from.Z);
    }

    internal static RaidCrisis Read(SaveReader r)
    {
        var crisis = new RaidCrisis { Started = r.Long(), _deadline = r.Long(), _deadBefore = r.Int() };
        for (var n = r.Count(1000); n > 0; n--)
            crisis._raiders.Add(r.Entity());
        // From format 57 the alarm waits till they're seen or trip a bell; before, it rang as they came.
        if (r.Version >= 57)
        {
            crisis.Alarmed = r.Bool();
            crisis.RungBy = r.Entity();
            crisis._from = new int3(r.Int(), r.Int(), r.Int());
        }
        else
            crisis.Alarmed = true;
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
