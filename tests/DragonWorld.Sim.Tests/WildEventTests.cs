using DragonWorld.Sim.Ai;
using DragonWorld.Sim.Combat;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Core;
using DragonWorld.Sim.Maps;
using DragonWorld.Sim.Scenarios;
using DragonWorld.Sim.Systems;
using Xunit.Abstractions;
using static DragonWorld.Sim.Tests.TestWorld;

namespace DragonWorld.Sim.Tests;

/// <summary>Animals in the storyteller (<see cref="WildEvents"/>): predators fought turn by turn, migrations, wild dragon visitors.</summary>
public class WildEventTests(ITestOutputHelper output)
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

    [Theory]
    [InlineData(1234UL)]
    [InlineData(99UL)]
    public void AWolfPackComesForTheVillageAndIsFoughtOffTurnByTurn(ulong seed)
    {
        var sim = Simulation.CreateStandard(LoadDefs(), 160, 160, seed, MapGenerator.RecommendedDepth);
        var center = MapGenerator.Generate(sim);
        SliceDemo.ApplyVillageDefaults(sim, center);
        var cast = RaidDemo.Setup(sim, center);
        foreach (var r in cast.Raiders)
            sim.World.Destroy(r); // wolves this time, not bandits
        var pack = WildEvents.SendPredators(sim, new SimRandom(seed), 200, sim.Defs.Creature("Wolf"));
        Assert.InRange(pack.Count, 3, 6);
        Assert.All(pack, e => Assert.Equal(Faction.Predator, sim.World.Get<Creature>(e).Faction));
        try
        {
            for (var t = 0; t < SimClock.HoursToTicks(3) && !sim.Combat.Active; t++)
                sim.Tick();
            Assert.True(sim.Combat.Active, "the wolves should come within sight of the village");
            Assert.Contains(sim.Combat.Units, u => pack.Contains(u.Entity) && u.Side == Side.Raiders);
            Assert.Contains(sim.Messages.Messages, m => m.Text.StartsWith("Wolves!"));

            sim.Combat.Resolve(sim, maxRounds: 40);
            Assert.False(sim.Combat.Active, "the fight should be over within 40 rounds");
            var w = sim.World;
            Assert.Contains(pack, e => !w.IsAlive(e) || w.Get<Creature>(e).Dead || !w.Get<Creature>(e).Able || w.Has<Leaving>(e));
        }
        finally
        {
            foreach (var l in sim.Combat.Log)
                output.WriteLine($"[round {l.Round}, {l.Side}] {l.Text}");
        }
    }

    [Fact]
    public void APackBreaksOnceHalfOfItIsDown()
    {
        var sim = Mild();
        Person(sim, C(30, 30));
        var pack = WildEvents.SendPredators(sim, new SimRandom(3), 400, sim.Defs.Creature("Wolf"));
        Assert.True(pack.Count >= 4);
        sim.Combat.Start(sim);
        var units = sim.Combat.Units.Where(u => pack.Contains(u.Entity)).ToList();
        Assert.False(Tactics.PackBroken(sim, sim.Combat, pack[0]));
        for (var i = 0; i < (units.Count + 1) / 2; i++)
            HealthSystem.SetDowned(sim, units[i].Entity, ref sim.World.Get<Creature>(units[i].Entity), true);
        Assert.True(Tactics.PackBroken(sim, sim.Combat, units[^1].Entity));
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
    public void AWildDragonFedFromTheStoresStays()
    {
        var sim = Mild();
        var wren = Person(sim, C(30, 30));
        var village = Dragons.Spawn(sim, null, DragonElement.Fire, "Ashwing", C(32, 30), ageDays: 20);
        var visitor = WildEvents.SendDragonVisitor(sim, new SimRandom(7));
        Assert.False(visitor.IsNull);
        var w = sim.World;
        Assert.Equal(Faction.Wild, w.Get<Creature>(visitor).Faction);
        Assert.NotEqual(w.Get<Dragon>(village).Sex, w.Get<Dragon>(visitor).Sex);

        // It eats meat laid out for it, and after a few meals it settles.
        sim.SpawnItem(sim.Thing("Meat"), 30, w.Get<Position>(visitor).Cell);
        var food = sim.Defs.Need("Food");
        for (var t = 0; t < SimClock.HoursToTicks(12) && w.Has<Visitor>(visitor); t++)
        {
            if (w.Get<Needs>(visitor).Get(food) > 0.5f)
                w.Get<Needs>(visitor).Values[food.Index] = 0.1f; // hungry again: another meal
            w.Get<Needs>(wren).Values[food.Index] = 1f; // (so the meat is left for the dragon)
            sim.Tick();
        }
        Assert.False(w.Has<Visitor>(visitor));
        Assert.Equal(Faction.Village, w.Get<Creature>(visitor).Faction);
    }

    [Fact]
    public void AnUnfedVisitorMovesOnAndDoesNoVillageWork()
    {
        var sim = Mild();
        Person(sim, C(30, 30));
        var visitor = WildEvents.SendDragonVisitor(sim, new SimRandom(8));
        var w = sim.World;
        w.Get<Visitor>(visitor).LeaveTick = sim.Clock.Tick + 10;
        sim.TickMany(SimClock.TicksPerHour / 2);
        Assert.True(w.Has<Leaving>(visitor) || !w.IsAlive(visitor));
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

    [Fact]
    public void VisitorsMigrationsAndTheEventTimerSurviveASave()
    {
        var sim = Mild();
        Person(sim, C(30, 30));
        sim.Storyteller.SetDifficulty(sim, sim.Defs.AllDifficulties.Single(d => d.Name == "Normal"));
        var visitor = WildEvents.SendDragonVisitor(sim, new SimRandom(9));
        var herd = WildEvents.SendMigration(sim, new SimRandom(9), sim.Defs.Creature("Deer"));
        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), sim.Defs);
        Assert.Equal(sim.World.Get<Visitor>(visitor).LeaveTick, loaded.World.Get<Visitor>(visitor).LeaveTick);
        Assert.Equal(sim.World.Get<Migrating>(herd[0]).To, loaded.World.Get<Migrating>(herd[0]).To);
        Assert.Equal(sim.Storyteller.NextEventTick, loaded.Storyteller.NextEventTick);
    }
}
