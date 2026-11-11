using DragonWorld.Sim.Ai;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Maps;
using DragonWorld.Sim.Scenarios;
using DragonWorld.Sim.Systems;
using Xunit.Abstractions;

namespace DragonWorld.Sim.Tests;

/// <summary>
/// The vertical slice with preset buildings on (<see cref="SliceDemo.ApplyPresetOrders"/>): a
/// dwelling and a campfire for the egg first, then the longhouse, raised phase by phase, and the
/// village must still hatch the egg and raise a flying dragon. Same checks on the people and the
/// dragon as <see cref="SliceScenarioTests"/>.
/// </summary>
public class PresetSliceScenarioTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1234UL)]
    [InlineData(99UL)]
    public void VillageRaisesItsHousesHatchesTheEggAndRaisesAFlyingDragon(ulong seed)
    {
        var sim = Simulation.CreateStandard(TestWorld.LoadDefs(), 160, 160, seed, MapGenerator.RecommendedDepth);
        sim.Presets.PresetMode = true;
        var c = MapGenerator.Generate(sim);
        SliceDemo.ApplyVillageDefaults(sim, c);
        var home = SliceDemo.ApplyPresetOrders(sim, c);
        try
        {
            Play(sim, c, home);
        }
        finally
        {
            foreach (var m in sim.Messages.Messages)
                output.WriteLine($"[{SimClock.TicksToDays(m.Tick):F2}d] {m.Kind}: {m.Text}");
            foreach (var e in sim.Creatures().Where(e => sim.World.Has<Mood>(e)))
            {
                var mood = sim.World.Get<Mood>(e);
                output.WriteLine($"Mood of {sim.World.Get<Creature>(e).Name}: {mood.Value:F2} ({string.Join(", ", mood.Situational.Select(t => t.Label).Concat(mood.Memories.Select(m => m.Def.Label)))})");
            }
            foreach (var b in sim.World.Query<PresetBuilding>())
            {
                var p = sim.World.Get<PresetBuilding>(b);
                var prog = Buildings.Progress(sim, b, p.Phase < BuildPhase.Done ? p.Phase : BuildPhase.Furnish);
                output.WriteLine($"{p.Def.Name}: {p.Phase} (materials {prog.Materials:P0}, work {prog.Work:P0}, blocked {prog.Blocked})");
            }
            foreach (var e in sim.Creatures().Where(e => sim.World.Get<Creature>(e).Def.Race == Defs.Race.Human))
                output.WriteLine($"{sim.World.Get<Creature>(e).Name}: {sim.World.Get<Mind>(e).Status}");
        }
    }

    private void Play(Simulation sim, int3 center, SliceDemo.Homestead home)
    {
        var w = sim.World;
        Assert.False(home.Hatchery.IsNull, "the hatchery should fit near the landing");

        // Day 1: the hatchery is up round its fire, and the egg incubating in it.
        sim.TickMany(SimClock.DaysToTicks(1f));
        Assert.Equal(BuildPhase.Done, w.Get<PresetBuilding>(home.Hatchery).Phase);
        var egg = Assert.Single(w.Query<Egg>());
        Assert.True(w.Get<Egg>(egg).Progress > 0.2f, "egg should have been incubating");
        Assert.True(w.Query<Apparel>().Any(e => sim.Worn(e).Any()), "someone should have put on the starting jerkin");

        // With the egg safe, the player lays out the houses.
        home = SliceDemo.ApplyHomeOrders(sim, center, home);
        Assert.False(home.Dwelling.IsNull, "the dwelling should fit near the landing");
        Assert.False(home.Longhouse.IsNull, "the longhouse should fit near the landing");

        // Day 3: the egg has hatched and the dwelling is up; its spare beds go in.
        sim.TickMany(SimClock.DaysToTicks(2f));
        var dragon = Assert.Single(w.Query<Dragon>());
        Assert.False(w.Get<Creature>(dragon).Dead);
        Assert.True(w.IsAlive(w.Get<Dragon>(dragon).Keeper));
        Assert.Equal(BuildPhase.Done, w.Get<PresetBuilding>(home.Dwelling).Phase);
        SliceDemo.FillSpareBeds(sim, home.Dwelling);

        var juvenileAge = w.Get<Creature>(dragon).Def.LifeStages.First(st => st.CanFly).MinAgeDays;
        w.Get<Creature>(dragon).BornTick = sim.Clock.Tick - SimClock.DaysToTicks(juvenileAge - 0.05f);
        for (var hour = 0; hour < 18; hour++)
        {
            SliceDemo.HuntNearbyGame(sim, center);
            sim.TickMany(SimClock.HoursToTicks(1));
        }
        Assert.Equal("Juvenile", w.Get<Creature>(dragon).Stage(sim.Clock.Tick).Name);
        Assert.True(w.Get<Dragon>(dragon).Flights > 0, "the juvenile should have taken to the air");
        Assert.True(w.Get<Needs>(dragon).Get(sim.Defs.Need("Food")) > 0.15f, "keeper should keep the dragon fed");
        Assert.Equal(3, Buildings.Count(sim, home.Dwelling, sim.Defs.Thing("Bed")));
        Assert.True(w.Get<PresetBuilding>(home.Longhouse).Phase >= BuildPhase.Roof, "the longhouse should be well on its way");
        foreach (var e in sim.Creatures().Where(e => w.Has<Mood>(e)))
            Assert.True(w.Get<Mood>(e).Value >= 0.25f, $"{w.Get<Creature>(e).Name} is miserable ({w.Get<Mood>(e).Value:F2})");
        foreach (var e in w.Query<Creature>())
        {
            if (w.Get<Creature>(e).Def.Race == Defs.Race.Animal)
                continue;
            Assert.False(w.Get<Creature>(e).Dead, $"{w.Get<Creature>(e).Name} died");
            var at = w.Get<Position>(e).Cell;
            Assert.True(Navigation.CanReach(sim, e, at, center, PathEnd.Touch), $"{w.Get<Creature>(e).Name} is stuck at {at}");
        }
    }
}
