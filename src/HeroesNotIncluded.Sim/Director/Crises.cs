using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Healing;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Director;

/// <summary>Things crises share: who raises the alarm.</summary>
internal static class Alarm
{
    /// <summary>Whoever of the village is nearest shouts it, so the town hears it from someone and not from the sky.</summary>
    public static void Raise(Simulation sim, int3 near, string line)
    {
        var w = sim.World;
        var lookout = sim.Creatures()
            .Where(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human, Dead: false } && SpeechSystem.CanAct(sim, e))
            .OrderBy(e => Map.Distance(w.Get<Position>(e).Cell, near))
            .FirstOrDefault(Entity.Null);
        if (!lookout.IsNull)
            sim.Speech.Say(sim, lookout, line, Volume.Shout);
    }
}

/// <summary>
/// A building catches: a spark from a hearth, a lamp knocked over. The town meets it if the fire's
/// out before the building has lost a fifth of itself (anyone can beat out flames: F at the fire,
/// and the AI does it as work).
/// </summary>
public sealed class FireCrisis : ICrisis
{
    public CrisisKind Kind => CrisisKind.Fire;
    public long Started { get; private init; }
    public Entity Building { get; private init; }
    public int3 Origin { get; private init; }
    private int _parts;
    private long _deadline;

    /// <summary>A fire this far from where it started still counts as this one.</summary>
    public const int Spread = 10;

    internal static FireCrisis? Begin(Simulation sim, SimRandom rand)
    {
        var w = sim.World;
        var buildings = w.Query<PresetBuilding>()
            .Where(b => w.Get<PresetBuilding>(b) is { Phase: BuildPhase.Done, Demolishing: false } && Buildings.Parts(sim, b).Any())
            .ToList();
        while (buildings.Count > 0)
        {
            var b = buildings[rand.NextInt(buildings.Count)];
            buildings.Remove(b);
            var anchor = w.Get<PresetBuilding>(b).Anchor;
            var cells = sim.SpiralFrom(anchor, 8)
                .Where(c => Buildings.At(sim, c) == b && sim.Map.Passable(c) && sim.Map.Fire[sim.Map.Idx(c)] <= 0 && Flammable(sim, c))
                .ToList();
            if (cells.Count == 0)
                continue;
            var origin = cells[rand.NextInt(cells.Count)];
            sim.Fire.Ignite(sim, origin, 1f);
            var name = Buildings.Called(sim, b);
            sim.Messages.Add(MessageKind.Danger, $"Fire at {name}!", origin);
            Alarm.Raise(sim, origin, $"Fire! Fire at {name}!");
            return new FireCrisis
            {
                Started = sim.Clock.Tick, Building = b, Origin = origin, _parts = Buildings.Parts(sim, b).Count(),
                _deadline = sim.Clock.Tick + SimClock.HoursToTicks(sim.Defs.Season.CrisisHours),
            };
        }
        return null;
    }

    /// <summary>Something that burns in the cell or beside it (a timber wall, a wooden floor).</summary>
    private static bool Flammable(Simulation sim, int3 c)
    {
        var w = sim.World;
        foreach (var n in sim.SpiralFrom(c, 1))
        {
            if (!sim.Map.InBounds(n))
                continue;
            var i = sim.Map.Idx(n);
            foreach (var e in new[] { sim.Map.Structure[i], sim.Map.Floor[i] })
                if (!e.IsNull && w.TryGet<Thing>(e, out var t) && t.Flammability > 0f)
                    return true;
        }
        return false;
    }

    public bool? Check(Simulation sim)
    {
        var burning = false;
        foreach (var i in sim.Fire.BurningTiles)
            if (Map.Distance(sim.Map.CellOf(i), Origin) <= Spread)
            {
                burning = true;
                break;
            }
        if (!burning)
        {
            var left = sim.World.IsAlive(Building) ? Buildings.Parts(sim, Building).Count() : 0;
            return left >= _parts * 0.8f;
        }
        return sim.Clock.Tick >= _deadline ? false : null;
    }

    public string Outcome(bool met) => met ? "The fire's out, and the building still stands." : "The fire's done its worst.";

    public void Save(SaveWriter w)
    {
        w.Long(Started);
        w.Entity(Building);
        w.Int(Origin.X); w.Int(Origin.Y); w.Int(Origin.Z);
        w.Int(_parts);
        w.Long(_deadline);
    }

    internal static FireCrisis Read(SaveReader r)
    {
        var c = new FireCrisis { Started = r.Long(), Building = r.Entity(), Origin = new int3(r.Int(), r.Int(), r.Int()) };
        c._parts = r.Int();
        c._deadline = r.Long();
        return c;
    }
}

/// <summary>
/// Hard times at the inn: rats have been at the larder (half its food spoiled) and the carter
/// won't come while it lasts. The town meets it if the inn keeps its customers served anyway:
/// enough meals and drinks done before it's over (<see cref="Served"/>).
/// </summary>
public sealed class ShortageCrisis : ICrisis
{
    public CrisisKind Kind => CrisisKind.Shortage;
    public long Started { get; private init; }
    private int _servedBefore;
    private long _deadline;

    /// <summary>Meals and drinks the inn has to serve while it lasts.</summary>
    public const int Served = 6;

