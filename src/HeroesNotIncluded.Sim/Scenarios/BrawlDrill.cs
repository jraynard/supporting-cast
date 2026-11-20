using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Director;
using HeroesNotIncluded.Sim.Hospitality;
using HeroesNotIncluded.Sim.Modding;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Scenarios;

/// <summary>
/// A brawl to settle on demand (<c>--brawl-test</c>, <c>--drill brawl</c>): the rowdy crowd already
/// in the inn and you among them. It's a real <see cref="BrawlCrisis"/>, so the heat, the ringleader
/// and the outcome run as they would in a round's beat.
/// </summary>
public static class BrawlDrill
{
    /// <summary>Send the crowd, stand them in the inn, and the player too. The inn, to look at; Entity.Null if it couldn't.</summary>
    public static Entity Stage(Simulation sim, Entity player)
    {
        if (!sim.Director.Enabled)
            sim.Director.Enable(sim.Defs.Difficulty("Normal"));
        if (sim.Director.Send(sim, CrisisKind.Brawl) is not BrawlCrisis brawl)
            return Entity.Null;
        var w = sim.World;
        var anchor = w.Get<PresetBuilding>(brawl.Inn).Anchor;
        var inside = sim.SpiralFrom(anchor, 14).Where(c => c.Z == anchor.Z && sim.Map.Passable(c) && Buildings.At(sim, c) == brawl.Inn).ToList();
        if (inside.Count == 0)
            return brawl.Inn;
        // The crowd in the middle of the room, the player a few steps off.
        var middle = inside[inside.Count / 2];
        var spots = inside.OrderBy(c => Maps.Map.Distance(c, middle)).ThenBy(c => c.X).ThenBy(c => c.Y).ToList();
        for (var i = 0; i < brawl.Crowd.Count && i < spots.Count; i++)
            FireDrill.MoveTo(sim, brawl.Crowd[i], spots[i]);
        FireDrill.MoveTo(sim, player, spots[Math.Min(spots.Count - 1, brawl.Crowd.Count + 2)]);
        return brawl.Inn;
    }
}

/// <summary>The game's own drills, registered as a mod's would be.</summary>
internal sealed class CoreDrills : ISimMod
{
    public void Register(SimRegistry registry) => registry.Drill("brawl", BrawlDrill.Stage);
}
