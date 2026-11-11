using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;
using Xunit.Abstractions;

namespace SupportingCast.Sim.Tests;

/// <summary>
/// The bottom of the tool ladder played out headless (docs/plans/mining-progression.md): a village
/// with stone tools and no dragon digs copper, bakes charcoal, smelts, forges a copper hammer and
/// pick, cuts through granite to the tin behind it, alloys bronze and forges a bronze pick. The
/// player only gives the orders (dig marks and bills); everything else is the villagers' own work.
/// </summary>
public class ProgressionScenarioTests(ITestOutputHelper output)
{
    /// <summary>How long the climb to bronze may take (game days): both seeds made it by day 0.46 when this was set.</summary>
    private const float DaysToBronze = 1f;

    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    public void AStoneAgeVillageWorksItsWayToABronzePick(ulong seed)
    {
        var sim = TestWorld.Create(48, 48, seed, depth: 6);
        sim.Climate.BaseTemperature = 18;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 18f);
        var map = sim.Map;

        // A limestone hill with a copper face showing on its west side.
        sim.FillBlocks(new int3(30, 8, 1), new int3(34, 15, 2), "Limestone");
        foreach (var y in new[] { 10, 11, 12, 13 })
            map.Block[map.Idx(new int3(30, y, 1))] = sim.Defs.Block("CopperOre");
        // A granite outcrop with tin sealed inside: a stone pick can't reach it.
        sim.FillBlocks(new int3(30, 24, 1), new int3(34, 30, 2), "Granite");
        var tin = new int3(31, 27, 1);
        map.Block[map.Idx(tin)] = sim.Defs.Block("TinOre");
        map.MarkStructureChanged();
        sim.RebuildDerivedIfNeeded();
        Assert.Equal("Granite", Mining.ShownBlock(sim, map.Idx(tin)).Name);

        var kiln = sim.SpawnStructure(sim.Thing("Kiln"), C(16, 18));
        var forge = sim.SpawnStructure(sim.Thing("Forge"), C(16, 26));
        sim.World.Get<Fueled>(kiln).Fuel = 0;
        sim.World.Get<Fueled>(forge).Fuel = 0;
        sim.SpawnItem(sim.Thing("Wood"), 60, C(8, 10));
        sim.SpawnItem(sim.Thing("Wood"), 60, C(8, 12));
        // A chest beside the forge for its tools: hauling leaves stored things alone.
        var chest = sim.SpawnStructure(sim.Thing("Chest"), C(15, 27));
        Assert.True(sim.Store(chest, sim.CreateItem(sim.Thing("StoneHammer"), 1)).IsNull);
        // A stockpile between the stations, where haulers bring what's dug.
        for (var y = 20; y <= 24; y++)
        for (var x = 18; x <= 22; x++)
            map.SetZone(map.Idx(C(x, y)), 1);
        sim.RebuildDerivedIfNeeded();

        string[] names = ["Aldric", "Brenna", "Corin"];
        for (var i = 0; i < names.Length; i++)
        {
            var e = sim.SpawnCreature(sim.Defs.Creature("Human"), names[i], C(10 + i, 20), ageDays: 25 * 365);
            sim.SetSkills(e);
            sim.GiveStartingPack(e);
            var work = sim.World.Get<Mind>(e).WorkPriority;
            Array.Fill(work, 0);
            work[(int)WorkType.Mine] = 1 + i % 2;
            work[(int)WorkType.Craft] = 1 + (i + 1) % 2;
            work[(int)WorkType.Refuel] = 1;
            work[(int)WorkType.Haul] = 3;
        }

        // The player's orders: dig the copper face and a way to the tin, and the bills.
        foreach (var y in new[] { 10, 11, 12, 13 })
            map.Designate(map.Idx(new int3(30, y, 1)), Designation.Dig);
        map.Designate(map.Idx(new int3(30, 27, 1)), Designation.Dig);
        map.Designate(map.Idx(tin), Designation.Dig);
        void Bill(Entity station, string recipe, BillMode mode, int target) =>
            sim.ApplyNow(new AddBillCommand(station, sim.Defs.Recipe(recipe), mode, target));
        Bill(kiln, "BakeCharcoal", BillMode.UntilYouHave, 10);
        Bill(kiln, "SmeltCopper", BillMode.Count, 4);
        Bill(kiln, "AlloyBronze", BillMode.Count, 1);
        Bill(forge, "ForgeCopperHammer", BillMode.Count, 1);
        Bill(forge, "ForgeCopperPick", BillMode.Count, 1);
        Bill(forge, "ForgeBronzePick", BillMode.Count, 1);

        var reached = float.PositiveInfinity;
        for (var t = 0; t < SimClock.DaysToTicks(DaysToBronze); t += SimClock.TicksPerHour)
        {
            foreach (var e in sim.World.Query<Needs>())
                Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
            sim.TickMany(SimClock.TicksPerHour);
            if (sim.World.Query<Thing>().Any(e => sim.World.Get<Thing>(e).Def.Name == "BronzePick"))
            {
                reached = sim.Clock.Tick / (float)SimClock.TicksPerDay;
                break;
            }
        }

        foreach (var m in sim.Messages.Messages)
            output.WriteLine($" [{m.Tick / (float)SimClock.TicksPerDay:F2}d] {m.Kind}: {m.Text}");
        int Count(string def) => sim.World.Query<Thing>().Where(e => sim.World.Get<Thing>(e).Def.Name == def)
            .Sum(e => sim.World.TryGet<Stack>(e, out var s) ? s.Count : 1);
        output.WriteLine($"Bronze pick on day {reached:F2}. Copper ore {Count("CopperOre")}, copper bars {Count("CopperBar")}, "
            + $"tin {Count("TinOre")}, bronze {Count("BronzeBar")}, charcoal {Count("Charcoal")}, copper picks {Count("CopperPick")}");
        Assert.True(reached < DaysToBronze, $"no bronze pick within {DaysToBronze} days");
        Assert.False(map.IsSolid(map.Idx(tin)), "the tin was dug out");
    }
}
