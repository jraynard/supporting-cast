using DragonWorld.Sim.Ai;
using DragonWorld.Sim.Combat;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Saving;
using DragonWorld.Sim.Systems;
using static DragonWorld.Sim.Tests.TestWorld;

namespace DragonWorld.Sim.Tests;

/// <summary>A keeper riding their grown dragon (<see cref="RidingSystem"/>).</summary>
public class RidingTests
{
    /// <summary>A warm field split by a river (x = 18..21) nobody can walk across; keeper and adult dragon on the west bank.</summary>
    private static (Simulation Sim, Entity Keeper, Entity Dragon) Riverside(float affinity = 2f, float ageDays = 14)
    {
        var sim = Create(40, 20, depth: 6);
        sim.Climate.BaseTemperature = 25;
        sim.Climate.DailySwing = 0;
        var water = sim.Defs.Block("Water");
        for (var y = 0; y < 20; y++)
        for (var x = 18; x <= 21; x++)
            sim.Map.Block[sim.Map.Idx(x, y, 0)] = water;
        sim.Map.MarkStructureChanged();
        sim.RebuildDerivedIfNeeded();
        Array.Fill(sim.Map.Temperature, 25f);
        var keeper = sim.SpawnCreature(sim.Defs.Creature("Human"), "Aldric", C(5, 10));
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Vesper", C(8, 10), ageDays: ageDays);
        sim.World.Get<Dragon>(dragon).Affinity = affinity;
        EggSystem.AssignKeeper(sim, dragon, keeper);
        Fed(sim);
        return (sim, keeper, dragon);
    }

    private static void Fed(Simulation sim)
    {
        foreach (var e in sim.World.Query<Needs>())
            Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
    }

    private static void Mounted(Simulation sim, Entity keeper, Entity dragon)
    {
        AiSystem.Order(sim, keeper, new MountJob(dragon));
        for (var t = 0; t < SimClock.TicksPerSecond * 30 && !sim.World.Has<Riding>(keeper); t++)
            sim.Tick();
        Assert.True(sim.World.Has<Riding>(keeper), $"the keeper should be riding ({sim.Ai.LastFailure.GetValueOrDefault(keeper)})");
    }

    [Fact]
    public void TheKeeperRidesTheirDragonAcrossTheRiver()
    {
        var (sim, keeper, dragon) = Riverside();
        Mounted(sim, keeper, dragon);
        Assert.True(sim.World.Get<Mind>(dragon).Drafted);

        Assert.True(RidingSystem.OrderMove(sim, keeper, C(32, 10)), "a mounted dragon goes where it's sent");
        var flew = false;
        for (var t = 0; t < SimClock.TicksPerSecond * 60 && sim.World.Get<Mover>(dragon).Moving; t++)
        {
            sim.Tick();
            Assert.Equal(sim.World.Get<Position>(dragon).Pos, sim.World.Get<Position>(keeper).Pos);
            if (TacticalMoves.Airborne(sim, sim.World.Get<Position>(dragon).Cell))
            {
                flew = true;
                Assert.False(RidingSystem.Dismount(sim, keeper), "no getting off in mid-air");
            }
        }
        Assert.True(flew, "the river is crossed by air");
        Assert.Equal(C(32, 10), sim.World.Get<Position>(dragon).Cell);

        Assert.True(RidingSystem.Dismount(sim, keeper));
        Assert.False(sim.World.Get<Mind>(dragon).Drafted);
        var landed = sim.World.Get<Position>(keeper).Cell;
        Assert.True(landed.X > 21, "the keeper gets off on the far bank");
        Assert.True(sim.Map.Passable(landed));
    }

    [Fact]
    public void ADragonThatGoesDownThrowsItsRider()
    {
        var (sim, keeper, dragon) = Riverside();
        Mounted(sim, keeper, dragon);
        ref var c = ref sim.World.Get<Creature>(dragon);
        HealthSystem.SetDowned(sim, dragon, ref c, true);
        Assert.False(sim.World.Has<Riding>(keeper));
        Assert.False(sim.World.Has<Mount>(dragon));
        Assert.False(sim.World.Get<Mind>(dragon).Drafted);
    }

    [Fact]
    public void OnlyAGrownDragonThatTrustsItsKeeperCanBeRidden()
    {
        var (sim, keeper, dragon) = Riverside(affinity: 0.2f);
        Assert.Contains("affinity", RidingSystem.CantMount(sim, keeper, dragon));
        var (young, youngKeeper, juvenile) = Riverside(ageDays: 6);
        Assert.Contains("too young", RidingSystem.CantMount(young, youngKeeper, juvenile));
        var stranger = sim.SpawnCreature(sim.Defs.Creature("Human"), "Brenna", C(6, 12));
        sim.World.Get<Dragon>(dragon).Affinity = 2f;
        Assert.Contains("keeper", RidingSystem.CantMount(sim, stranger, dragon));
        Assert.Null(RidingSystem.CantMount(sim, keeper, dragon));
    }

    [Fact]
    public void RidingMakesADragonMoreObedient()
    {
        var (sim, keeper, dragon) = Riverside();
        Mounted(sim, keeper, dragon);
        sim.World.Get<Dragon>(dragon).Affinity = 0.4f;
        Assert.Equal(0.7f + RidingSystem.MountedObedience, DragonSystem.Obedience(sim, dragon), 3);
        RidingSystem.Dismount(sim, keeper);
        Assert.Equal(0.7f, DragonSystem.Obedience(sim, dragon), 3);
    }

    [Fact]
    public void ARideSurvivesASave()
    {
        var (sim, keeper, dragon) = Riverside();
        Mounted(sim, keeper, dragon);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        AiSystem.InterruptAll(sim);
        Assert.True(loaded.World.Has<Riding>(keeper));
        Assert.True(loaded.World.Get<Mind>(dragon).Drafted);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        RidingSystem.OrderMove(sim, dragon, C(32, 10));
        RidingSystem.OrderMove(loaded, dragon, C(32, 10));
        sim.TickMany(SimClock.TicksPerSecond * 30);
        loaded.TickMany(SimClock.TicksPerSecond * 30);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        Assert.Equal(loaded.World.Get<Position>(dragon).Pos, loaded.World.Get<Position>(keeper).Pos);
    }
}
