using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Environment;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Tests;

public class BreathTests
{
    private static readonly BreathShape Juvenile = new(7f, 22f, 10, 0.9f, 1.5f);

    private static Simulation Field()
    {
        var sim = TestWorld.Create(30, 20, depth: 6);
        var dirt = sim.Defs.Block("Dirt"); // bare ground: only what we place can burn
        Array.Fill(sim.Map.Block, dirt, 0, sim.Map.LayerSize);
        sim.Map.MarkStructureChanged();
        sim.RebuildDerivedIfNeeded();
        return sim;
    }

    private static float3 Mouth(int x, int y) => new(x + 0.5f, y + 0.5f, 1.8f);

    [Fact]
    public void BreathIntoATreeClusterSetsItAlightButNotTheRocks()
    {
        var sim = Field();
        for (var y = 8; y <= 12; y++)
        for (var x = 10; x <= 12; x++)
            sim.SpawnStructure(sim.Thing((x + y) % 4 == 0 ? "Boulder" : "Oak"), C(x, y));
        sim.RebuildDerivedIfNeeded();
        var boulders = sim.World.Query<Thing>().Where(e => sim.World.Get<Thing>(e).Def.Name == "Boulder").ToList();

        var ev = Breath.Breathe(sim, Mouth(5, 10), new float3(11.5f, 10.5f, 1.3f), Juvenile);
        Assert.True(ev.Ignited >= 3, $"only {ev.Ignited} cells caught");
        var burningTrees = sim.Fire.BurningTiles.Count(i => sim.Map.Structure[i] is { IsNull: false } s && sim.World.Get<Thing>(s).Def.Name == "Oak");
        Assert.True(burningTrees >= 3, $"{burningTrees} trees burning");

        sim.TickMany(SimClock.HoursToTicks(4));
        Assert.All(boulders, b => Assert.True(sim.World.IsAlive(b)));
    }

    [Fact]
    public void SneezesScorchTheGroundButSpareBuildings()
    {
        var sim = Field();
        var grass = sim.Defs.Block("Grass");
        for (var y = 5; y <= 15; y++)
        for (var x = 6; x <= 14; x++)
            sim.Map.Block[sim.Map.Idx(x, y, 0)] = grass;
        for (var y = 8; y <= 12; y++)
            sim.SpawnStructure(sim.Thing("WoodFloor"), C(12, y));
        sim.SpawnStructure(sim.Thing("Bed"), C(11, 10));
        sim.Map.MarkStructureChanged();
        sim.RebuildDerivedIfNeeded();

        var sneeze = new BreathShape(6f, 30f, 8, 0.9f, 1f, SparesBuildings: true);
        for (var i = 0; i < 4; i++)
            Breath.Breathe(sim, Mouth(6, 10), new float3(13.5f, 10.5f, 1.3f), sneeze);
        Assert.NotEmpty(sim.Fire.BurningTiles);
        Assert.All(sim.Fire.BurningTiles, i => Assert.True(sim.Map.Structure[i].IsNull && sim.Map.Floor[i].IsNull, $"{sim.Map.CellOf(i)} is part of a building"));

        // The same breath, in earnest, does set the bed and floor alight.
        for (var i = 0; i < 4; i++)
            Breath.Breathe(sim, Mouth(6, 10), new float3(13.5f, 10.5f, 1.3f), sneeze with { SparesBuildings = false });
        Assert.Contains(sim.Fire.BurningTiles, i => !sim.Map.Structure[i].IsNull || !sim.Map.Floor[i].IsNull);
    }