    /// <summary>How long the carter stays away, in game hours.</summary>
    public const float Hours = 24;

    private static int Count(Simulation sim) =>
        sim.Shops.Served(ServiceKind.Meal) + sim.Shops.Served(ServiceKind.Drink) + sim.Shops.Served(ServiceKind.MixedDrink);

    internal static ShortageCrisis? Begin(Simulation sim)
    {
        var w = sim.World;
        var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        if (inn.IsNull)
            return null;
        // Rats in the larder: half of every stack of food spoiled.
        foreach (var store in Buildings.Parts(sim, inn).Where(e => w.Get<Thing>(e).Def.Name == "Larder").ToList())
            foreach (var item in sim.StoredIn(store).ToList())
                if (w.Get<Thing>(item).Def.Nutrition > 0 && w.TryGet<Stack>(item, out var stack))
                {
                    var lose = (stack.Count + 1) / 2;
                    if (lose >= stack.Count)
                        sim.DestroyThing(item);
                    else
                        w.Get<Stack>(item).Count -= lose;
                }
        var at = w.Get<PresetBuilding>(inn).Anchor;
        sim.Messages.Add(MessageKind.Danger, "Rats have been at the inn's larder, and word is the carter won't come while there's trouble on the road.", at);
        Alarm.Raise(sim, at, "Rats in the larder! Half of it's spoiled!");
        return new ShortageCrisis
        {
            Started = sim.Clock.Tick, _servedBefore = Count(sim), _deadline = sim.Clock.Tick + SimClock.HoursToTicks(Hours),
        };
    }

    public bool? Check(Simulation sim)
    {
        if (Count(sim) - _servedBefore >= Served)
            return true;
        return sim.Clock.Tick >= _deadline ? false : null;
    }

    public string Outcome(bool met) => met ? "The inn kept its tables served through the shortage." : "The inn's tables went bare in the shortage.";

    public void Save(SaveWriter w)
    {
        w.Long(Started);
        w.Int(_servedBefore);
        w.Long(_deadline);
    }

    internal static ShortageCrisis Read(SaveReader r) => new() { Started = r.Long(), _servedBefore = r.Int(), _deadline = r.Long() };
}

/// <summary>
/// A party brings a fever to town (<see cref="FeverSystem"/>). It spreads to whoever sits near
/// them. The town meets it if, before it's over, nobody in town is fevered any more and nobody
/// carried it out of town; the healer is who can see to that.
/// </summary>
public sealed class PlagueCrisis : ICrisis
{
    public CrisisKind Kind => CrisisKind.Plague;
    public long Started { get; private init; }
    private long _deadline;
    private bool _carriedOff;
    private readonly List<Entity> _fevered = [];

    /// <summary>How long the town has to see the fever off, in game hours.</summary>
    public const float Hours = 12;

    internal static PlagueCrisis? Begin(Simulation sim, SimRandom rand)
    {
        var w = sim.World;
        var carriers = sim.Creatures().Where(e => w.Has<Adventurer>(e) && !w.Get<Creature>(e).Dead && !w.Has<Combat.Leaving>(e)).ToList();
        if (carriers.Count == 0)
            return null;
        var first = carriers[rand.NextInt(carriers.Count)];
        FeverSystem.Catch(sim, first);
        sim.Speech.Say(sim, first, "*cough* Don't mind me. Long road. *cough*", Volume.Say);
        sim.Messages.Add(MessageKind.Warning, $"{w.Get<Creature>(first).Name} looks flushed, and won't stop coughing.", w.Get<Position>(first).Cell);
        var crisis = new PlagueCrisis { Started = sim.Clock.Tick, _deadline = sim.Clock.Tick + SimClock.HoursToTicks(Hours) };
        crisis._fevered.Add(first);
        return crisis;
    }

    public bool? Check(Simulation sim)
    {
        var w = sim.World;
        // Anyone it reached is watched; anyone who left town still sick took it with them.
        foreach (var e in sim.Creatures())
            if (!_fevered.Contains(e) && FeverSystem.Has(sim, e))
                _fevered.Add(e);
        var still = 0;
        foreach (var e in _fevered)
        {
            if (!w.IsAlive(e))
            {
                _carriedOff = true;
                continue;
            }
            if (FeverSystem.Has(sim, e) && !w.Get<Creature>(e).Dead)
                still++;
        }
        _fevered.RemoveAll(e => !w.IsAlive(e) || !FeverSystem.Has(sim, e));
        if (still == 0)
            return !_carriedOff;
        return sim.Clock.Tick >= _deadline ? false : null;
    }

    public string Outcome(bool met) => met ? "The fever's gone from town." : "The fever's had its way with the town.";

    public void Save(SaveWriter w)
    {
        w.Long(Started);
        w.Long(_deadline);
        w.Bool(_carriedOff);
        w.Int(_fevered.Count);
        foreach (var e in _fevered)
            w.Entity(e);
    }

    internal static PlagueCrisis Read(SaveReader r)
    {
        var c = new PlagueCrisis { Started = r.Long() };
        c._deadline = r.Long();
        c._carriedOff = r.Bool();
        for (var n = r.Count(1000); n > 0; n--)
            c._fevered.Add(r.Entity());
        return c;
    }
}
