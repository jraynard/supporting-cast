using DragonWorld.Sim.Components;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Saving;

namespace DragonWorld.Sim.Tests;

/// <summary>Packs: worn containers that carry items by weight (<see cref="ContainerProps"/>).</summary>
public class PackTests
{
    private static (Simulation Sim, Entity Pawn, Entity Pack) Setup()
    {
        var sim = TestWorld.Create(30, 20, 1, 4);
        var pawn = sim.SpawnCreature(sim.Defs.Creature("Human"), "Ada", C(10, 10), ageDays: 25 * 365);
        sim.World.Add(pawn, new Avatar()); // the player's own pack: companions' packs only hold a job's load
        var pack = sim.Wear(pawn, sim.Thing("Satchel"));
        return (sim, pawn, pack);
    }

    private static Entity Wood(Simulation sim, int count) => sim.CreateItem(sim.Thing("Wood"), count, sim.Defs.Material("Birch"));

    [Fact]
    public void ItemsNeedAMass()
    {
        var ex = Assert.ThrowsAny<Exception>(() => DefDatabase.FromJson(
            """{ "things": [ { "name": "Feather", "category": "Item" } ] }"""));
        Assert.Contains("mass", ex.Message);
        Assert.All(TestWorld.LoadDefs().AllThings.Where(t => t.Category == ThingCategory.Item), t => Assert.True(t.Mass > 0, t.Name));
    }

    [Fact]
    public void APackCarriesByWeight()
    {
        var (sim, pawn, pack) = Setup();
        Assert.Equal(pack, sim.PackOf(pawn));
        Assert.Equal(25, sim.ContainerRoom(pack, sim.Thing("Wood"), sim.Defs.Material("Birch"))); // 1 kg each

        // 40 wood: 25 go in, the rest comes back.
        var rest = sim.Store(pack, Wood(sim, 40));
        Assert.Equal(15, sim.World.Get<Stack>(rest).Count);
        Assert.Equal(25f, sim.LoadOf(pack), 3);
        Assert.Equal(26f, sim.MassOf(pack), 3); // its own kilo too
        Assert.Equal(rest, sim.Store(pack, rest)); // full: nothing taken
        Assert.Equal(0, sim.ContainerRoom(pack, sim.Thing("Berries"), null));
    }

    [Fact]
    public void StacksMergeAndSpareWeightTakesOtherKinds()
    {
        var (sim, _, pack) = Setup();
        Assert.True(sim.Store(pack, Wood(sim, 5)).IsNull);
        Assert.True(sim.Store(pack, Wood(sim, 5)).IsNull);
        Assert.True(sim.Store(pack, sim.CreateItem(sim.Thing("Stone"), 5)).IsNull); // 2 kg each
        var held = sim.StoredIn(pack);
        Assert.Equal(2, held.Count);
        Assert.Equal(10, sim.World.Get<Stack>(held[0]).Count);
        Assert.Equal(5, sim.ContainerRoom(pack, sim.Thing("Wood"), sim.Defs.Material("Birch")));
    }

    [Fact]
    public void BulkyThingsAndPacksStayOut()
    {
        var (sim, _, pack) = Setup();
        Assert.Equal(0, sim.ContainerRoom(pack, sim.Thing("DragonEgg"), null));
        Assert.Equal(0, sim.ContainerRoom(pack, sim.Thing("Satchel"), null));
    }

    [Fact]
    public void WhatsInAPackIsntUpForGrabs()
    {
        var (sim, pawn, pack) = Setup();
        var wood = Wood(sim, 5);
        sim.Store(pack, wood);
        Assert.False(sim.IsAvailable(wood));
        Assert.DoesNotContain(wood, sim.AvailableItems());
        Assert.Equal(C(10, 10), sim.LocationOf(wood));
        Assert.Equal(C(10, 10), sim.LocationOf(pack));

        // A wagon's contents still are.
        var wagon = sim.SpawnStructure(sim.Thing("Wagon"), C(5, 5));
        var stone = sim.CreateItem(sim.Thing("Stone"), 5);
        sim.Store(wagon, stone);
        Assert.True(sim.IsAvailable(stone));
    }

    [Fact]
    public void FoodRotsInAPack()
    {
        var (sim, _, pack) = Setup();
        Array.Fill(sim.Map.Temperature, 20f);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        var meat = sim.CreateItem(sim.Thing("Meat"), 5);
        sim.Store(pack, meat);
        sim.TickMany(SimClock.TicksPerDay);
        Assert.True(sim.World.Get<Rot>(meat).Progress > 0.1f);
    }

    [Fact]
    public void ADroppedPackKeepsItsContentsAndABurnedOneSpillsThem()
    {
        var (sim, pawn, pack) = Setup();
        var wood = Wood(sim, 5);
        sim.Store(pack, wood);
        sim.TakeOff(pawn, ApparelSlot.Back);
        Assert.True(sim.World.Get<Thing>(pack).Spawned);
        Assert.Equal([wood], sim.StoredIn(pack));
        Assert.Equal(sim.World.Get<Thing>(pack).Cell, sim.LocationOf(wood));

        sim.DestroyThing(pack);
        Assert.True(sim.World.Get<Thing>(wood).Spawned);
        Assert.False(sim.World.Has<Stored>(wood));
    }

    [Fact]
    public void AFullPackSurvivesASave()
    {
        var (sim, pawn, pack) = Setup();
        sim.Store(pack, Wood(sim, 12));
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var w = loaded.World;
        var back = w.Get<Apparel>(pawn).Back;
        Assert.Equal("Satchel", w.Get<Thing>(back).Def.Name);
        Assert.Equal(12f, loaded.LoadOf(back), 3);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
    }
}
