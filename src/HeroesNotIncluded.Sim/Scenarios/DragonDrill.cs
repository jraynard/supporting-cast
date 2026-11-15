using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Director;

namespace HeroesNotIncluded.Sim.Scenarios;

/// <summary>
/// The dragon on demand (<c>--dragon-test</c>): a real <see cref="DragonCrisis"/> in a round, so
/// the alarm, the town hiding, a party's fight and the outcome run as they would at the season's
/// end, with the player at the well and a full bucket in hand, to see what water does against it.
/// </summary>
public static class DragonDrill
{
    /// <summary>Send the dragon and stand the player at the well. Call once the round's dealt.</summary>
    public static DragonCrisis? Stage(Simulation sim, Entity player)
    {
        if (!sim.Director.Enabled)
            sim.Director.Enable(sim.Defs.Difficulty("Normal"));
        var dragon = sim.Director.Send(sim, CrisisKind.Dragon) as DragonCrisis;
        FireDrill.StandAtWell(sim, player, sim.World.Get<Position>(player).Cell);
        return dragon;
    }
}
