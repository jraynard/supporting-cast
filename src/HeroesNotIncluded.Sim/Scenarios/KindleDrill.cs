using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Scenarios;

/// <summary>
/// The smith's cold start on demand (<c>--drill kindle</c>): the town's nearest fire that goes cold (the forge)
/// put out with charcoal in, and the player standing at it. F lights it (<see cref="KindleSession"/>).
/// </summary>
public static class KindleDrill
{
    /// <summary>Put the fire out and the player beside it. The fire, to look at; Entity.Null if the town has none.</summary>
    public static Entity Stage(Simulation sim, Entity player)
    {
        var w = sim.World;
        var me = w.Get<Position>(player).Cell;
        var fire = w.Query<Fueled>().Where(e => Kindling.GoesCold(sim, e))
            .OrderBy(e => Maps.Map.Distance(w.Get<Thing>(e).Cell, me)).ThenBy(e => e.Index).FirstOrDefault(Entity.Null);
        if (fire.IsNull)
            return Entity.Null;
        Kindling.PutOutAll(sim);
        ref var fueled = ref w.Get<Fueled>(fire);
        fueled.Fuel = Math.Max(fueled.Fuel, 5f);
        var at = w.Get<Thing>(fire).Cell;
        foreach (var c in sim.SpiralFrom(at, 3))
            if (c != at && c.Z == at.Z && sim.Map.Passable(c) && Ai.Pathfinder.Touches(c, at))
            {
                FireDrill.MoveTo(sim, player, c);
                break;
            }
        return fire;
    }
}
