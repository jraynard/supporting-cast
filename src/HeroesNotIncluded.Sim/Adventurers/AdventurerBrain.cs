using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Environment;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Adventurers;

/// <summary>
/// Adventurers in town: out of danger, fed and rested, otherwise lounging about their haunt (the
/// tavern) as if they owned it, sitting down with whatever they're served, and off down the road
/// once their stay is up. They take no orders
/// and do no work; what they want from the town they buy from its shops (<c>Shops.ShopSystem</c>),
/// lining up at the counter for it (<c>Shops.QueueJob</c>).
/// </summary>
public sealed class AdventurerBrain : IBrain
{
    public Job? Think(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var creature = w.Get<Creature>(pawn);
        var tile = w.Get<Position>(pawn).Cell;

        if (Healing.Empathy.DrawnBy(sim, pawn) is { IsNull: false } healer)
            return new Healing.HoldStillJob(healer);
        if (w.Has<Leaving>(pawn))
            return sim.NearestEdge(tile) is { } edge
                ? new LeaveMapJob(edge, w.TryGet<Adventurer>(pawn, out var setting) && setting.Quest != 0
                    && sim.Expeditions.Find(setting.Quest) is { } q ? $"Setting out for {q.Def.Label}" : "Heading out")
                : null;

        var temp = Warmth.FeltAt(sim, tile);
        var tol = creature.Tolerance(sim.Clock.Tick);
        if (sim.Map.Fire[sim.Map.Idx(tile)] > 0 || temp < tol.SafeMin + 2 || temp > tol.SafeMax - 2)
            if (HumanBrain.SeekComfort(sim, pawn, tol, temp, urgent: true) is { } escape)
                return escape;

        var needs = w.Get<Needs>(pawn);
        var food = sim.Defs.Need("Food");
        var rest = sim.Defs.Need("Rest");
        if (needs.Get(food) < food.UrgentBelow && HumanBrain.EatSomething(sim, pawn, creature.Def) is { } eat)
            return eat;
        if (needs.Get(rest) < rest.UrgentBelow || (AiQueries.IsNight(sim) && needs.Get(rest) < 0.5f))
            return HumanBrain.Sleep(sim, pawn);

        // In line for something, take their place in it; waiting on a piece they've handed over (or
        // past the line's end), wait about the shop; else lounge about the haunt, else stay put.
        // Served a drink or a meal, they sit with it, unless they're at the front of a line being served.
        var inLine = sim.Shops.PlaceOf(sim, pawn);
        if (w.Has<Hospitality.Savouring>(pawn) && inLine is not { Place: 0 })
            return new Hospitality.SavourJob(Hospitality.Table.FreeSeat(sim, pawn));
        if (inLine is { } place)
        {
            if (place.Cell is { } cell)
            {
                // A shop that lays its customers down: their place is a bed.
                var s = sim.Map.Structure[sim.Map.Idx(cell)];
                if (!s.IsNull && w.TryGet<Thing>(s, out var bed) && bed.Def.Building?.IsBed == true)
                    return new Shops.LieAbedJob(s, Buildings.Called(sim, place.Shop));
                return new Shops.QueueJob(cell, Buildings.Called(sim, place.Shop), place.Place == 0);
            }
        }
        var center = WaitingAt(sim, pawn)
            ?? (w.TryGet<Adventurer>(pawn, out var a) && PartySystem.HauntOf(sim, a.PartyDef) is { } haunt ? haunt.Anchor : tile);
        return AiQueries.RandomReachableNear(sim, pawn, center, 4) is { } spot
            ? new WanderJob(spot, sim.Rand.Range(120, 480))
            : null;
    }

    /// <summary>The anchor of the building their oldest open order is to be done at, if any.</summary>
    private static int3? WaitingAt(Simulation sim, Entity pawn)
    {
        foreach (var e in sim.Shops.All)
            if (e.IsOpen && e.Poster == pawn && sim.World.IsAlive(e.Where) && sim.World.TryGet<PresetBuilding>(e.Where, out var b))
                return b.Anchor;
        return null;
    }
}
