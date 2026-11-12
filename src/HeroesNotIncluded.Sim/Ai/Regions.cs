using HeroesNotIncluded.Sim.Maps;

namespace HeroesNotIncluded.Sim.Ai;

/// <summary>The village's regions: Narsil's <see cref="GridRegions{TGrid}"/> over the <see cref="Map"/>.</summary>
public sealed class Regions(Map map) : GridRegions<MapNav>(new MapNav(map));
