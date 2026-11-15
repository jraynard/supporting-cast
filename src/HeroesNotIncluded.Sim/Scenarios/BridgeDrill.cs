using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Tinkering;

namespace HeroesNotIncluded.Sim.Scenarios;

/// <summary>
/// The bridge to mend on demand (<c>--bridge-test</c>): the span down as a strong raid leaves it,
/// and the player (the tinker, unless told otherwise) at the gap with the wood a mending takes, so
/// bracing it (<see cref="SpanSession"/>) is one key away.
/// </summary>
public static class BridgeDrill
{
    /// <summary>Break the bridge and stand the player at it. Call once the round's dealt.</summary>
    public static Breach? Stage(Simulation sim, Entity player)
    {
        var breach = sim.Repairs.BreakBridge(sim, sim.Rand);
        if (breach == null)
            return null;
        var w = sim.World;
        var stand = breach.Cells
            .SelectMany(gap => sim.SpiralFrom(gap.Cell, 3))
            .Where(c => sim.Map.Passable(c) && breach.Cells.Exists(gap => AvatarSystem.InReach(c, gap.Cell)))
            .OrderBy(c => Maps.Map.Distance(c, breach.Middle))
            .FirstOrDefault(new int3(-1, -1, -1));
        if (stand.X >= 0)
            FireDrill.MoveTo(sim, player, stand);
        FireDrill.PutInHand(sim, player, sim.CreateItem(sim.Defs.Thing("Wood"), Repairs.MendWood));
        return breach;
    }
}
