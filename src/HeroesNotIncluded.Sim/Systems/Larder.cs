using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Systems;

/// <summary>
/// The village's standing hunting order: keep <see cref="KeepMeat"/> raw meat and fish in store.
/// Once an hour, while there's less than that (counting what animals already marked for the hunt
/// will bring), it marks the next animal with a <see cref="HuntOrder"/>: the nearest to home of a
/// harmless, healthy one from a herd that keeps at least one other, so it never empties a herd.
/// Plain arithmetic, no rolls. Off (0) by default.
/// </summary>
public sealed class LarderSystem : ISimSystem, Saving.ISaveState
{
    public string Name => "Larder";
    public int Interval => SimClock.TicksPerHour;

    /// <summary>Raw meat and fish to keep in store; 0 leaves hunting to the player's own orders.</summary>
    public int KeepMeat { get; set; }

    /// <summary>The most animals marked for the hunt at once, so a big target doesn't empty the valley in an hour.</summary>
    public const int MaxOrders = 2;

    public void Update(Simulation sim)
    {
        if (KeepMeat <= 0)
            return;
        var w = sim.World;
        var stored = 0;
        foreach (var e in sim.AvailableItems())
            if (w.Get<Thing>(e).Def.IsMeat && w.TryGet<Stack>(e, out var s))
                stored += s.Count;
        var orders = 0;
        var coming = 0;
        foreach (var e in w.Each<HuntOrder>())
            if (w.TryGet<Creature>(e, out var c) && !c.Dead && c.Def.Animal is { } animal)
            {
                orders++;
                coming += animal.MeatYield;
            }
        if (orders >= MaxOrders || stored + coming >= KeepMeat)
            return;
        if (Pick(sim) is { IsNull: false } prey)
            w.Add(prey, new HuntOrder());
    }

    /// <summary>The animal the larder would mark next, or Entity.Null.</summary>
    public static Entity Pick(Simulation sim)
    {
        var w = sim.World;
        if (Home(sim) is not { } home)
            return Entity.Null;
        var herdSize = new Dictionary<int, int>();
        foreach (var e in w.Each<Herd>())
            if (w.TryGet<Creature>(e, out var c) && c.Able && !w.Has<Leaving>(e) && !w.Has<HuntOrder>(e)) // what's left once the hunters are done
                herdSize[w.Get<Herd>(e).Id] = herdSize.GetValueOrDefault(w.Get<Herd>(e).Id) + 1;
        var best = Entity.Null;
        var bestDist = int.MaxValue;
        foreach (var e in w.Each<Creature>())
        {
            var c = w.Get<Creature>(e);
            if (c.Def.Animal is not { Aggressive: false } || c.Faction != Faction.Wild || !c.Able
                || w.Has<HuntOrder>(e) || w.Has<Leaving>(e))
                continue;
            // Never the last of its herd: the valley should keep its game.
            if (!w.TryGet<Herd>(e, out var herd) || herdSize.GetValueOrDefault(herd.Id) < 2)
                continue;
            var d = Maps.Map.Distance(home, w.Get<Position>(e).Cell);
            if (d < bestDist)
            {
                best = e;
                bestDist = d;
            }
        }
        return best;
    }

    /// <summary>Where the village is: its first villager (by entity order), as wildlife reckons it.</summary>
    private static int3? Home(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in w.Each<Creature>())
        {
            var c = w.Get<Creature>(e);
            if (c.Faction == Faction.Village && c.Def.Race == Race.Human && !c.Dead)
                return w.Get<Position>(e).Cell;
        }
        return null;
    }

    public void Save(Saving.SaveWriter w, Simulation sim) => w.Int(KeepMeat);

    public void Load(Saving.SaveReader r, Simulation sim) => KeepMeat = r.Int();
}