    [Fact]
    public void ConeStopsAtAWall()
    {
        var sim = Field();
        for (var y = 4; y <= 16; y++)
        {
            sim.SpawnStructure(sim.Thing("StoneWall"), C(8, y));
            sim.SpawnStructure(sim.Thing("StoneWall"), new int3(8, y, 2));
            for (var x = 9; x <= 14; x++)
                sim.SpawnStructure(sim.Thing("Oak"), C(x, y));
        }
        sim.SpawnItem(sim.Thing("Wood"), 30, C(7, 10)); // something to catch on our side
        sim.RebuildDerivedIfNeeded();

        for (var i = 0; i < 4; i++)
            Breath.Breathe(sim, Mouth(4, 10), new float3(12.5f, 10.5f, 1.3f), Juvenile);
        Assert.DoesNotContain(sim.Fire.BurningTiles, i => sim.Map.CellOf(i).X >= 9);
        Assert.Contains(sim.Fire.BurningTiles, i => sim.Map.CellOf(i).X < 8);
    }

    [Fact]
    public void OrderedDragonApproachesAndBurnsTheTarget()
    {
        var sim = Field();
        sim.Climate.BaseTemperature = 30;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 30f);
        var tree = sim.SpawnStructure(sim.Thing("Oak"), C(25, 10));
        sim.RebuildDerivedIfNeeded();
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Ember", C(3, 10), ageDays: 5);
        Array.Fill(sim.World.Get<Needs>(dragon).Values, 1f);
        sim.World.Get<Dragon>(dragon).Affinity = 2f; // obedient

