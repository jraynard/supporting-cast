using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Hospitality;

/// <summary>
/// A rat that's got into a larder (the shortage crisis, <see cref="Director.ShortageCrisis"/>): it
/// keeps about the larder, darts off when someone comes close, and gnaws its food while it lives
/// (<see cref="RatSystem"/>). The cook goes after them with the kitchen knife: a fight at the
/// smallest scale, through free movement and blows.
/// </summary>
public struct Vermin
{
    public Entity Larder;

    /// <summary>When it next gets its teeth into something.</summary>
    public long NextGnaw;
}

/// <summary>
/// Rats at their larder: each one left alive spoils another portion now and then. When the hard
/// times are over (no shortage on), those still about scurry off for good.
/// </summary>
public sealed class RatSystem : ISimSystem
{
    public string Name => "Rats";
    public int Interval => 300;

    /// <summary>Game hours between a rat's meals: each costs the larder a portion.</summary>
    public const float GnawHours = 1.5f;

    /// <summary>How near its larder a rat must be to get at it.</summary>
    public const int Reach = 2;

    public const string Def = "Rat";

    public void Update(Simulation sim)
    {
        var w = sim.World;
        if (w.Count<Vermin>() == 0)
            return;
        var now = sim.Clock.Tick;
        var hardTimes = sim.Director.Active.Any(c => c.Kind == Defs.CrisisKind.Shortage);
        foreach (var rat in w.Query<Vermin>())
        {
            if (!w.TryGet<Creature>(rat, out var c) || c.Dead || !c.Able)
                continue;
            ref var v = ref w.Get<Vermin>(rat);
            // The trouble's over: off they go.
            if (!hardTimes || !w.IsAlive(v.Larder))
            {
                if (!w.Has<Migrating>(rat) && sim.NearestEdge(w.Get<Position>(rat).Cell) is { } edge)
                    w.Add(rat, new Migrating { To = edge });
                continue;
            }
            if (w.Has<Migrating>(rat))
                continue; // on its way out (scared off: Afterlife.Hauntings' spook)
            if (now < v.NextGnaw || Maps.Map.Distance(w.Get<Position>(rat).Cell, w.Get<Thing>(v.Larder).Cell) > Reach)
                continue;
            v.NextGnaw = now + SimClock.HoursToTicks(GnawHours);
            Gnaw(sim, v.Larder);
        }
    }

    /// <summary>A portion of the biggest stack of food in the larder, gone.</summary>
    public static bool Gnaw(Simulation sim, Entity larder)
    {
        var w = sim.World;
        var food = Entity.Null;
        var most = 0;
        foreach (var item in sim.StoredIn(larder))
            if (w.Get<Thing>(item).Def.Nutrition > 0 && w.Get<Stack>(item).Count > most)
                (food, most) = (item, w.Get<Stack>(item).Count);
        if (food.IsNull)
            return false;
        if (most <= 1)
            sim.DestroyThing(food);
        else
            w.Get<Stack>(food).Count--;
        return true;
    }

    /// <summary>Rats into a larder: <paramref name="count"/> of them, about it, each soon at the food.</summary>
    public static List<Entity> Loose(Simulation sim, Entity larder, int count)
    {
        var w = sim.World;
        var list = new List<Entity>();
        if (!sim.Defs.TryCreature(Def, out var def) || !w.IsAlive(larder))
            return list;
        var at = w.Get<Thing>(larder).Cell;
        var cells = sim.SpiralFrom(at, 3).Where(c => c != at && c.Z == at.Z && sim.Map.Passable(c)).Take(count).ToList();
        for (var i = 0; i < cells.Count; i++)
        {
            var rat = sim.SpawnCreature(def, "Rat", cells[i], Faction.Wild, ageDays: 60);
            w.Add(rat, new Vermin { Larder = larder, NextGnaw = sim.Clock.Tick + SimClock.HoursToTicks(GnawHours) * (i + 1) / count });
            list.Add(rat);
        }
        return list;
    }

    /// <summary>The live rats in a building's larders, nearest <paramref name="to"/> first.</summary>
    public static IEnumerable<Entity> In(Simulation sim, Entity building, int3 to)
    {
        var w = sim.World;
        return w.Query<Vermin>()
            .Where(e => w.TryGet<Creature>(e, out var c) && !c.Dead && !w.Has<Migrating>(e) && Buildings.Of(sim, w.Get<Vermin>(e).Larder) == building)
            .OrderBy(e => Maps.Map.Distance(w.Get<Position>(e).Cell, to)).ThenBy(e => e.Index);
    }
}
