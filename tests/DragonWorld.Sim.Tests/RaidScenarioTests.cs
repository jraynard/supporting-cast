using DragonWorld.Sim.Components;
using DragonWorld.Sim.Maps;
using DragonWorld.Sim.Scenarios;
using DragonWorld.Sim.Systems;
using Xunit.Abstractions;

namespace DragonWorld.Sim.Tests;

/// <summary>
/// The combat slice played headless: two bandits walk in, turn-based combat starts when they're
/// in sight, the AI plays both sides to the end, and afterwards the village looks after its wounded.
/// </summary>
public class RaidScenarioTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1234UL)]
    [InlineData(99UL)]
    public void BanditsAttackFightEndsAndTheWoundedAreTended(ulong seed)
    {
        var sim = Simulation.CreateStandard(TestWorld.LoadDefs(), 160, 160, seed, MapGenerator.RecommendedDepth);
        var center = MapGenerator.Generate(sim);
        SliceDemo.ApplyVillageDefaults(sim, center);
        var cast = RaidDemo.Setup(sim, center);
        try
        {
            Play(sim, cast);
        }
        finally
        {
            foreach (var l in sim.Combat.Log)
                output.WriteLine($"[round {l.Round}, {l.Side}] {l.Text}");
            foreach (var m in sim.Messages.Messages)
                output.WriteLine($"[{SimClock.TicksToDays(m.Tick):F2}d] {m.Kind}: {m.Text}");
        }
    }

    private static void Play(Simulation sim, RaidDemo.Cast cast)
    {
        var w = sim.World;
        for (var t = 0; t < SimClock.TicksPerHour && !sim.Combat.Active; t++)
            sim.Tick();
        Assert.True(sim.Combat.Active, "the bandits should have come within sight");
        Assert.Contains(sim.Combat.Units, u => u.Entity == cast.Dragon);

        sim.Combat.Resolve(sim, maxRounds: 40);
        Assert.False(sim.Combat.Active, $"the fight should be over within 40 rounds (round {sim.Combat.Round})");
        Assert.NotNull(sim.Combat.Outcome);
        var everyone = new[] { cast.Archer, cast.Axeman, cast.Keeper, cast.Dragon }.Concat(cast.Raiders).ToList();
        Assert.Contains(everyone, e => !w.IsAlive(e) || w.Get<Creature>(e).Dead || w.Get<Health>(e).Injuries.Count > 0);

        // A day later, every living villager's wounds are bandaged, and the raiders left lying
        // there were stabilized rather than left to bleed out.
        var survivors = cast.Raiders.Where(r => w.IsAlive(r) && !w.Get<Creature>(r).Dead).ToList();
        sim.TickMany(SimClock.DaysToTicks(1));
        foreach (var r in survivors.Where(w.IsAlive)) // the rest walked off
            Assert.False(w.Get<Creature>(r).Dead, $"{w.Get<Creature>(r).Name} died after the fight");
        foreach (var e in new[] { cast.Archer, cast.Axeman, cast.Keeper, cast.Dragon })
        {
            var c = w.Get<Creature>(e);
            if (c.Dead)
                continue;
            Assert.All(w.Get<Health>(e).Injuries, i => Assert.True(i.Tended, $"{c.Name}'s wounds should be tended"));
        }
    }

    [Fact]
    public void RaidIsDeterministic()
    {
        ulong Run()
        {
            var sim = Simulation.CreateStandard(TestWorld.LoadDefs(), 160, 160, 7, MapGenerator.RecommendedDepth);
            var center = MapGenerator.Generate(sim);
            SliceDemo.ApplyVillageDefaults(sim, center);
            RaidDemo.Setup(sim, center);
            for (var t = 0; t < SimClock.TicksPerHour && !sim.Combat.Active; t++)
                sim.Tick();
            sim.Combat.Resolve(sim, maxRounds: 40);
            sim.TickMany(SimClock.TicksPerHour);
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
