using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;

namespace SupportingCast.Sim.Systems;

/// <summary>
/// Herds of wild animals wander in from the map edge, graze for a day or so and move on: the
/// village's renewable source of meat. Off by default (<see cref="Enabled"/>) so tests and
/// scripted maps stay empty; <see cref="MapGenerator"/> turns it on. Arrivals and the animals'
/// own decisions (<see cref="Ai.AnimalBrain"/>) draw from <see cref="Rand"/>, a stream of its own,
/// so animals that nobody bothers don't reshuffle the rest of the sim.
/// </summary>
public sealed class WildlifeSystem(ulong seed) : ISimSystem, Saving.ISaveState
{
    public string Name => "Wildlife";
    public int Interval => SimClock.TicksPerHour / 4;

    /// <summary>Wildlife randomness: arrivals, herd sizes and where animals wander.</summary>
    public SimRandom Rand { get; } = new(seed ^ 0x3157_D11F_E0u);

    public bool Enabled { get; private set; }

    /// <summary>When the next herd is due; -1 while off.</summary>
    public long NextArrivalTick { get; private set; } = -1;

    public int HerdsArrived { get; private set; }

    /// <summary>Animals killed by anyone (hunters, a goring gone wrong, dragons).</summary>
    public int Hunted { get; internal set; }

    /// <summary>Fish landed by anyone, ever.</summary>
    public int FishCaught { get; private set; }

    private int _nextHerdId;

    /// <summary>A fresh herd id (a migrating herd the storyteller sends).</summary>
    public int NewHerdId() => ++_nextHerdId;

    /// <summary>Fish stock per patch of water (see <see cref="Fishing.PatchOf"/>) and when it was last fished; absent means full.</summary>
    private readonly SortedDictionary<int, (float Stock, long Tick)> _fishStock = [];

    /// <summary>How well stocked a patch of water is now, 0..1 (it recovers between catches).</summary>
    public float FishStock(Simulation sim, int patch)
    {
        if (!_fishStock.TryGetValue(patch, out var s))
            return 1f;
        return Math.Min(1f, s.Stock + Fishing.RecoveryPerDay * SimClock.TicksToDays(sim.Clock.Tick - s.Tick));
    }

    /// <summary>One cast: maybe a fish, which thins the patch out.</summary>
    public bool Cast(Simulation sim, int patch)
    {
        var stock = FishStock(sim, patch);
        if (!sim.Rand.Chance(Fishing.BiteChance * stock))
            return false;
        _fishStock[patch] = (Math.Max(0f, stock - Fishing.CatchDepletion), sim.Clock.Tick);
        FishCaught++;
        return true;
    }

    public void Save(Saving.SaveWriter w, Simulation sim)
    {
        var (a, b, c, d) = Rand.State;
        w.UInt(a); w.UInt(b); w.UInt(c); w.UInt(d);
        w.Bool(Enabled);
        w.Long(NextArrivalTick);
        w.Int(HerdsArrived);
        w.Int(Hunted);
        w.Int(_nextHerdId);
        w.Int(FishCaught);
        w.Int(_fishStock.Count);
        foreach (var (patch, (stock, tick)) in _fishStock)
        {
            w.Int(patch);
            w.Float(stock);
            w.Long(tick);
        }
    }

    public void Load(Saving.SaveReader r, Simulation sim)
    {
        Rand.State = (r.UInt(), r.UInt(), r.UInt(), r.UInt());
        Enabled = r.Bool();
        NextArrivalTick = r.Long();
        HerdsArrived = r.Int();
        Hunted = r.Int();
        _nextHerdId = r.Int();
        FishCaught = r.Int();
        _fishStock.Clear();
        for (int i = 0, n = r.Count(); i < n; i++)
            _fishStock[r.Int()] = (r.Float(), r.Long());
    }

    /// <summary>Start herds arriving (the first after <see cref="WildlifeDef.FirstArrivalHours"/>).</summary>
    public void Enable(Simulation sim)
    {
        Enabled = true;
        NextArrivalTick = sim.Clock.Tick + SimClock.HoursToTicks(sim.Defs.Wildlife.FirstArrivalHours * Rand.Range(0.7f, 1.3f));
    }

    /// <summary>For the state hash: everything here that isn't in components.</summary>
    internal long Fingerprint
    {
        get
        {
            var h = Enabled ? (long)Rand.Fingerprint ^ NextArrivalTick ^ ((long)_nextHerdId << 40) : 0;
            foreach (var (patch, (stock, tick)) in _fishStock)
                h = h * 31 + patch * 7919 + (long)(stock * 1000) + tick;
            return h;
        }
    }

