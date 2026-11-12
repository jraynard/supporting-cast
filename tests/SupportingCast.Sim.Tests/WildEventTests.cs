using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Systems;
using Xunit.Abstractions;
using static SupportingCast.Sim.Tests.TestWorld;

namespace SupportingCast.Sim.Tests;

/// <summary>Animals in the storyteller (<see cref="WildEvents"/>): predators fought turn by turn, migrations, wild dragon visitors.</summary>
public class WildEventTests
{
    private static Simulation Mild(int size = 60)
    {
        var sim = Create(size, size, depth: 8);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    private static Entity Person(Simulation sim, int3 at, string name = "Wren")
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), name, at, ageDays: 25 * 365);
        sim.SetSkills(e);
        return e;
    }

    [Fact]
    public void PredatorsNeverWanderInWithTheHerds()
    {
        var sim = Mild();
        Person(sim, C(30, 30));
        for (var i = 0; i < 60; i++)
            sim.Wildlife.SendHerd(sim);
        Assert.DoesNotContain(sim.World.Query<Creature>(), e => sim.World.Get<Creature>(e).Def.Animal?.Predator == true);
        Assert.True(sim.Defs.Creature("Wolf").Animal!.Predator && sim.Defs.Creature("Bear").Animal!.Predator);
    }

    [Fact]
    public void AMigratingHerdCrossesTheValleyAndLeaves()
    {
        var sim = Mild();
        Person(sim, C(30, 30));
        var herd = WildEvents.SendMigration(sim, new SimRandom(5), sim.Defs.Creature("Deer"));
        Assert.InRange(herd.Count, WildEvents.MigrationMin, WildEvents.MigrationMax);
        var to = sim.World.Get<Migrating>(herd[0]).To;
        var from = sim.World.Get<Position>(herd[0]).Cell;
        Assert.True(Map.Distance(from, to) > 40, "it enters on one side and leaves by the other");
        sim.TickMany(SimClock.HoursToTicks(4));
        Assert.True(herd.Count(sim.World.IsAlive) <= herd.Count / 3, "most of the herd should have crossed and gone");
    }

    [Fact]
    public void TheStorytellerSendsWildEventsButNoPredatorsInTheGraceDaysOrOnPeaceful()
    {
        var sim = Mild();
        Person(sim, C(30, 30));
        sim.Storyteller.SetDifficulty(sim, sim.Defs.AllDifficulties.Single(d => d.Name == "Normal"));
        Assert.True(sim.Storyteller.NextEventTick > sim.Clock.Tick);
        for (var i = 0; i < 50; i++)
            Assert.NotEqual(WildEventKind.Predators, sim.Storyteller.PickEvent(sim)); // day 1: still in the grace days

        var peaceful = Mild();
        Person(peaceful, C(30, 30));
        peaceful.Storyteller.SetDifficulty(peaceful, peaceful.Defs.AllDifficulties.Single(d => d.Name == "Peaceful"));
        peaceful.Clock.Tick += SimClock.DaysToTicks(30);
        for (var i = 0; i < 50; i++)
            Assert.NotEqual(WildEventKind.Predators, peaceful.Storyteller.PickEvent(peaceful));

        sim.Clock.Tick += SimClock.DaysToTicks(20);
        Assert.Contains(Enumerable.Range(0, 60).Select(_ => sim.Storyteller.PickEvent(sim)), k => k == WildEventKind.Predators);
    }

}
