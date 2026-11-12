using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Gear orders: a "Take off" that sticks, lifting it, and "wear this" (<see cref="Outfit"/>).</summary>
public class OutfitTests
{
    private static (Simulation Sim, Entity Pawn) Setup()
    {
        var sim = TestWorld.Create(30, 20, 1, 4);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        var pawn = sim.SpawnCreature(sim.Defs.Creature("Human"), "Ada", C(10, 10), ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(pawn).Values, 1f);
        sim.SetSkills(pawn);
        // Nothing to do but look after themselves.
        Array.Fill(sim.World.Get<Mind>(pawn).WorkPriority, 0);
        return (sim, pawn);
    }

    private static Entity WornHead(Simulation sim, Entity e) =>
        sim.World.TryGet<Apparel>(e, out var a) && sim.World.IsAlive(a.Head) ? a.Head : Entity.Null;

    private static void Run(Simulation sim, int hours)
    {
        sim.TickMany(1); // apply queued commands
        sim.TickMany(SimClock.TicksPerHour * hours);
    }

    [Fact]
    public void ATakenOffHelmStaysOff()
    {
        var (sim, pawn) = Setup();
        sim.Wear(pawn, sim.Thing("LeatherCap"));
        sim.SpawnItem(sim.Thing("IronHelm"), 1, C(13, 10));
        sim.Enqueue(new GearOrderCommand(pawn, GearSlot.Head));
        Run(sim, 24);
        Assert.True(WornHead(sim, pawn).IsNull, "they shouldn't put a helm back on");
        Assert.True(sim.World.Get<Outfit>(pawn).BareHead);

        // Lifting the order: they go and put one on again.
        sim.Enqueue(new GearOrderCommand(pawn, GearSlot.Head, KeepBare: false));
        Run(sim, 2);
        Assert.False(WornHead(sim, pawn).IsNull);
    }

    [Fact]
    public void DroppingTheWeaponSticksToo()
    {
        var (sim, pawn) = Setup();
        sim.Equip(pawn, sim.Thing("Axe"));
        sim.Enqueue(new GearOrderCommand(pawn, GearSlot.Weapon));
        Run(sim, 12);
        Assert.False(sim.World.Has<Equipment>(pawn));
    }

    [Fact]
    public void WearThisPutsOnThatItem()
    {
        var (sim, pawn) = Setup();
        sim.Wear(pawn, sim.Thing("LeatherCap"));
        var helm = sim.CreateItem(sim.Thing("IronHelm"), 1);
        sim.PlaceItemNear(helm, C(16, 12));
        sim.Enqueue(new GearOrderCommand(pawn, GearSlot.Head));
        sim.Enqueue(new WearCommand(pawn, helm));
        Run(sim, 2);
        Assert.Equal("IronHelm", sim.World.Get<Thing>(WornHead(sim, pawn)).Def.Name);
        Assert.False(sim.World.Get<Outfit>(pawn).BareHead);
        Assert.True(sim.World.Get<Outfit>(pawn).Wanted.IsNull || !sim.IsAvailable(sim.World.Get<Outfit>(pawn).Wanted));
    }

    [Fact]
    public void GearOrdersSurviveASave()
    {
        var (sim, pawn) = Setup();
        sim.Wear(pawn, sim.Thing("LeatherCap"));
        sim.Enqueue(new GearOrderCommand(pawn, GearSlot.Head));
        sim.TickMany(1);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.True(loaded.World.Get<Outfit>(pawn).BareHead);
        Assert.False(loaded.World.Get<Outfit>(pawn).Unarmed);
        AiSystem.InterruptAll(sim);
        loaded.TickMany(SimClock.TicksPerHour * 6);
        Assert.True(WornHead(loaded, pawn).IsNull);
    }
}