    public void Update(Simulation sim)
    {
        if (!Enabled)
            return;
        var w = sim.World;
        var now = sim.Clock.Tick;

        // Herds whose time is up head for the edge (RaiderBrain walks anyone Leaving off the map).
        var announced = new HashSet<int>();
        foreach (var e in w.Query<Herd>())
        {
            var herd = w.Get<Herd>(e);
            if (now < herd.LeaveTick || !w.Get<Creature>(e).Able || w.Has<Leaving>(e))
                continue;
            w.Add(e, new Leaving());
            if (announced.Add(herd.Id))
                sim.Messages.Add(MessageKind.Info, $"The {w.Get<Creature>(e).Def.Animal!.Plural} are moving on.", w.Get<Position>(e).Cell);
        }

        var def = sim.Defs.Wildlife;
        // Herds that moved on can empty the valley between arrivals: don't keep it empty for long.
        if (def.MinAnimals > 0 && LiveAnimals(sim) < def.MinAnimals)
            NextArrivalTick = Math.Min(NextArrivalTick, now + SimClock.HoursToTicks(def.MeanHoursBetween * def.ScarceArrivalFactor));
        if (now < NextArrivalTick)
            return;
        var live = LiveAnimals(sim);
        // Scarce, the valley refills with the starting kind (deer): game a grown dragon can take.
        if (live < def.MaxAnimals)
            SendHerd(sim, live < def.MinAnimals ? def.StartingKindDef : null);
        // An emptied valley fills up again sooner: a grown dragon eats a deer a day.
        var wait = def.MeanHoursBetween * Rand.Range(0.5f, 1.5f);
        if (LiveAnimals(sim) < def.MinAnimals)
            wait *= def.ScarceArrivalFactor;
        NextArrivalTick = now + SimClock.HoursToTicks(wait);
    }

    public int LiveAnimals(Simulation sim)
    {
        var count = 0;
        foreach (var e in sim.World.Each<Herd>())
            if (sim.World.Get<Creature>(e).Able)
                count++;
        return count;
    }

    /// <summary>A herd walks in from a random edge (also the debug panel's "Herd now"). Returns its animals.</summary>
    public List<Entity> SendHerd(Simulation sim, CreatureDef? kind = null)
    {
        kind ??= PickKind(sim);
        if (kind == null || VillageCenter(sim) is not { } toward)
            return [];
        var side = Rand.NextInt(4);
        if (Raids.EdgeEntry(sim, side, toward) is not { } entry)
            return [];
        var herd = SpawnHerd(sim, kind, entry, toward);
        if (herd.Count > 0)
        {
            HerdsArrived++;
            var what = herd.Count == 1 ? $"A lone {kind.Label} has" : $"{herd.Count} {kind.Animal!.Plural} have";
            sim.Messages.Add(MessageKind.Info, $"{what} wandered in from the {Raids.Direction(sim.Map, entry)}.", entry);
        }
        return herd;
    }

    /// <summary>
    /// Herds already grazing on a fresh map, out of sight of the landing site: a few random spots
    /// <paramref name="minDistance"/> or more from <paramref name="center"/> that can reach it.
    /// </summary>
    public void PlaceStartingHerds(Simulation sim, int3 center, int minDistance = 30)
    {
        var map = sim.Map;
        for (var i = 0; i < sim.Defs.Wildlife.StartingHerds; i++)
        {
            if (((i == 0 ? sim.Defs.Wildlife.StartingKindDef : null) ?? PickKind(sim)) is not { } kind)
                return;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var x = Rand.Range(2, map.Width - 2);
                var y = Rand.Range(2, map.Height - 2);
                if (Math.Abs(x - center.X) + Math.Abs(y - center.Y) < minDistance)
                    continue;
                if (map.NearestStandable(x, y, center.Z, map.Depth) is not { } at || !map.Passable(at) || !sim.Regions.CanReach(at, center, PathEnd.Touch))
                    continue;
                SpawnHerd(sim, kind, at, center);
                break;
            }
        }
    }

    private List<Entity> SpawnHerd(Simulation sim, CreatureDef kind, int3 at, int3 toward)
    {
        var props = kind.Animal!;
        var room = sim.Defs.Wildlife.MaxAnimals - LiveAnimals(sim);
        var size = Math.Min(Rand.Range(props.HerdMin, props.HerdMax + 1), Math.Max(1, room));
        var leave = sim.Clock.Tick + SimClock.HoursToTicks(Rand.Range(props.StayHours[0], props.StayHours[1]));
        var id = _nextHerdId++;
        var animals = new List<Entity>();
        foreach (var spot in Raids.GatherSpots(sim, at, toward, size))
        {
            var e = sim.SpawnCreature(kind, Capitalize(kind.Label), spot, Faction.Wild, ageDays: Rand.Range(1f, 6f), rand: Rand);
            sim.World.Add(e, new Herd { Id = id, LeaveTick = leave });
            animals.Add(e);
        }
        return animals;
    }

    /// <summary>A kind of animal, weighted by commonness (in def order, so it's deterministic).</summary>
    private CreatureDef? PickKind(Simulation sim)
    {
        var total = 0f;
        foreach (var c in sim.Defs.AllCreatures)
            if (c.Animal is { } a)
                total += a.Commonness;
        if (total <= 0)
            return null;
        var roll = Rand.Range(0f, total);
        CreatureDef? last = null;
        foreach (var c in sim.Defs.AllCreatures)
        {
            if (c.Animal is not { Commonness: > 0 } a)
                continue; // predators come only when the storyteller sends them
            last = c;
            roll -= a.Commonness;
            if (roll < 0)
                return c;
        }
        return last;
    }

    /// <summary>Where herds head for when they arrive (the village's first member), so they enter where they can graze.</summary>
    private static int3? VillageCenter(Simulation sim)
    {
        foreach (var e in sim.Creatures())
            if (sim.World.Get<Creature>(e).Faction == Faction.Village)
                return sim.World.Get<Position>(e).Cell;
        return null;
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
