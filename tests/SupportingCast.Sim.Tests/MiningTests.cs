using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Tests;

/// <summary>The tool ladder on rock (docs/plans/mining-progression.md): picks gate what they break, better ones go faster.</summary>
public class MiningTests
{
    private static Simulation Warm(int w = 20, int h = 10)
    {
        var sim = TestWorld.Create(w, h, depth: 4);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    private static Entity Miner(Simulation sim, int3 at, string? pick)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), "Miner", at, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        sim.SetSkills(e);
        var mind = sim.World.Get<Mind>(e);
        Array.Fill(mind.WorkPriority, 0);
        mind.WorkPriority[(int)WorkType.Mine] = 1;
        if (pick != null)
            Assert.False(Toolbelt.PutOn(sim, e, sim.CreateItem(sim.Thing(pick), 1)).IsNull);
        return e;
    }

    /// <summary>A wall of <paramref name="block"/> across the map at x = 10, and one cell of it marked to dig.</summary>
    private static int Ridge(Simulation sim, string block)
    {
        sim.FillBlocks(new int3(10, 0, 1), new int3(10, sim.Map.Height - 1, 1), block);
        var rock = sim.Map.Idx(C(10, 5));
        sim.Map.Designate(rock, Designation.Dig);
        return rock;
    }

    [Fact]
    public void TiersAreNamedAndResolved()
    {
        var defs = TestWorld.LoadDefs();
        Assert.Equal(1, (int)defs.Thing("StonePick").Tool!.Tier);
        Assert.Equal(4, (int)defs.Thing("Pick").Tool!.Tier);
        Assert.Equal(6, (int)defs.Thing("DragonPick").Tool!.Tier);
        Assert.Equal(4, (int)defs.Recipe("ForgeSword").ToolTier);
        Assert.Equal(2, (int)defs.Block("Granite").DigTier);
        Assert.Equal(0, (int)defs.Block("Stone").DigTier);
        Assert.Equal("a copper pick", defs.ToolOfTier(2, "Pick"));
        Assert.Equal("an iron hammer", defs.ToolOfTier(4, "Hammer"));
        Assert.Equal("a pick", defs.ToolOfTier(0, "Pick"));

        var ex = Assert.ThrowsAny<Exception>(() => DefDatabase.FromJson(
            """{ "blocks": [ { "name": "Air", "solid": false }, { "name": "Glass", "digTier": "Mithril" } ] }"""));
        Assert.Contains("Mithril", ex.Message);
    }

    [Fact]
    public void AStonePickCantBreakGranite()
    {
        var sim = Warm();
        var miner = Miner(sim, C(9, 5), "StonePick");
        var rock = Ridge(sim, "Granite");
        Assert.Equal("needs a copper pick", Mining.BreakProblem(sim, miner, sim.Defs.Block("Granite")));
        sim.TickMany(SimClock.TicksPerHour);
        Assert.True(sim.Map.IsSolid(rock));
        Assert.True((sim.Map.Designations[rock] & Designation.Dig) != 0); // still marked, for a better pick
        Assert.IsNotType<DigJob>(sim.World.Get<Mind>(miner).Job);
    }

    [Fact]
    public void ABetterPickBreaksItAndGoesFasterTheHigherItIs()
    {
        var sim = Warm();
        var miner = Miner(sim, C(9, 5), "Pick"); // iron: two tiers above granite
        var granite = sim.Defs.Block("Granite");
        Assert.Equal(1.25f * (1f + 2 * Mining.BonusPerTier), Mining.DigFactor(sim, miner, granite), 3);
        var rock = Ridge(sim, "Granite");
        sim.TickMany(SimClock.TicksPerHour);
        Assert.False(sim.Map.IsSolid(rock));
        Assert.Contains(sim.SpawnedItems(), e => sim.World.Get<Thing>(e) is { Def.Name: "Stone", Material.Name: "Granite" });
    }

    [Fact]
    public void RoughStoneDigsAsItAlwaysDid()
    {
        var sim = Warm();
        var stone = Miner(sim, C(9, 5), "StonePick");
        var bare = Miner(sim, C(9, 6), null);
        var rough = sim.Defs.Block("Stone");
        Assert.Equal(0.75f, Mining.DigFactor(sim, stone, rough));
        Assert.Equal(Crafting.BareHands, Mining.DigFactor(sim, bare, rough));
        Assert.Null(Mining.BreakProblem(sim, bare, rough));
        Assert.Equal(1f, Mining.DigFactor(sim, bare, sim.Defs.Block("Dirt"))); // soil: any hands
        // Sandstone is soft: tier 0, so anyone may dig it.
        Assert.Null(Mining.BreakProblem(sim, bare, sim.Defs.Block("Sandstone")));
    }

    [Fact]
    public void OnlyPeopleNeedTheRightPick()
    {
        var sim = Warm();
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Ember", C(5, 5), ageDays: 14);
        Assert.Null(Mining.BreakProblem(sim, dragon, sim.Defs.Block("Basalt")));
    }

    [Fact]
    public void ADigOrderTooHardForTheVillageSaysSo()
    {
        var sim = Warm();
        Miner(sim, C(3, 5), "StonePick");
        sim.FillBlocks(new int3(10, 0, 1), new int3(12, 9, 1), "Granite");
        var before = sim.Messages.Messages.Count;
        sim.ApplyNow(new AreaOrderCommand(AreaTool.Dig, new int2(10, 4), new int2(11, 5), 1, 1));
        var said = sim.Messages.Messages.Skip(before).Select(m => m.Text).ToList();
        Assert.Contains("4 cells need a copper pick to dig.", said);
        Assert.True((sim.Map.Designations[sim.Map.Idx(C(10, 4))] & Designation.Dig) != 0);
    }

    [Fact]
    public void YouCantDigWhatYourPickWontBreak()
    {
        var sim = Warm();
        var me = Miner(sim, C(9, 5), "StonePick");
        sim.World.Add(me, new Avatar());
        sim.FillBlocks(new int3(10, 0, 1), new int3(10, 9, 1), "Granite");
        Assert.Equal(AvatarActionKind.None, AvatarSystem.ActionAt(sim, me, C(10, 5), AvatarTool.Dig).Kind);
        sim.FillBlocks(new int3(10, 0, 1), new int3(10, 9, 1), "Limestone");
        Assert.Equal(AvatarActionKind.Dig, AvatarSystem.ActionAt(sim, me, C(10, 5), AvatarTool.Dig).Kind);
    }

    [Theory]
    [InlineData(1234UL)]
    [InlineData(99UL)]
    public void GeneratedMapsHaveEveryRockAndLeaveTheLandingAlone(ulong seed)
    {
        var sim = Simulation.CreateStandard(TestWorld.LoadDefs(), 160, 160, seed, MapGenerator.RecommendedDepth);
        var center = MapGenerator.Generate(sim, new MapGenerator.Options { SpawnStartingKit = false, Wildlife = false });
        var counts = new Dictionary<string, int>();
        var rough = sim.Defs.Block("Stone");
        for (var i = 0; i < sim.Map.Count; i++)
        {
            var b = sim.Map.Block[i];
            if (!b.Solid || b.DigTool == null && b.DigTier <= 0 && b != sim.Defs.Block("Clay"))
                continue;
            counts[b.Name] = counts.GetValueOrDefault(b.Name) + 1;
            var c = sim.Map.CellOf(i);
            var near = MathF.Sqrt((c.X - center.X) * (c.X - center.X) + (c.Y - center.Y) * (c.Y - center.Y)) < 24;
            if (near)
                Assert.True(b == rough || b.Name is "Dirt" or "Sand" or "Grass", $"{b.Name} at {c}, by the landing");
        }
        foreach (var rock in new[] { "Sandstone", "Limestone", "Granite", "Slate", "Basalt", "IronOre", "Clay" })
            Assert.True(counts.GetValueOrDefault(rock) > 20, $"{rock}: {counts.GetValueOrDefault(rock)} on seed {seed}");
    }
}