        Assert.True(DragonSystem.OrderBreath(sim, dragon, C(25, 10)));
        sim.TickMany(SimClock.HoursToTicks(1));
        Assert.True(!sim.World.IsAlive(tree) || sim.Map.Fire[sim.Map.Idx(C(25, 10))] > 0 || sim.World.Get<Thing>(tree).Hp < sim.Thing("Oak").MaxHp,
            "the tree should be burning or burned");
    }

    [Fact]
    public void BurnOrderClearsTheMarkedGroveAndVillagersLetItBurn()
    {
        var sim = Field();
        sim.Climate.BaseTemperature = 30;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 30f);
        var grove = new List<int3>();
        for (var y = 8; y <= 12; y++)
        for (var x = 20; x <= 22; x++)
        {
            sim.SpawnStructure(sim.Thing("Oak"), C(x, y));
            grove.Add(C(x, y));
        }
        sim.SpawnStructure(sim.Thing("Boulder"), C(21, 14)); // inside the order, but won't burn
        sim.RebuildDerivedIfNeeded();
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Vesper", C(3, 10), ageDays: 14);
        sim.World.Get<Dragon>(dragon).Affinity = 2f;
        var dragonWork = sim.World.Get<Mind>(dragon).WorkPriority;
        dragonWork[(int)WorkType.Chop] = dragonWork[(int)WorkType.Haul] = 0; // just the burning here
        var human = sim.SpawnCreature(sim.Defs.Creature("Human"), "Firefighter", C(15, 3));
        var mind = sim.World.Get<Mind>(human);
        Array.Fill(mind.WorkPriority, 0);
        mind.WorkPriority[(int)WorkType.Firefight] = 1;

        var marked = DragonSystem.DesignateBurn(sim, [.. grove, C(21, 14), C(10, 3)]);
        Assert.Equal(grove.Count, marked); // bare dirt and rock aren't marked

        string? DefAt(int3 c) => sim.Map.Structure[sim.Map.Idx(c)] is { IsNull: false } s ? sim.World.Get<Thing>(s).Def.Name : null;
        for (var hour = 0; hour < 12 && grove.Any(c => DefAt(c) == "Oak"); hour++)
        {
            Array.Fill(sim.World.Get<Needs>(dragon).Values, 1f);
            Array.Fill(sim.World.Get<Needs>(human).Values, 1f);
            for (var t = 0; t < SimClock.TicksPerHour; t += 30)
            {
                sim.TickMany(30);
                Assert.IsNotType<FightFireJob>(mind.Job);
            }
        }

        Assert.All(grove, c => Assert.Equal("DeadTree", DefAt(c))); // charred snags are left standing...
        Assert.All(grove, c => Assert.True(sim.Map.Designations[sim.Map.Idx(c)].HasFlag(Designation.Chop))); // ...to be felled next
        Assert.False(sim.Map.Structure[sim.Map.Idx(C(21, 14))].IsNull);
        sim.TickMany(SimClock.TicksPerHour); // let the dragon notice there's nothing left
        Assert.Empty(sim.Map.Designated(Designation.Burn));
    }

    [Fact]
    public void AnAdultClearsTheLandItBurned()
    {
        var sim = Field();
        var grove = new List<int3>();
        for (var x = 20; x <= 22; x++)
        {
            sim.SpawnStructure(sim.Thing("Pine"), C(x, 10));
            grove.Add(C(x, 10));
        }
        sim.RebuildDerivedIfNeeded();
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Vesper", C(3, 10), ageDays: 14);
        sim.World.Get<Dragon>(dragon).Affinity = 2f;
        DragonSystem.DesignateBurn(sim, grove);

        string? DefAt(int3 c) => sim.Map.Structure[sim.Map.Idx(c)] is { IsNull: false } s ? sim.World.Get<Thing>(s).Def.Name : null;
        var felled = false;
        for (var hour = 0; hour < 24 && !felled; hour++)
        {
            Array.Fill(sim.World.Get<Needs>(dragon).Values, 1f);
            sim.TickMany(SimClock.TicksPerHour);
            felled = grove.All(c => DefAt(c) == null);
        }
        Assert.True(felled, "the adult should burn the trees, then fell the snags");
        Assert.Contains(sim.World.Query<Thing>(), e => sim.World.Get<Thing>(e).Def.Name == "Wood");
    }

    [Fact]
    public void ADragonSetToNeverBurnLeavesTheOrderAlone()
    {
        var sim = Field();
        sim.SpawnStructure(sim.Thing("Oak"), C(20, 10));
        sim.RebuildDerivedIfNeeded();
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Vesper", C(3, 10), ageDays: 5);
        sim.World.Get<Dragon>(dragon).Affinity = 2f;
        sim.World.Get<Mind>(dragon).WorkPriority[(int)WorkType.Burn] = 0;
        DragonSystem.DesignateBurn(sim, [C(20, 10)]);
        sim.TickMany(SimClock.HoursToTicks(2));
        Assert.Equal(0f, sim.Map.Fire[sim.Map.Idx(C(20, 10))]);
        Assert.Equal("Oak", sim.World.Get<Thing>(sim.Map.Structure[sim.Map.Idx(C(20, 10))]).Def.Name);
    }

    [Fact]
    public void WorkDragonsCanDoGrowsWithThem()
    {
        var defs = LoadDefs();
        var dragon = defs.Creature("Dragon");
        var human = defs.Creature("Human");
        LifeStage Stage(string name) => dragon.LifeStages.First(s => s.Name == name);
        Assert.DoesNotContain(Enum.GetValues<WorkType>(), t => dragon.CanDo(t, Stage("Hatchling")));
        Assert.Equal([WorkType.Burn, WorkType.Stoke], Enum.GetValues<WorkType>().Where(t => dragon.CanDo(t, Stage("Juvenile"))));
        Assert.False(dragon.CanDo(WorkType.Craft, Stage("Adult")) || dragon.CanDo(WorkType.Cook, Stage("Adult")));
        Assert.True(dragon.CanDo(WorkType.Haul, Stage("Adult")) && dragon.CanDo(WorkType.Chop, Stage("Adult")));
        Assert.False(dragon.CanDo(WorkType.Doctor, Stage("Adult")));
        var adult = human.StageForAge(9000);
        Assert.False(human.CanDo(WorkType.Burn, adult), "people don't breathe fire");
        Assert.True(human.CanDo(WorkType.Hunt, adult) && human.CanDo(WorkType.Fish, adult));
    }

    [Fact]
    public void HatchlingsCantBreatheOnCommand()
    {
        var sim = Field();
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Pip", C(3, 10), ageDays: 1);
        Assert.False(DragonSystem.OrderBreath(sim, dragon, C(10, 10)));
    }
}
