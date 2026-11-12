using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Tests;

/// <summary>Steel (docs/plans/mining-progression.md): bellows and coal take a forge to iron and steel heat without a dragon.</summary>
public class SteelTests
{
    private static Simulation Warm()
    {
        var sim = TestWorld.Create(30, 30, depth: 5);
        sim.Climate.BaseTemperature = 15;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 15f);
        return sim;
    }

    private static (Entity Forge, Entity Bellows) ForgeWithBellows(Simulation sim)
    {
        var forge = sim.SpawnStructure(sim.Thing("Forge"), C(15, 15));
        var bellows = sim.SpawnStructure(sim.Thing("Bellows"), C(16, 15));
        sim.RebuildDerivedIfNeeded();
        return (forge, bellows);
    }

    /// <summary>Where the forge settles with its bellows pumped all along, on <paramref name="fuel"/>.</summary>
    private static float Blown(string fuel)
    {
        var sim = Warm();
        var (forge, _) = ForgeWithBellows(sim);
        sim.World.Get<Fueled>(forge).Loaded = sim.Thing(fuel);
        sim.ApplyNow(new Core.AddBillCommand(forge, sim.Defs.Recipe("SmeltIron"), BillMode.Forever, 0));
        for (var h = 0; h < 8; h++)
        {
            sim.World.Get<Fueled>(forge).Fuel = 20;
            Crafting.Pump(sim, forge);
            sim.World.Get<Blown>(forge).Until = sim.Clock.Tick + SimClock.TicksPerHour * 2;
            sim.TickMany(SimClock.TicksPerHour);
        }
        return sim.TemperatureAt(C(15, 15));
    }

    [Fact]
    public void BellowsLiftTheForgeByAboutTheirLift()
    {
        var defs = TestWorld.LoadDefs();
        var props = defs.Thing("Forge").Building!.Fuel!;
        var lift = defs.Thing("Bellows").Building!.Bellows!.Lift;
        var coal = Blown("Coal");
        Assert.InRange(coal, props.OptionFor(defs.Thing("Coal"))!.Reach + lift - 40, props.OptionFor(defs.Thing("Coal"))!.Reach + lift + 40);
        Assert.True(coal < 1300, "only a dragon's breath makes dragonforged heat");
        Assert.True(Blown("Charcoal") < 1100, "charcoal and bellows aren't enough for iron");
    }

    [Fact]
    public void ASmithWorkingTheBellowsSmeltsIronAndSteel()
    {
        var sim = Warm();
        var smith = sim.SpawnCreature(sim.Defs.Creature("Human"), "Smith", C(5, 5), ageDays: 25 * 365);
        sim.SetSkills(smith, 8);
        var work = sim.World.Get<Mind>(smith).WorkPriority;
        Array.Fill(work, 0);
        work[(int)WorkType.Craft] = 1;
        work[(int)WorkType.Refuel] = 1;
        var (forge, _) = ForgeWithBellows(sim);
        sim.World.Get<Fueled>(forge).Fuel = 0;
        sim.SpawnItem(sim.Thing("Coal"), 60, C(6, 9));
        sim.SpawnItem(sim.Thing("IronOre"), 8, C(7, 9));
        sim.SpawnItem(sim.Thing("Charcoal"), 8, C(8, 9));
        RecipeBooks.Discover(sim, smith, "handle:IronOre");
        RecipeBooks.Discover(sim, Entity.Null, "built:Bellows");
        sim.ApplyNow(new Core.AddBillCommand(forge, sim.Defs.Recipe("SmeltIron"), BillMode.Count, 2));
        sim.ApplyNow(new Core.AddBillCommand(forge, sim.Defs.Recipe("SmeltSteel"), BillMode.Count, 1));
        var pumped = false;
        for (var t = 0; t < SimClock.HoursToTicks(16); t += 60)
        {
            Array.Fill(sim.World.Get<Needs>(smith).Values, 1f);
            sim.TickMany(60);
            pumped |= sim.World.Get<Mind>(smith).Job?.Report.StartsWith("Working the bellows") == true;
        }
        int Count(string def) => sim.World.Query<Thing>().Where(e => sim.World.Get<Thing>(e).Def.Name == def)
            .Sum(e => sim.World.TryGet<Stack>(e, out var s) ? s.Count : 1);
        Assert.True(pumped, "the smith worked the bellows");
        Assert.True(Count("SteelBar") == 2, $"steel {Count("SteelBar")}, iron {Count("IronBar")}, forge {sim.TemperatureAt(C(15, 15)):F0}°C, "
            + string.Join(" | ", sim.Messages.Messages.TakeLast(5).Select(m => m.Text)));
        Assert.Equal(2, Count("IronBar"));
        Assert.Equal("Coal", sim.World.Get<Fueled>(forge).Loaded?.Name);
    }

    [Fact]
    public void WithoutBellowsTheForgeWontTryIron()
    {
        var sim = Warm();
        var forge = sim.SpawnStructure(sim.Thing("Forge"), C(15, 15));
        sim.World.Get<Fueled>(forge).Loaded = sim.Thing("Coal");
        Assert.False(Crafting.CanGetHotEnough(sim, forge, sim.Defs.Recipe("SmeltIron")));
        sim.SpawnStructure(sim.Thing("Bellows"), C(15, 16));
        Assert.True(Crafting.CanGetHotEnough(sim, forge, sim.Defs.Recipe("SmeltIron")));
    }

}
