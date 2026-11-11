using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;

namespace SupportingCast.Sim.Tests;

public class JobTests
{
    private static Entity Villager(Simulation sim, int3 at, params WorkType[] only)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), "Worker", at);
        var needs = sim.World.Get<Needs>(e);
        Array.Fill(needs.Values, 1f); // not hungry or tired during the test
        if (only.Length > 0)
        {
            var mind = sim.World.Get<Mind>(e);
            Array.Fill(mind.WorkPriority, 0);
            foreach (var w in only)
                mind.WorkPriority[(int)w] = 1;
        }
        return e;
    }

    private static void Warm(Simulation sim)
    {
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
    }

    [Fact]
    public void HaulsLooseItemsIntoStockpile()
    {
        var sim = TestWorld.Create();
        Warm(sim);
        Villager(sim, C(5, 5), WorkType.Haul);
        sim.SpawnItem(sim.Thing("Wood"), 30, C(8, 8));
        for (var x = 20; x < 23; x++)
        for (var y = 20; y < 23; y++)
            sim.Map.SetZone(sim.Map.Idx(C(x, y)), 1);

        sim.TickMany(SimClock.TicksPerHour);

        var wood = sim.SpawnedItems().Single(e => sim.World.Get<Thing>(e).Def.Name == "Wood");
        var tile = sim.World.Get<Thing>(wood).Cell;
        Assert.Equal(1, sim.Map.Zone[sim.Map.Idx(tile)]);
        Assert.Equal(30, sim.World.Get<Stack>(wood).Count);
    }

    [Fact]
    public void AnItemNoStockpileTakesDoesNotHoldUpTheRest()
    {
        var sim = TestWorld.Create();
        Warm(sim);
        Villager(sim, C(5, 5), WorkType.Haul);
        // A one-cell stockpile with part of a stack of wood in it: room for wood, nothing else.
        var pile = C(20, 20);
        sim.Map.SetZone(sim.Map.Idx(pile), 1);
        sim.SpawnItem(sim.Thing("Wood"), 5, pile);
        sim.SpawnItem(sim.Thing("Wood"), 10, C(12, 12));
        sim.SpawnItem(sim.Thing("Stone"), 3, C(6, 5)); // nearer, but nowhere to put it

        sim.TickMany(SimClock.TicksPerHour / 2);

        Assert.Equal(15, sim.World.Get<Stack>(sim.Map.Item[sim.Map.Idx(pile)]).Count);
    }

    [Fact]
    public void BuildsWallFromBlueprintUsingDeliveredMaterials()
    {
        var sim = TestWorld.Create();
        Warm(sim);
        Villager(sim, C(5, 5), WorkType.Construct);
        sim.SpawnItem(sim.Thing("Wood"), 12, C(6, 10));
        var site = sim.PlaceBlueprint(sim.Thing("WoodWall"), C(12, 12));
        Assert.False(site.IsNull);

        sim.TickMany(SimClock.TicksPerHour);

        var built = sim.Map.Structure[sim.Map.Idx(C(12, 12))];
        Assert.False(built.IsNull);
        Assert.False(sim.World.Has<Construction>(built));
        Assert.Equal("WoodWall", sim.World.Get<Thing>(built).Def.Name);
        var leftover = sim.SpawnedItems().Where(e => sim.World.Get<Thing>(e).Def.Name == "Wood").Sum(e => sim.World.Get<Stack>(e).Count);
        Assert.Equal(7, leftover);
    }

    [Fact]
    public void ChopsDesignatedTreeForWood()
    {
        var sim = TestWorld.Create();
        Warm(sim);
        Villager(sim, C(5, 5), WorkType.Chop);
        var tree = sim.SpawnStructure(sim.Thing("Oak"), C(15, 15));
        sim.RebuildDerivedIfNeeded();
        sim.Map.SetDesignations(sim.Map.Idx(C(15, 15)), Designation.Chop);

        sim.TickMany(SimClock.TicksPerHour);

        Assert.False(sim.World.IsAlive(tree));
        Assert.Equal(12, sim.SpawnedItems().Where(e => sim.World.Get<Thing>(e).Def.Name == "Wood").Sum(e => sim.World.Get<Stack>(e).Count));
    }

    [Fact]
    public void ChoppingABirchGivesBirchWood()
    {
        var sim = TestWorld.Create();
        Warm(sim);
        Villager(sim, C(5, 5), WorkType.Chop);
        sim.SpawnStructure(sim.Thing("Birch"), C(15, 15));
        sim.RebuildDerivedIfNeeded();
        sim.Map.SetDesignations(sim.Map.Idx(C(15, 15)), Designation.Chop);

        sim.TickMany(SimClock.TicksPerHour);

        var wood = sim.SpawnedItems().Where(e => sim.World.Get<Thing>(e).Def.Name == "Wood").ToList();
        Assert.Equal(12, wood.Sum(e => sim.World.Get<Stack>(e).Count));
        Assert.All(wood, e => Assert.Equal("Birch", sim.World.Get<Thing>(e).Material?.Name));
        Assert.Equal("birch wood", sim.World.Get<Thing>(wood[0]).Label);
    }

    [Fact]
    public void DifferentWoodsStackSeparately()
    {
        var sim = TestWorld.Create();
        sim.SpawnItem(sim.Thing("Wood"), 10, C(5, 5), sim.Defs.Material("Oak"));
        sim.SpawnItem(sim.Thing("Wood"), 10, C(5, 5), sim.Defs.Material("Birch"));
        sim.SpawnItem(sim.Thing("Wood"), 5, C(5, 5), sim.Defs.Material("Oak"));

        var stacks = sim.SpawnedItems().Select(e => sim.World.Get<Thing>(e)).ToList();
        Assert.Equal(2, stacks.Count);
        Assert.Equal(15, sim.SpawnedItems().Where(e => sim.World.Get<Thing>(e).Material?.Name == "Oak").Sum(e => sim.World.Get<Stack>(e).Count));
    }

    [Fact]
    public void WallsAreMadeOfTheWoodDeliveredAndRefundIt()
    {
        var sim = TestWorld.Create();
        Warm(sim);
        Villager(sim, C(5, 5), WorkType.Construct);
        sim.SpawnItem(sim.Thing("Wood"), 5, C(6, 6), sim.Defs.Material("Pine"));
        var site = sim.PlaceBlueprint(sim.Thing("WoodWall"), C(12, 12));
        sim.RebuildDerivedIfNeeded();

        sim.TickMany(SimClock.TicksPerHour);

        var wall = sim.Map.Structure[sim.Map.Idx(C(12, 12))];
        Assert.False(sim.World.IsAlive(site));
        Assert.Equal("Pine", sim.World.Get<Thing>(wall).Material?.Name);
        Assert.Equal("pine wall", sim.World.Get<Thing>(wall).Label);

        // Mixed deliveries: the building is made of whichever wood made up most of it.
        var mixed = sim.PlaceBlueprint(sim.Thing("WoodWall"), C(20, 20));
        var c = sim.World.Get<Construction>(mixed);
        c.AddMaterial(sim.Defs.Material("Oak"), 2);
        c.AddMaterial(sim.Defs.Material("Birch"), 3);
        c.Delivered[0] = 5;
        var built = sim.CompleteConstruction(mixed);
        Assert.Equal("Birch", sim.World.Get<Thing>(built).Material?.Name);

        sim.Deconstruct(built);
        var refund = sim.SpawnedItems().Where(e => sim.World.Get<Thing>(e).Cell.X >= 18).ToList();
        Assert.Equal(3, refund.Sum(e => sim.World.Get<Stack>(e).Count));
        Assert.All(refund, e => Assert.Equal("Birch", sim.World.Get<Thing>(e).Material?.Name));
    }

    [Fact]
    public void BurnedTreeLeavesADeadTreeWithALittleWood()
    {
        var sim = TestWorld.Create();
        Warm(sim);
        var tree = sim.SpawnStructure(sim.Thing("Oak"), C(15, 15));
        sim.RebuildDerivedIfNeeded();
        sim.Map.SetDesignations(sim.Map.Idx(C(15, 15)), Designation.Chop);
        sim.Fire.Ignite(sim, C(15, 15), 1f);
        for (var t = 0; t < SimClock.HoursToTicks(6) && sim.World.IsAlive(tree); t += 60)
            sim.TickMany(60);
        Assert.False(sim.World.IsAlive(tree));
        var dead = sim.Map.Structure[sim.Map.Idx(C(15, 15))];
        Assert.False(dead.IsNull, "the tree should leave a dead tree behind");
        Assert.Equal("DeadTree", sim.World.Get<Thing>(dead).Def.Name);

        sim.Fire.ExtinguishAll(sim);
        Villager(sim, C(5, 5), WorkType.Chop);
        sim.TickMany(SimClock.TicksPerHour);

        Assert.False(sim.World.IsAlive(dead));
        Assert.Equal(5, sim.SpawnedItems().Where(e => sim.World.Get<Thing>(e).Def.Name == "Wood").Sum(e => sim.World.Get<Stack>(e).Count));
    }

    [Fact]
    public void HungryVillagerEats()
    {
        var sim = TestWorld.Create();
        Warm(sim);
        var pawn = Villager(sim, C(5, 5));
        var food = sim.Defs.Need("Food");
        sim.World.Get<Needs>(pawn).Values[food.Index] = 0.1f;
        sim.SpawnItem(sim.Thing("Berries"), 20, C(10, 10));

        sim.TickMany(SimClock.TicksPerHour / 2);

        Assert.True(sim.World.Get<Needs>(pawn).Get(food) > 0.8f);
    }

    [Fact]
    public void VillagerCarriesColdEggToTheFire()
    {
        var sim = TestWorld.Create();
        sim.Climate.BaseTemperature = 5;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 5f);
        sim.BuildWallRing(C(10, 10), C(16, 16), door: C(13, 16));
        var fire = sim.SpawnStructure(sim.Thing("Campfire"), C(13, 13));
        sim.World.Get<Fueled>(fire).Fuel = 1000;
        sim.TickMany(SimClock.TicksPerHour * 3); // let the room heat up

        var egg = sim.CreateItem(sim.Thing("DragonEgg"), 1);
        sim.PlaceItemNear(egg, C(3, 3));
        Villager(sim, C(4, 4), WorkType.Tend);

        sim.TickMany(SimClock.TicksPerHour);

        var eggTile = sim.World.Get<Thing>(egg).Cell;
        var temp = sim.TemperatureAt(eggTile);
        Assert.True(temp >= 28f, $"egg should be somewhere warm, is at {eggTile} with {temp:F1}°C");
    }

    [Fact]
    public void DigsATunnelThroughARockRidge()
    {
        var sim = TestWorld.Create(20, 10, depth: 4);
        Warm(sim);
        sim.FillBlocks(new int3(10, 0, 1), new int3(10, 9, 2)); // a ridge two levels high, wall to wall
        Systems.Toolbelt.GiveStartingTools(sim, Villager(sim, C(3, 5), WorkType.Mine)); // rock wants a pick
        Assert.False(sim.Regions.CanReach(C(3, 5), C(15, 5), PathEnd.OnTile));

        var rock = C(10, 5);
        Assert.True(sim.CanDig(rock));
        sim.Map.Designate(sim.Map.Idx(rock), Designation.Dig);
        sim.TickMany(SimClock.TicksPerHour);

        Assert.False(sim.Map.IsSolid(sim.Map.Idx(rock)));
        Assert.True(sim.Regions.CanReach(C(3, 5), C(15, 5), PathEnd.OnTile));
        Assert.Contains(sim.SpawnedItems(), e => sim.World.Get<Thing>(e).Def.Name == "Stone");
        // The level above is still solid: it's a tunnel, not a cutting.
        Assert.True(sim.Map.IsSolid(sim.Map.Idx(new int3(10, 5, 2))));
    }

    [Fact]
    public void CreatureFallsWhenTheGroundIsDugAway()
    {
        var sim = TestWorld.Create(10, 10, depth: 4);
        Warm(sim);
        sim.FillBlocks(new int3(4, 4, 1), new int3(6, 6, 2)); // a small mesa, standing at z = 3
        var pawn = Villager(sim, new int3(5, 5, 3));
        sim.World.Get<Mind>(pawn).Drafted = true;

        sim.DigOut(new int3(5, 5, 2));
        sim.DigOut(new int3(5, 5, 1));
        sim.RebuildDerivedIfNeeded();
        sim.Tick();

        Assert.Equal(C(5, 5), sim.World.Get<Position>(pawn).Cell);
        var injury = Assert.Single(sim.World.Get<Health>(pawn).Injuries); // a two-level fall should hurt
        Assert.Equal(DamageType.Blunt, injury.Type);
        Assert.Equal(PartHeight.Low, sim.World.Get<Health>(pawn).Body.Parts[injury.Part].Height);
    }

    private sealed class ThrowingJob : Job
    {
        protected override IEnumerable<JobStatus> Run()
        {
            yield return JobStatus.Running;
            throw new InvalidOperationException("boom");
        }
    }

    /// <summary>With StrictJobs off (the game), a job that throws fails by itself and the pawn carries on.</summary>
    [Fact]
    public void CrashingJobFailsWithoutStoppingTheSim()
    {
        var sim = Create();
        sim.StrictJobs = false;
        var pawn = sim.SpawnCreature(sim.Defs.Creature("Human"), "Ann", C(5, 5));
        Systems.AiSystem.Order(sim, pawn, new ThrowingJob());
        sim.TickMany(10);
        Assert.IsNotType<ThrowingJob>(sim.World.Get<Mind>(pawn).Job);
        Assert.Contains("crashed: InvalidOperationException: boom", sim.Ai.LastFailure[pawn]);
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("ThrowingJob failed unexpectedly"));
    }

    /// <summary>Tests and the bench run strict, so job bugs still fail loudly there.</summary>
    [Fact]
    public void CrashingJobThrowsWhenStrict()
    {
        var sim = Create();
        var pawn = sim.SpawnCreature(sim.Defs.Creature("Human"), "Ann", C(5, 5));
        Systems.AiSystem.Order(sim, pawn, new ThrowingJob());
        Assert.Throws<InvalidOperationException>(() => sim.TickMany(10));
    }
}
