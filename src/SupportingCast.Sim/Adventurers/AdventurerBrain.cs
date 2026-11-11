using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Environment;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Adventurers;

/// <summary>
/// Adventurers in town: out of danger, fed and rested, otherwise lounging about their haunt (the
/// tavern) as if they owned it, and off down the road once their stay is up. They take no orders
/// and do no work; what they want from the town comes as errands (M2's next stage).
/// </summary>
public sealed class AdventurerBrain : IBrain
{
    public Job? Think(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var creature = w.Get<Creature>(pawn);
        var tile = w.Get<Position>(pawn).Cell;

        if (w.Has<Leaving>(pawn))
            return sim.NearestEdge(tile) is { } edge ? new LeaveMapJob(edge, "Heading out") : null;

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

        // Lounge about the haunt, or wherever they are if the town has none.
        var center = w.TryGet<Adventurer>(pawn, out var a) && PartySystem.HauntOf(sim, a.PartyDef) is { } haunt
            ? haunt.Anchor
            : tile;
        return AiQueries.RandomReachableNear(sim, pawn, center, 4) is { } spot
            ? new WanderJob(spot, sim.Rand.Range(120, 480))
            : null;
    }
}
