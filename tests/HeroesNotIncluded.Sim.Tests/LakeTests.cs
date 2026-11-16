using Narsil.Sim.Grid;
using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Environment;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Building on the water, for a lake town on stilts: boardwalks on piles stand far from any bank, a
/// house without a foundation stands on them, buckets are let down from a dipping post, fire runs
/// along the planks, and a raid brings down the causeway (and only the causeway).
/// </summary>
public class LakeTests
{
    /// <summary>The level the decks are laid at: the shore's top, two levels of air over the water.</summary>
    private const int Deck = 3;

    /// <summary>A bank along the west edge (ground to z 2) and a lake beyond it (water at z 0).</summary>
    private static Simulation Lake()
    {
        var sim = Create(48, 32, depth: 8);
        sim.FillBlocks(new int3(0, 0, 1), new int3(5, 31, 2), "Dirt");
        sim.FillBlocks(new int3(6, 0, 0), new int3(47, 31, 0), "Water");
        return sim;
    }

    private static int3 D(int x, int y) => new(x, y, Deck);

    private static void Lay(Simulation sim, string def, int x0, int y0, int x1, int y1)
    {
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
            sim.SpawnStructure(sim.Thing(def), D(x, y));
        sim.RebuildDerivedIfNeeded();
    }

    [Fact]
    public void APiledDeckStandsFarFromTheBankAndAPlainFloorDoesnt()
    {
        var sim = Lake();
        Assert.False(sim.CanPlaceStructure(D(24, 12), sim.Thing("WoodFloor")), "a plain floor wants a bank or a wall in reach");
        Assert.True(sim.CanPlaceStructure(D(24, 12), sim.Thing("Boardwalk")), "piles reach the bed");
        Lay(sim, "Boardwalk", 24, 12, 24, 12);
        Assert.True(sim.Map.Standable(sim.Map.Idx(D(24, 12))));
        Assert.True(sim.Map.Passable(D(24, 12)));
        Assert.False(sim.Map.Passable(new int3(25, 12, 1)), "the deep water beside it isn't");
        // The causeway from the bank out to it can be walked.
        Lay(sim, "Causeway", 6, 12, 23, 12);
        Assert.True(Navigation.CanReach(sim, Entity.Null, new int3(3, 12, Deck), D(24, 12), PathEnd.OnTile));
    }

    [Fact]
    public void AHouseWithoutAFoundationStandsOnADeck()
    {
        var sim = Lake();
        var def = sim.Defs.Building("Cookhouse");
        var piled = def.Variants.FindIndex(v => v.Name == "Piled");
        Assert.True(piled >= 0);
        var anchor = D(24, 14);
        Assert.NotNull(Buildings.PlacementProblem(sim, def, anchor, 0, piled)); // no deck yet: open water
        Lay(sim, "Boardwalk", 16, 6, 32, 22);
        Assert.NotNull(Buildings.PlacementProblem(sim, def, anchor, 0, 0)); // one with a foundation won't go on a deck
        Assert.Null(Buildings.PlacementProblem(sim, def, anchor, 0, piled));
        var house = Buildings.SpawnFinished(sim, def, anchor, 0, piled);
        Assert.False(house.IsNull);
        sim.RebuildDerivedIfNeeded();
        var b = sim.World.Get<PresetBuilding>(house);
        var kitchen = Buildings.CellOf(sim.Map, def, b.Anchor, 0, 5, 3);
        var eating = Buildings.CellOf(sim.Map, def, b.Anchor, 0, 5, 7);
        Assert.True(sim.Rooms.RoomAt(kitchen) is { Indoors: true }, "the kitchen's walled and roofed over the water");
        Assert.NotEqual(sim.Rooms.RoomAt(kitchen), sim.Rooms.RoomAt(eating));
        Assert.False(Shops.Hatch.Of(sim, house).IsNull);
        Assert.False(Work.Cooking.PotIn(sim, house).IsNull);
    }

    [Fact]
    public void BucketsAreLetDownFromADippingPost()
    {
        var sim = Lake();
        Lay(sim, "Boardwalk", 20, 10, 24, 14);
        var post = sim.SpawnStructure(sim.Thing("DippingPost"), D(24, 12));
        Assert.True(Buckets.IsWell(sim, post));
        Assert.Contains(post, Buckets.Wells(sim));
        Buckets.HangAtWells(sim);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Ana", D(23, 12), ageDays: 24 * 365f);
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing(Buckets.Empty), 1);
        var action = Buckets.FillAt(sim, me, post, D(24, 12));
        Assert.NotNull(action);
        Assert.Equal(AvatarActionKind.FillBucket, action!.Value.Kind);
        Assert.True(Buckets.Fill(sim, me));
        Assert.True(Buckets.IsFull(sim, sim.World.Get<Carrying>(me).Item));
    }

    [Fact]
    public void FireRunsAlongThePlanks()
    {
        var sim = Lake();
        Lay(sim, "Boardwalk", 10, 12, 30, 12);
        Assert.True(sim.Fire.Ignite(sim, D(20, 12), 0.8f));
        sim.TickMany(SimClock.TicksPerHour * 2);
        var burning = Enumerable.Range(10, 21).Count(x => sim.Map.Fire[sim.Map.Idx(D(x, 12))] > 0
            || sim.Map.Floor[sim.Map.Idx(D(x, 12))].IsNull);
        Assert.True(burning > 1, "the fire's spread, or burned through, past the plank it started on");
    }

    [Fact]
    public void ARaidBringsDownTheCausewayNotTheTown()
    {
        var sim = Lake();
        Lay(sim, "Causeway", 6, 12, 19, 12);
        Lay(sim, "Boardwalk", 20, 8, 30, 16);
        var breach = sim.Repairs.BreakBridge(sim, sim.Rand);
        Assert.NotNull(breach);
        Assert.All(breach!.Cells, c => Assert.InRange(c.Cell.X, 6, 19));
        Assert.True(sim.Repairs.RoadCut);
        Assert.Equal("Causeway", breach.Def.Name);
        for (var y = 8; y <= 16; y++)
        for (var x = 20; x <= 30; x++)
            Assert.False(sim.Map.Floor[sim.Map.Idx(D(x, y))].IsNull, "the town's own decks stand");
        sim.RebuildDerivedIfNeeded();
        Assert.False(Navigation.CanReach(sim, Entity.Null, new int3(3, 12, Deck), D(25, 12), PathEnd.OnTile), "cut off");

        var tinker = sim.SpawnCreature(sim.Defs.Creature("Human"), "Gareth", new int3(3, 12, Deck), ageDays: 24 * 365f);
        sim.Repairs.Mended(sim, tinker, breach.Id, byHand: false);
        sim.RebuildDerivedIfNeeded();
        Assert.False(sim.Repairs.RoadCut);
        Assert.True(Navigation.CanReach(sim, Entity.Null, new int3(3, 12, Deck), D(25, 12), PathEnd.OnTile));
    }

    [Fact]
    public void DecksAndAHouseOnThemSurviveASave()
    {
        var sim = Lake();
        Lay(sim, "Boardwalk", 16, 6, 32, 22);
        var def = sim.Defs.Building("Cookhouse");
        Buildings.SpawnFinished(sim, def, D(24, 14), 0, def.Variants.FindIndex(v => v.Name == "Piled"));
        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), sim.Defs);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        Assert.True(loaded.Map.Standable(loaded.Map.Idx(D(16, 6))));
        Assert.False(Shops.ShopSystem.BuildingFor(loaded, Defs.TownRole.Kitchen).IsNull);
    }
}
