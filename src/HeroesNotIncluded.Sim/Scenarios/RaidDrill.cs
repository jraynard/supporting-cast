using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Director;
using HeroesNotIncluded.Sim.Militia;

namespace HeroesNotIncluded.Sim.Scenarios;

/// <summary>
/// A raid to play through on demand (<c>--raid-test</c>): the player armed with a short sword, two
/// spears hung on the muster rack as the town's, and bandits coming in by the north road at once.
/// It's a real <see cref="RaidCrisis"/>, so the bell, the muster and the outcome all run as they
/// would in a round's beat.
/// </summary>
public static class RaidDrill
{
    public const string Sword = "ShortSword";
    public const string Spear = "IronSpear";
    public const int Spears = 2;
    public const int North = 2; // the map edge at y 0 (Raids.SideName)

    /// <summary>Arm <paramref name="player"/>, stock the rack and send the raid. Call once the round's dealt.</summary>
    public static RaidCrisis? Stage(Simulation sim, Entity player)
    {
        var w = sim.World;
        if (!sim.Director.Enabled)
            sim.Director.Enable(sim.Defs.Difficulty("Normal"));
        sim.Equip(player, sim.Defs.Thing(Sword));

        var rack = w.Query<Thing>().Where(e => MilitiaSystem.IsRack(sim, e)).OrderBy(e => e.Index).DefaultIfEmpty(Entity.Null).First();
        if (rack.IsNull)
            throw new InvalidOperationException("The raid drill needs a muster rack on the map.");
        for (var i = 0; i < Spears; i++)
        {
            var spear = sim.CreateItem(sim.Defs.Thing(Spear), 1);
            w.Add(spear, new TownArms()); // the town's already: hanging it pays nobody
            if (!sim.Store(rack, spear).IsNull)
                throw new InvalidOperationException("The muster rack has no room for the drill's spears.");
        }
        return sim.Director.SendRaid(sim, North);
    }
}
