using SupportingCast.Sim.Components;

namespace SupportingCast.Sim.Tests;

public class EggTests
{
    private static Simulation EggOnly(float temperature, out Entity egg)
    {
        var sim = TestWorld.Create(20, 20);
        sim.Climate.BaseTemperature = temperature;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, temperature);
        egg = sim.CreateItem(sim.Thing("DragonEgg"), 1);
        sim.PlaceItemNear(egg, C(10, 10));
        return sim;
    }

    [Fact]
    public void EggHatchesWhenKeptInRange()
    {
        var sim = EggOnly(40f, out var egg);
        var human = sim.SpawnCreature(sim.Defs.Creature("Human"), "Tester", C(12, 12));
        sim.World.Get<Mind>(human).Drafted = true; // keep the tester out of the way

        sim.TickMany(SimClock.DaysToTicks(1.6f));

        Assert.False(sim.World.IsAlive(egg));
        var dragon = Assert.Single(sim.World.Query<Dragon>());
        Assert.Equal(human, sim.World.Get<Dragon>(dragon).Keeper);
        Assert.Equal(dragon, sim.World.Get<Keeper>(human).Dragon);
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("hatched"));
    }

    [Fact]
    public void ColdEggDiesWithoutHatching()
    {
        var sim = EggOnly(5f, out var egg);
        // A cold egg should survive the first night (giving the player time to react) but not two days.
        sim.TickMany(SimClock.DaysToTicks(1f));
        Assert.True(sim.World.IsAlive(egg));
        sim.TickMany(SimClock.DaysToTicks(1.5f));
        Assert.False(sim.World.IsAlive(egg));
        Assert.Empty(sim.World.Query<Dragon>());
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("never hatch"));
    }

    [Fact]
    public void EggDoesNotProgressOutsideRange()
    {
        var sim = EggOnly(15f, out var egg);
        sim.TickMany(SimClock.TicksPerHour * 2);
        Assert.Equal(0f, sim.World.Get<Egg>(egg).Progress);
        Assert.True(sim.World.Get<Thing>(egg).Hp < 100f);
    }
}
