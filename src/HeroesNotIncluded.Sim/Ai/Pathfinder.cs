using HeroesNotIncluded.Sim.Maps;

namespace HeroesNotIncluded.Sim.Ai;

/// <summary>The village's pathfinder: Narsil's <see cref="GridPathfinder{TGrid}"/> over the <see cref="Map"/>.</summary>
public sealed class Pathfinder(Map map, Regions regions) : GridPathfinder<MapNav>(new MapNav(map), regions)
{
    /// <inheritdoc cref="NavGraph.Touches"/>
    public static bool Touches(int3 at, int3 target) => NavGraph.Touches(at, target);
}
