using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Maps;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Materials that matter: what a thing is made of changes its HP, weight, flammability, insulation and work.</summary>
public class MaterialTests
{
    private static Thing Made(Simulation sim, string def, string material) =>
        sim.World.Get<Thing>(sim.CreateItem(sim.Thing(def), 1, sim.Defs.Material(material)));

    [Fact]
    public void WoodsDiffer()
    {
        var sim = TestWorld.Create();
        var oak = Made(sim, "Wood", "Oak");
        var pine = Made(sim, "Wood", "Pine");
        var birch = Made(sim, "Wood", "Birch");
        Assert.True(oak.UnitMass > birch.UnitMass && birch.UnitMass > pine.UnitMass);
        Assert.True(oak.MaxHp > pine.MaxHp);
        Assert.True(pine.Flammability > oak.Flammability);
        Assert.Equal(sim.Thing("Wood").Mass, birch.UnitMass); // birch is the baseline
        Assert.Equal(oak.MaxHp, oak.Hp); // made at full strength for its material
    }

    [Fact]
    public void StoneTakesItsRocksName()
    {
        var sim = TestWorld.Create();
        Assert.Equal("granite blocks", Made(sim, "Stone", "Granite").Label);
        Assert.Equal("cut slate", Made(sim, "CutStone", "Slate").Label);
        var wall = sim.World.Get<Thing>(sim.SpawnStructure(sim.Thing("StoneWall"), C(5, 5))) with { Material = sim.Defs.Material("Granite") };
        Assert.Equal("granite wall", wall.Label);
        var cut = sim.World.Get<Thing>(sim.SpawnStructure(sim.Thing("CutStoneWall"), C(6, 5))) with { Material = sim.Defs.Material("Basalt") };
        Assert.Equal("cut basalt wall", cut.Label);
        Assert.Equal("birch wall", Made(sim, "WoodWall", "Birch").Label); // wood keeps its old rule
    }

    [Fact]
    public void AGraniteWallIsBuiltStrongerAndWarmerThanASandstoneOne()
    {
        var sim = TestWorld.Create();
        Entity Build(string material, int x)
        {
            var site = sim.PlaceBlueprint(sim.Thing("StoneWall"), C(x, 5));
            var c = sim.World.Get<Construction>(site);
            c.Delivered[0] = c.Target.Building!.Cost[0].Count;
            c.AddMaterial(sim.Defs.Material(material), c.Delivered[0]);
            return sim.CompleteConstruction(site);
        }
        var granite = sim.World.Get<Thing>(Build("Granite", 5));
        var sandstone = sim.World.Get<Thing>(Build("Sandstone", 7));
        Assert.Equal("granite wall", granite.Label);
        Assert.Equal(granite.MaxHp, granite.Hp);
        Assert.True(granite.MaxHp > sandstone.MaxHp * 1.5f);
        Assert.True(granite.Insulation > sandstone.Insulation);
        Assert.True(granite.Insulation < 0.99f);
    }

    [Fact]
    public void HeavierStuffFillsAPackSooner()
    {
        var sim = TestWorld.Create();
        var pawn = sim.SpawnCreature(sim.Defs.Creature("Human"), "P", C(10, 10), ageDays: 25 * 365);
        var pack = sim.Wear(pawn, sim.Thing("Satchel"));
        var oak = sim.ContainerRoom(pack, sim.Thing("Wood"), sim.Defs.Material("Oak"));
        var pine = sim.ContainerRoom(pack, sim.Thing("Wood"), sim.Defs.Material("Pine"));
        Assert.True(pine > oak, $"pine {pine} vs oak {oak}");
    }

    [Fact]
    public void PineBurnsFasterThanOak()
    {
        var sim = TestWorld.Create();
        float FuelOf(string material, int x)
        {
            sim.SpawnItem(sim.Thing("Wood"), 5, C(x, 5), sim.Defs.Material(material));
            return Environment.FireSystem.Fuel(sim, sim.Map.Idx(C(x, 5)));
        }
        Assert.True(FuelOf("Pine", 3) > FuelOf("Oak", 6));
    }
}
