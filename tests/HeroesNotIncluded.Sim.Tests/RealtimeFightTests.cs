using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Fights happen in the world's own time: nothing stops the clock, and only those in reach are hurt.</summary>
public class RealtimeFightTests
{
    [Fact]
    public void ARaiderWoundsWhoeverItReaches()
    {
        var sim = Create();
        var w = sim.World;
        var victim = sim.SpawnCreature(sim.Defs.Creature("Human"), "Corin", C(6, 5), ageDays: 25 * 365);
        w.Get<Mind>(victim).Drafted = true; // stands still
        var raider = Raids.SpawnRaider(sim, sim.Defs.AllRaiderKinds[0], "Skarn", C(5, 5), sim.Rand);

        var before = sim.Clock.Tick;
        sim.TickMany(SimClock.TicksPerSecond * 60);

        Assert.Equal(before + SimClock.TicksPerSecond * 60, sim.Clock.Tick);
        Assert.True(w.Get<Creature>(victim).Dead || w.Get<Health>(victim).Injuries.Count > 0, "a minute beside a raider should leave a mark");
        // With nobody left on their feet to fight, it gives up and goes rather than stand about.
        Assert.True(!w.IsAlive(raider) || w.Has<Leaving>(raider) || w.Get<Creature>(victim).Able);
    }
}
