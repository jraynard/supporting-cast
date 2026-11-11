using DragonWorld.Sim.Components;
using DragonWorld.Sim.Maps;
using DragonWorld.Sim.Scenarios;
using Xunit.Abstractions;

namespace DragonWorld.Sim.Tests;

/// <summary>
/// The Pact's new home with nobody else around: can the player's grown dragon feed itself from the
/// valley's game alone? It eats about a deer a day (DragonHunting), so the starting herds and
/// arrivals (wildlife.json) have to keep up. The player does nothing here but stay fed themselves.
/// </summary>
public class PactFoodScenarioTests(ITestOutputHelper output)
{
    private const int Days = 4;

    [Theory]
    [InlineData(1234UL, null)]
    [InlineData(99UL, null)]
    [InlineData(1234UL, "NorseDragon")] // the heavier, hungrier breed
    [InlineData(99UL, "NorseDragon")]
    public void ALoneDragonHuntsEnoughToStayFed(ulong seed, string? breed)
    {
        var sim = Simulation.CreateStandard(TestWorld.LoadDefs(), 160, 160, seed, MapGenerator.RecommendedDepth);
        var me = AdventureStart.PactSite(sim, new Hero("Arin", null, null, "Vesper", null, DragonBreed: breed));
        var w = sim.World;
        var dragon = Companions.DragonOf(sim, me);
        Assert.False(dragon.IsNull);
        var food = sim.Defs.Need("Food");
        var lowest = 1f;
        try
        {
            for (var hour = 0; hour < Days * 24; hour++)
            {
                Array.Fill(w.Get<Needs>(me).Values, 1f); // the player looks after themselves
                sim.TickMany(SimClock.HoursToTicks(1));
                lowest = Math.Min(lowest, w.Get<Needs>(dragon).Get(food));
            }
        }
        finally
        {
            foreach (var m in sim.Messages.Messages)
                output.WriteLine($"[{SimClock.TicksToDays(m.Tick):F2}d] {m.Kind}: {m.Text}");
            output.WriteLine($"Animals killed: {sim.Wildlife.Hunted}, herds arrived: {sim.Wildlife.HerdsArrived}, lowest dragon food: {lowest:F2}");
        }
        Assert.False(w.Get<Creature>(dragon).Dead);
        Assert.True(lowest > food.UrgentBelow, $"the dragon went hungry (lowest food {lowest:F2})");
    }
}
