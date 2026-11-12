using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Ore (docs/plans/mining-progression.md): hidden until a face of it is opened, a chance of it in
/// broken boulders, more of it for a skilled miner, and the kiln's copper and bronze, fired with coal.
/// </summary>
public class OreTests
{
    private static Simulation Warm()
    {
        var sim = TestWorld.Create(30, 30, depth: 5);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    private static Entity Villager(Simulation sim, int3 at, int skill = Skills.Average, params WorkType[] only)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), "Smith", at, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        sim.SetSkills(e, skill);
        var mind = sim.World.Get<Mind>(e);
        Array.Fill(mind.WorkPriority, 0);
        foreach (var w in only)
            mind.WorkPriority[(int)w] = 1;
        return e;
    }

    private static int Count(Simulation sim, string def) =>
        sim.World.Query<Thing>().Where(e => sim.World.Get<Thing>(e).Def.Name == def)
            .Sum(e => sim.World.TryGet<Stack>(e, out var s) ? s.Count : 1);

    private static void TickFed(Simulation sim, int ticks)
    {
        for (var t = 0; t < ticks; t += 60)
        {
            foreach (var e in sim.World.Query<Needs>())
                Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
            sim.TickMany(60);
        }
    }

    [Fact]
    public void OreHidesInTheRockUntilAFaceIsOpened()
    {
        var sim = Warm();
        var map = sim.Map;
        sim.FillBlocks(new int3(8, 3, 1), new int3(12, 7, 3), "Granite");
        var tin = new int3(10, 5, 2);
        map.Block[map.Idx(tin)] = sim.Defs.Block("TinOre");
        Assert.False(Mining.Exposed(map, map.Idx(tin)));
        Assert.Equal("Granite", Mining.ShownBlock(sim, map.Idx(tin)).Name);
        Assert.Null(Mining.DigNote(sim, map.Idx(tin), null) is { } note && note.Contains("tin") ? note : null);

        var miner = Villager(sim, C(2, 2));
        var before = sim.Messages.Messages.Count;
        sim.DigOut(tin + new int3(0, 0, 1), miner); // open the face above it
        Assert.True(Mining.Exposed(map, map.Idx(tin)));
        Assert.Equal("TinOre", Mining.ShownBlock(sim, map.Idx(tin)).Name);
        Assert.Contains(sim.Messages.Messages.Skip(before), m => m.Text == "Smith uncovered tin ore!");
    }

    [Fact]
    public void OreWithNothingButOreAroundItLooksLikeItsHostRock()
    {
        var sim = Warm();
        var map = sim.Map;
        sim.FillBlocks(new int3(8, 3, 1), new int3(12, 7, 3), "CopperOre");
        Assert.Equal("Limestone", Mining.ShownBlock(sim, map.Idx(new int3(10, 5, 2))).Name);
        Assert.Equal("CopperOre", Mining.ShownBlock(sim, map.Idx(new int3(10, 5, 3))).Name); // open sky above
    }

    [Fact]
    public void BouldersSometimesHoldOreWithoutDrawingOnTheSimsDice()
    {
        var harvest = TestWorld.LoadDefs().Thing("Boulder").Harvest!;
        Dictionary<string, int> Roll()
        {
            var sim = Warm();
            var rand = sim.Rand.Fingerprint;
            for (var y = 0; y < 30; y++)
            for (var x = 0; x < 30; x++)
                Mining.RollBonus(sim, harvest, new int3(x, y, 1), Entity.Null);
            Assert.Equal(rand, sim.Rand.Fingerprint);
            return harvest.Bonus.ToDictionary(b => b.Def, b => Count(sim, b.Def));
        }
        var first = Roll();
        Assert.Equal(first, Roll()); // the same boulders, the same finds
        // 900 boulders: copper at 6% should turn up about 54 times (two ore each).
        Assert.InRange(first["CopperOre"] / 2, 30, 80);
        Assert.InRange(first["TinOre"] / 2, 10, 45);
    }

    [Fact]
    public void ASkilledMinerGetsMoreOreButNoMoreStone()
    {
        var sim = Warm();
        var novice = Villager(sim, C(2, 2), 0);
        var master = Villager(sim, C(3, 2), 15);
        var copper = sim.Defs.Block("CopperOre");
        Assert.Equal(3, Mining.OreYield(sim, novice, copper, sim.Thing("CopperOre"), 4));
        Assert.Equal(5, Mining.OreYield(sim, master, copper, sim.Thing("CopperOre"), 4));
        Assert.Equal(1, Mining.OreYield(sim, master, copper, sim.Thing("Stone"), 1));
        Assert.Equal(8, Mining.OreYield(sim, master, sim.Defs.Block("Granite"), sim.Thing("Stone"), 8));
    }

    private static Entity Station(Simulation sim, string station)
    {
        var e = sim.SpawnStructure(sim.Thing(station), C(15, 15));
        sim.RebuildDerivedIfNeeded();
        return e;
    }

    [Fact]
    public void TheKilnSmeltsCopperOnWoodAndFetchesCharcoalForBronze()
    {
        var sim = Warm();
        var smith = Villager(sim, C(5, 5), only: [WorkType.Craft, WorkType.Refuel]);
        var kiln = Station(sim, "Kiln");
        sim.World.Get<Fueled>(kiln).Fuel = 0;
        sim.SpawnItem(sim.Thing("Wood"), 40, C(6, 9));
        sim.SpawnItem(sim.Thing("CopperOre"), 12, C(7, 9));
        sim.SpawnItem(sim.Thing("TinOre"), 2, C(8, 9));
        sim.SpawnItem(sim.Thing("Charcoal"), 30, C(9, 9));
        RecipeBooks.Discover(sim, smith, "handle:CopperOre");
        RecipeBooks.Discover(sim, smith, "handle:TinOre");
        sim.ApplyNow(new Core.AddBillCommand(kiln, sim.Defs.Recipe("SmeltCopper"), BillMode.Count, 3));
        TickFed(sim, SimClock.HoursToTicks(10));
        Assert.True(Count(sim, "CopperBar") == 6, $"copper {Count(sim, "CopperBar")}, kiln {sim.TemperatureAt(C(15, 15)):F0}°C, fuel {sim.World.Get<Fueled>(kiln).Fuel:F1} {sim.World.Get<Fueled>(kiln).Loaded}, smith: {sim.World.Get<Mind>(smith).Job?.Report}, "
            + string.Join(" | ", sim.Messages.Messages.TakeLast(6).Select(m => m.Text)));
        Assert.Equal("Wood", sim.World.Get<Fueled>(kiln).Loaded?.Name); // wood was hot enough for copper

        sim.ApplyNow(new Core.AddBillCommand(kiln, sim.Defs.Recipe("AlloyBronze"), BillMode.Count, 1));
        TickFed(sim, SimClock.HoursToTicks(10));
        Assert.True(Count(sim, "BronzeBar") == 4,
            $"bronze {Count(sim, "BronzeBar")}, copper {Count(sim, "CopperBar")}, kiln {sim.TemperatureAt(C(15, 15)):F0}°C, "
            + string.Join(" | ", sim.Messages.Messages.TakeLast(6).Select(m => m.Text)));
        Assert.Equal(3, Count(sim, "CopperBar"));
        Assert.Equal("Charcoal", sim.World.Get<Fueled>(kiln).Loaded?.Name); // bronze wants more than a wood fire
    }

    [Theory]
    [InlineData("Kiln", null)]
    [InlineData("Kiln", "Charcoal")]
    [InlineData("Kiln", "Coal")]
    [InlineData("Forge", null)]
    [InlineData("Forge", "Coal")]
    public void FuelReachMatchesWhatTheStationHolds(string station, string? fuel)
    {
        var sim = Warm();
        sim.Climate.BaseTemperature = 15;
        Array.Fill(sim.Map.Temperature, 15f);
        var e = Station(sim, station);
        var props = sim.Thing(station).Building!.Fuel!;
        ref var fueled = ref sim.World.Get<Fueled>(e);
        fueled.Loaded = fuel != null ? sim.Thing(fuel) : null;
        sim.ApplyNow(new Core.AddBillCommand(e, sim.Defs.Recipe(station == "Kiln" ? "BakeCharcoal" : "SmeltIron"), BillMode.Forever, 0));
        for (var h = 0; h < 8; h++)
        {
            sim.World.Get<Fueled>(e).Fuel = props.Capacity;
            sim.TickMany(SimClock.TicksPerHour);
        }
        var reach = sim.World.Get<Fueled>(e).Burn(props).Reach;
        Assert.InRange(sim.TemperatureAt(C(15, 15)), reach - 40, reach + 40);
    }

    [Fact]
    public void TheLoadedFuelSurvivesASave()
    {
        var sim = Warm();
        var kiln = sim.SpawnStructure(sim.Thing("Kiln"), C(15, 15));
        sim.World.Get<Fueled>(kiln).Loaded = sim.Thing("Coal");
        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), sim.Defs);
        Assert.Equal("Coal", loaded.World.Get<Fueled>(kiln).Loaded?.Name);
    }
}
