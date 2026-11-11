using DragonWorld.Sim.Combat;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Maps;
using static DragonWorld.Sim.Tests.TestWorld;

namespace DragonWorld.Sim.Tests;

/// <summary>Herds arriving, grazing, shying away from people and moving on (<see cref="Systems.WildlifeSystem"/>).</summary>
public class WildlifeTests
{
    private static Simulation WithVillager(out Entity villager, int width = 60, int height = 60, int3? at = null)
    {
        var sim = Create(width, height);
        villager = sim.SpawnCreature(sim.Defs.Creature("Human"), "Aldric", at ?? C(width / 2, height / 2));
        sim.Wildlife.Enable(sim);
        return sim;
    }

    [Fact]
    public void HerdsWanderInFromTheEdgeAndMoveOnLater()
    {
        var sim = WithVillager(out _);
        sim.TickMany(SimClock.HoursToTicks(sim.Defs.Wildlife.FirstArrivalHours * 1.3f) + SimClock.TicksPerHour);
        var w = sim.World;
        var herd = w.Query<Herd>();
        Assert.NotEmpty(herd);
        Assert.Equal(1, sim.Wildlife.HerdsArrived);
        Assert.All(herd, e => Assert.Equal(Faction.Wild, w.Get<Creature>(e).Faction));

        // Once their stay is over they walk off the map.
        foreach (var e in herd)
            w.Get<Herd>(e).LeaveTick = sim.Clock.Tick;
        sim.TickMany(SimClock.HoursToTicks(2));
        Assert.All(herd, e => Assert.False(w.IsAlive(e), "the herd should have left"));
    }

    [Theory]
    [InlineData(1234UL)]
    [InlineData(99UL)]
    public void ANewMapStartsWithDeerAmongItsHerds(ulong seed)
    {
        // A grown dragon hunting for itself needs game that won't fight back from the first day.
        var sim = Simulation.CreateStandard(LoadDefs(), 160, 160, seed, MapGenerator.RecommendedDepth);
        MapGenerator.Generate(sim);
        var w = sim.World;
        var herds = w.Query<Herd>().Select(e => w.Get<Herd>(e).Id).Distinct().Count();
        Assert.True(herds >= 3, $"only {herds} starting herds");
        Assert.Contains(w.Query<Herd>(), e => w.Get<Creature>(e).Def.Name == "Deer");
        // And something to bake into the dragon's other food: emberroot and berry bushes.
        Assert.True(w.Query<Thing>().Count(e => w.Get<Thing>(e).Def.Name == "EmberrootPlant") >= 10);
        Assert.True(w.Query<Thing>().Count(e => w.Get<Thing>(e).Def.Name == "BerryBush") >= 10);
    }

    [Fact]
    public void AnEmptiedValleyFillsUpSooner()
    {
        var sim = WithVillager(out _);
        var def = sim.Defs.Wildlife;
        Assert.True(def.MinAnimals > 0);
        sim.Wildlife.Enable(sim);
        // The first herd into an empty valley is too few: the next one is due sooner than usual.
        while (sim.Wildlife.HerdsArrived == 0)
            sim.Tick();
        Assert.True(sim.Wildlife.LiveAnimals(sim) < def.MinAnimals);
        var wait = (sim.Wildlife.NextArrivalTick - sim.Clock.Tick) / (float)SimClock.TicksPerHour;
        Assert.True(wait <= def.MeanHoursBetween * 1.5f * def.ScarceArrivalFactor + 0.5f, $"next herd in {wait:F1} h");
    }

    [Fact]
    public void ArrivalsComeFromTheirOwnRandomStream()
    {
        // Animals nobody goes near don't change the villager's day.
        static Simulation Run(bool herd)
        {
            var sim = Create(120, 40);
            var villager = sim.SpawnCreature(sim.Defs.Creature("Human"), "Aldric", C(5, 20));
            if (herd)
            {
                sim.Wildlife.Enable(sim);
                Assert.NotEmpty(sim.Wildlife.SendHerd(sim, sim.Defs.Creature("Deer")));
            }
            sim.TickMany(SimClock.HoursToTicks(3));
            return sim;
        }
        var with = Run(herd: true);
        var without = Run(herd: false);
        Assert.Equal(without.Rand.Fingerprint, with.Rand.Fingerprint);
    }

    [Fact]
    public void AnimalsShyAwayFromPeople()
    {
        var sim = WithVillager(out var villager, at: C(30, 30));
        sim.World.Get<Mind>(villager).Drafted = true; // stands still
        var deer = sim.SpawnCreature(sim.Defs.Creature("Deer"), "Deer", C(34, 30), Faction.Wild);
        sim.TickMany(SimClock.TicksPerSecond * 10);

        var gap = Map.Distance(sim.World.Get<Position>(deer).Cell, C(30, 30));
        Assert.True(gap >= 8, $"the deer should have run off, but it's {gap} cells away");
    }

    [Fact]
    public void GeneratedMapsStartWithHerdsAwayFromTheVillage()
    {
        var sim = Simulation.CreateStandard(LoadDefs(), 120, 120, 3, MapGenerator.RecommendedDepth);
        var center = MapGenerator.Generate(sim);
        var w = sim.World;
        var animals = w.Query<Herd>();
        Assert.NotEmpty(animals);
        Assert.All(animals, e => Assert.True(Map.Distance(w.Get<Position>(e).Cell, center) >= 20));
        Assert.True(sim.Wildlife.Enabled);
    }

    [Fact]
    public void TestMapsHaveNoWildlife()
    {
        var sim = WithVillager(out _);
        var plain = Create();
        plain.TickMany(SimClock.HoursToTicks(30));
        Assert.Empty(plain.World.Query<Herd>());
        Assert.False(plain.Wildlife.Enabled);
        Assert.True(sim.Wildlife.Enabled);
    }

    [Fact]
    public void LeavingHerdsDontAnnounceEachAnimal()
    {
        var sim = WithVillager(out _);
        var herd = sim.Wildlife.SendHerd(sim, sim.Defs.Creature("Deer"));
        Assert.True(herd.Count >= 2);
        foreach (var e in herd)
            sim.World.Get<Herd>(e).LeaveTick = sim.Clock.Tick;
        var before = sim.Messages.Messages.Count;
        sim.TickMany(SimClock.HoursToTicks(2));
        Assert.Equal(1, sim.Messages.Messages.Skip(before).Count(m => m.Text.Contains("moving on")));
        Assert.DoesNotContain(sim.Messages.Messages.Skip(before), m => m.Text.Contains("fled"));
        Assert.DoesNotContain(herd, e => sim.World.Has<Leaving>(e) && sim.World.IsAlive(e));
    }
}
