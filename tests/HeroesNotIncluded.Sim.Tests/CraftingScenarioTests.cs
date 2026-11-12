using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using Xunit.Abstractions;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// The first crafting loop played out headless: villagers fell oaks, saw the logs into planks at
/// a sawhorse (a standing "until you have" bill) and build a plank-walled room, with nobody told
/// more than that. Read the log when it fails: a starved work type is the usual story.
/// </summary>
public class CraftingScenarioTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1UL)]
    [InlineData(1234UL)]
    public void VillagersFellSawAndBuildAPlankRoom(ulong seed)
    {
        var sim = TestWorld.Create(40, 40, seed);
        sim.Climate.BaseTemperature = 18;
        sim.Climate.DailySwing = 4;
        for (var i = 0; i < 3; i++)
        {
            var v = sim.SpawnCreature(sim.Defs.Creature("Human"), $"Villager{i}", C(5 + i, 5), ageDays: 25 * 365);
            var mind = sim.World.Get<Mind>(v);
            Array.Fill(mind.WorkPriority, 0);
            foreach (var w in new[] { WorkType.Chop, WorkType.Haul, WorkType.Craft, WorkType.Construct })
                mind.WorkPriority[(int)w] = 2;
        }
        sim.SpawnItem(sim.Thing("Berries"), 30, C(3, 3)); // enough to eat for a couple of days
        for (var x = 25; x < 33; x++)
        {
            sim.SpawnStructure(sim.Thing("Oak"), C(x, 30));
            sim.Map.Designate(sim.Map.Idx(C(x, 30)), Designation.Chop);
        }
        for (var y = 8; y < 11; y++)
            for (var x = 4; x < 9; x++)
                sim.Map.SetZone(sim.Map.Idx(C(x, y)), 1);
        var saw = sim.SpawnStructure(sim.Thing("Sawhorse"), C(12, 8));
        sim.RebuildDerivedIfNeeded();
        sim.ApplyNow(new AddBillCommand(saw, sim.Defs.Recipe("SawPlanks"), BillMode.UntilYouHave, 30));

        // A 4×4 ring: 12 walls, 72 planks, so the sawhorse has to keep up with the builders.
        var walls = new List<int3>();
        for (var x = 15; x <= 18; x++)
            for (var y = 14; y <= 17; y++)
                if (x is 15 or 18 || y is 14 or 17)
                {
                    walls.Add(C(x, y));
                    sim.PlaceBlueprint(sim.Thing("PlankWall"), C(x, y));
                }
        sim.RebuildDerivedIfNeeded();

        sim.TickMany(SimClock.DaysToTicks(2));

        foreach (var m in sim.Messages.Messages)
            output.WriteLine($"[{m.Tick / SimClock.TicksPerHour,3}h] {m.Text}");
        var built = walls.Select(c => sim.Map.Structure[sim.Map.Idx(c)])
            .Where(e => !e.IsNull && !sim.World.Has<Construction>(e) && sim.World.Get<Thing>(e).Def.Name == "PlankWall").ToList();
        Assert.True(built.Count == walls.Count, $"{built.Count}/{walls.Count} plank walls built");
        Assert.All(built, e => Assert.Equal("oak plank wall", sim.World.Get<Thing>(e).Label));
        Assert.True(sim.World.Get<Workbench>(saw).Bills.Count == 1, "the standing bill stays");
    }

    [Fact]
    public void APlankRoomHoldsHeatBetterThanALogOne()
    {
        float Inside(string wall)
        {
            var sim = TestWorld.Create(20, 20, depth: 3);
            sim.Climate.BaseTemperature = -5;
            sim.Climate.DailySwing = 0;
            Array.Fill(sim.Map.Temperature, -5f);
            sim.BuildWallRing(new int3(5, 5, 1), new int3(9, 9, 1), wall);
            for (var x = 5; x <= 9; x++)
                for (var y = 5; y <= 9; y++)
                    sim.SpawnStructure(sim.Thing("WoodFloor"), new int3(x, y, 2)); // a roof
            var fire = sim.SpawnStructure(sim.Thing("Campfire"), C(7, 7));
            sim.RebuildDerivedIfNeeded();
            for (var h = 0; h < 6; h++)
            {
                sim.World.Get<Fueled>(fire).Fuel = 20;
                sim.TickMany(SimClock.TicksPerHour);
            }
            return sim.TemperatureAt(C(6, 8));
        }
        var log = Inside("WoodWall");
        var plank = Inside("PlankWall");
        var stone = Inside("CutStoneWall");
        output.WriteLine($"log {log:F1}°C, plank {plank:F1}°C, cut stone {stone:F1}°C");
        Assert.True(plank > log + 0.5f, $"plank {plank:F1} vs log {log:F1}");
        Assert.True(stone > plank, $"cut stone {stone:F1} vs plank {plank:F1}");
    }
}
