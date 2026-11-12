using Brutal.Numerics;
using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Tests;

/// <summary>Tick smoothing (<see cref="Interpolation"/>), and turn-based moves that happen without ticks.</summary>
public class InterpolationTests
{
    private static Simulation Field()
    {
        var sim = Simulation.CreateStandard(DefDatabase.LoadDirectory(DevPaths.Defs!), 20, 20, 3);
        sim.RebuildDerivedIfNeeded();
        return sim;
    }

    [Fact]
    public void BetweenTicksACreatureIsDrawnPartWay()
    {
        var sim = Field();
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), "Wren", new int3(5, 5, 1));
        var interp = new Interpolation();
        interp.Capture(sim);
        sim.World.Get<Position>(e) = Position.At(new int3(6, 5, 1)); // a tick's worth of walking
        sim.Tick();
        interp.Alpha = 0.5f;
        var drawn = interp.Smoothed(sim, e, sim.World.Get<Position>(e).Pos);
        Assert.InRange(drawn.X, 5.9f, 6.1f); // halfway from 5.5 to 6.5
    }

    [Fact]
    public void AfterAResetMovesWithoutTicksAreDrawnExactly()
    {
        // A fight moves units with the clock frozen; the game resets the smoothing when one starts.
        var sim = Field();
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), "Wren", new int3(5, 5, 1));
        var interp = new Interpolation();
        interp.Capture(sim);
        sim.Tick();
        interp.Alpha = 0.3f; // left over from before the fight
        interp.Reset();
        sim.World.Get<Position>(e) = Position.At(new int3(6, 5, 1)); // a combat step, no tick
        var current = sim.World.Get<Position>(e).Pos;
        Assert.Equal(current, interp.Smoothed(sim, e, current));
    }
}
