using SupportingCast.Sim.Components;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Systems;
using Xunit.Abstractions;

namespace SupportingCast.Sim.Tests;

/// <summary>
/// Can a small village keep a full-grown dragon fed once the starting meat is gone (and rotting)?
/// Three villagers with one bow, the slice's fishing spots, and a player who marks game that
/// wanders near. The adult eats about 23 meat a day, more than the starting kit lasts.
/// </summary>
public class FoodChainScenarioTests(ITestOutputHelper output)
{
    private const int Days = 5;

    [Theory]
    [InlineData(1234UL)]
    [InlineData(99UL)]
    public void HuntersAndFishersKeepAnAdultDragonFed(ulong seed)
    {
        var sim = Simulation.CreateStandard(TestWorld.LoadDefs(), 160, 160, seed, MapGenerator.RecommendedDepth);
        var center = MapGenerator.Generate(sim);
        SliceDemo.ApplyVillageDefaults(sim, center);
        SliceDemo.ApplyPlayerOrders(sim, center);
        var w = sim.World;
        // A logistics test: average villagers. A trait's small mood effect on work speed shifts
        // where everyone (and so the shy herds) ends up, which swings the hunt more than the
        // trait itself would. The slice and raid scenarios keep rolled traits.
        foreach (var e in w.Query<Traits>())
            w.Get<Traits>(e).List.Clear();

        // Skip the egg: a grown dragon, fed, and bonded to the keeper.
        foreach (var egg in w.Query<Egg>())
            sim.DestroyThing(egg);
        var spot = sim.SpiralFrom(center + new int3(3, 3, 0), 6).First(c => sim.Map.Passable(c));
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Ember", spot, ageDays: 12.5f);
        Array.Fill(w.Get<Needs>(dragon).Values, 1f);
        EggSystem.AssignKeeper(sim, dragon, w.Query<Keeper>().First());

        var food = sim.Defs.Need("Food");
        var lowest = 1f;
        try
        {
            for (var hour = 0; hour < Days * 24; hour++)
            {
                SliceDemo.HuntNearbyGame(sim, center, radius: 60);
                sim.TickMany(SimClock.HoursToTicks(1));
                if (hour >= 48)
                    lowest = Math.Min(lowest, w.Get<Needs>(dragon).Get(food));
            }
        }
        finally
        {
            foreach (var m in sim.Messages.Messages)
                output.WriteLine($"[{SimClock.TicksToDays(m.Tick):F2}d] {m.Kind}: {m.Text}");
            output.WriteLine($"Animals killed: {sim.Wildlife.Hunted}, fish caught: {sim.Wildlife.FishCaught}, lowest dragon food after day 2: {lowest:F2}");
        }

        Assert.Equal("Adult", w.Get<Creature>(dragon).Stage(sim.Clock.Tick).Name);
        Assert.True(lowest > 0.15f, $"the dragon went hungry (food fell to {lowest:F2})");
        Assert.True(sim.Wildlife.Hunted > 0, "the hunter should have brought something down");
        foreach (var e in w.Query<Creature>().Where(e => w.Get<Creature>(e) is { Faction: Faction.Village, Dead: true }))
            Assert.Fail($"{w.Get<Creature>(e).Name} died");
    }
}
