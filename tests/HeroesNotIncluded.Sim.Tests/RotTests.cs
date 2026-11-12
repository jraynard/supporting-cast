using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Food and carcasses going off (<see cref="RotSystem"/>): temperature, stack mixing and splitting.</summary>
public class RotTests
{
    /// <summary>A map held at one temperature, running nothing but rot.</summary>
    private static Simulation At(float celsius)
    {
        var sim = new Simulation(LoadDefs(), 20, 20, 1);
        sim.AddSystem(new RotSystem());
        sim.RebuildDerivedIfNeeded();
        Array.Fill(sim.Map.Temperature, celsius);
        return sim;
    }

    private static Entity ItemAt(Simulation sim, int3 cell) => sim.Map.Item[sim.Map.Idx(cell)];

    [Theory]
    [InlineData(-5f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(5f, 0.5f)]
    [InlineData(20f, 1f)]
    [InlineData(45f, 1.5f)]
    [InlineData(90f, 2f)]
    public void RotSpeedFollowsTheTemperature(float celsius, float rate) => Assert.Equal(rate, RotSystem.RateAt(celsius), 3);

    [Fact]
    public void MeatRotsAwayInItsRotDaysAtRoomTemperature()
    {
        var sim = At(20);
        var meat = sim.Thing("Meat");
        sim.SpawnItem(meat, 10, C(5, 5));
        var item = ItemAt(sim, C(5, 5));
        sim.TickMany(SimClock.DaysToTicks(meat.RotDays * 0.95f));
        Assert.True(sim.World.IsAlive(item));
        Assert.InRange(sim.World.Get<Rot>(item).Progress, 0.9f, 0.99f);
        sim.TickMany(SimClock.DaysToTicks(meat.RotDays * 0.1f));
        Assert.False(sim.World.IsAlive(item), "the meat should have rotted away");
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("rotted away"));
    }

    [Fact]
    public void FrozenFoodKeeps()
    {
        var sim = At(-5);
        sim.SpawnItem(sim.Thing("RawFish"), 10, C(5, 5));
        sim.TickMany(SimClock.DaysToTicks(10));
        Assert.Equal(0f, sim.World.Get<Rot>(ItemAt(sim, C(5, 5))).Progress);
    }

    [Fact]
    public void StacksAverageTheirSpoilageAndSplitsKeepIt()
    {
        var sim = At(20);
        var meat = sim.Thing("Meat");
        sim.SpawnItem(meat, 10, C(5, 5), rot: 0.6f);
        sim.SpawnItem(meat, 20, C(5, 5)); // fresh meat on top
        var stack = ItemAt(sim, C(5, 5));
        Assert.Equal(30, sim.World.Get<Stack>(stack).Count);
        Assert.Equal(0.2f, sim.World.Get<Rot>(stack).Progress, 3);

        var taken = sim.TakeFromStack(stack, 5);
        Assert.Equal(0.2f, sim.World.Get<Rot>(taken).Progress, 3);
    }

    [Fact]
    public void UnbutcheredCarcassesRotAway()
    {
        var sim = At(20);
        var deer = sim.SpawnCreature(sim.Defs.Creature("Deer"), "Deer", C(5, 5), Faction.Wild);
        ref var c = ref sim.World.Get<Creature>(deer);
        NeedsSystem.Kill(sim, deer, ref c);
        Assert.True(sim.World.Has<Rot>(deer));
        sim.TickMany(SimClock.DaysToTicks(c.Def.Animal!.CorpseRotDays * 1.05f));
        Assert.False(sim.World.IsAlive(deer));
    }

    [Fact]
    public void ButcheredMeatIsAsFarGoneAsTheCarcass()
    {
        var sim = Create();
        sim.SpawnCreature(sim.Defs.Creature("Human"), "Aldric", C(10, 10)); // the butcher
        var deer = sim.SpawnCreature(sim.Defs.Creature("Deer"), "Deer", C(14, 10), Faction.Wild);
        ref var c = ref sim.World.Get<Creature>(deer);
        NeedsSystem.Kill(sim, deer, ref c);
        sim.World.Get<Rot>(deer).Progress = 0.5f;
        sim.TickMany(SimClock.HoursToTicks(1));
        Assert.False(sim.World.IsAlive(deer), "the deer should be butchered");
        var meat = sim.World.Query<Thing>().Where(e => sim.World.Get<Thing>(e).Def.Name == "Meat").ToList();
        Assert.NotEmpty(meat);
        Assert.All(meat, e => Assert.InRange(sim.World.Get<Rot>(e).Progress, 0.5f, 0.6f));
    }
}
