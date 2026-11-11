using DragonWorld.Sim.Combat;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Core;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Maps;
using DragonWorld.Sim.Scenarios;
using DragonWorld.Sim.Systems;
using static DragonWorld.Sim.Tests.TestWorld;

namespace DragonWorld.Sim.Tests;

/// <summary>The player binding wounds with bandages (<see cref="Bandaging"/>).</summary>
public class BandagingTests
{
    private static (Simulation Sim, Entity Me) Wounded(int bandages = 5, int cuts = 2)
    {
        var sim = Create(30, 20);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Wren", C(5, 5), ageDays: 25 * 365);
        sim.World.Add(me, new Avatar());
        sim.SetSkills(me);
        sim.GiveStartingPack(me);
        var body = sim.World.Get<Health>(me).Body;
        string[] parts = ["LeftArm", "RightLeg", "LeftLeg"];
        for (var i = 0; i < cuts; i++)
            HealthSystem.Damage(sim, me, 6, DamageType.Cut, body.PartIndex(parts[i]));
        if (bandages > 0)
            sim.Store(sim.PackOf(me), sim.CreateItem(sim.Thing("Bandage"), bandages));
        return (sim, me);
    }

    [Fact]
    public void ABandageBindsAWoundAndIsUsedUp()
    {
        var (sim, me) = Wounded(bandages: 5, cuts: 2);
        var h = sim.World.Get<Health>(me);
        Assert.Equal(2, Bandaging.WoundsToBind(sim, me));
        Assert.True(h.BleedPerHour > 0);
        Assert.Null(Bandaging.CantBind(sim, me, me));

        Assert.Equal(2, Bandaging.Bind(sim, me, me));
        Assert.Equal(0, Bandaging.WoundsToBind(sim, me));
        Assert.Equal(3, Bandaging.BandagesOn(sim, me));
        Assert.All(h.Injuries, i => Assert.True(i.Tended && i.Quality > 0));
        Assert.Equal(0f, h.BleedPerHour, 3);
    }

    [Fact]
    public void YouBindAsManyAsYouHaveBandagesFor()
    {
        var (sim, me) = Wounded(bandages: 1, cuts: 2);
        Assert.Equal(1, Bandaging.Bind(sim, me, me));
        Assert.Equal(1, Bandaging.WoundsToBind(sim, me));
        Assert.Equal(0, Bandaging.BandagesOn(sim, me));
        Assert.Equal("no bandages", Bandaging.CantBind(sim, me, me));
    }

    [Fact]
    public void OutOfAFightItsAShortJobFromTheItemsMenu()
    {
        var (sim, me) = Wounded();
        sim.Enqueue(new BandageCommand(me, me));
        for (var t = 0; t < SimClock.TicksPerSecond * 10 && Bandaging.WoundsToBind(sim, me) > 0; t++)
            sim.Tick();
        Assert.Equal(0, Bandaging.WoundsToBind(sim, me));
    }

    [Fact]
    public void YouCanBindYourDragonBesideYouButNotFromAcrossTheField()
    {
        var (sim, me) = Wounded(cuts: 0);
        var dragon = Companions.SpawnBondedDragon(sim, me, "Ashwing", null, C(6, 5));
        sim.World.Get<Position>(dragon) = Position.At(C(6, 5));
        HealthSystem.Damage(sim, dragon, 10, DamageType.Pierce);
        Assert.Equal(1, Bandaging.WoundsToBind(sim, dragon));
        Assert.Null(Bandaging.CantBind(sim, me, dragon));
        sim.World.Get<Position>(dragon) = Position.At(C(15, 5));
        Assert.Equal("too far away", Bandaging.CantBind(sim, me, dragon));
    }

    [Fact]
    public void InAFightBandagingCostsTimeUnits()
    {
        var (sim, me) = Wounded();
        sim.SpawnCreature(sim.Defs.Creature("Human"), "Bandit", C(15, 5), Faction.Raider);
        sim.Combat.Start(sim);
        var unit = sim.Combat.Unit(me)!;
        Assert.Null(sim.Combat.Interact(sim, me, TimeUnits.Bandage, () => Bandaging.Bind(sim, me, me) > 0, "bandaged"));
        Assert.Equal(TimeUnits.PerTurn(sim, me) - TimeUnits.Share(sim, me, TimeUnits.Bandage), unit.TimeUnits);
        Assert.Equal(0, Bandaging.WoundsToBind(sim, me));
        // Nothing left to bind: it doesn't happen, and costs nothing.
        Assert.NotNull(sim.Combat.Interact(sim, me, TimeUnits.Bandage, () => Bandaging.Bind(sim, me, me) > 0, "bandaged"));
        Assert.Equal(TimeUnits.PerTurn(sim, me) - TimeUnits.Share(sim, me, TimeUnits.Bandage), unit.TimeUnits);
    }

    [Fact]
    public void TheCampsChestHoldsYourBandages()
    {
        var sim = Simulation.CreateStandard(LoadDefs(), 96, 96, 61, Maps.MapGenerator.RecommendedDepth);
        AdventureStart.Setup(sim, StartKind.Escape, new Hero("Wren", null, null), "Normal");
        var chest = EscapeCamp.Of(sim)!.Chest;
        Assert.Contains(sim.StoredIn(chest), e => sim.World.Get<Thing>(e).Def.Name == "Bandage");
    }

    [Fact]
    public void BandagesAreWovenByHandFromFlaxFiber()
    {
        var (sim, me) = Wounded(bandages: 0, cuts: 2);
        sim.Store(sim.PackOf(me), sim.CreateItem(sim.Thing("Fiber"), 6));
        Assert.Equal("no bandages", Bandaging.CantBind(sim, me, me));

        sim.ApplyNow(new HandCraftCommand(me, sim.Defs.Recipe("MakeBandages")));
        sim.ApplyNow(new AvatarWorkCommand(me, true));
        sim.TickMany(SimClock.TicksPerSecond * 12);

        Assert.Equal(2, Bandaging.BandagesOn(sim, me));
        Assert.Null(Bandaging.CantBind(sim, me, me));
    }

    [Fact]
    public void WildFlaxGrowsNearTheStart()
    {
        var sim = Simulation.CreateStandard(LoadDefs(), 160, 160, 1234, MapGenerator.RecommendedDepth);
        var center = MapGenerator.Generate(sim, new MapGenerator.Options { SpawnStartingKit = false, Wildlife = false });
        var flax = sim.World.Query<Thing>().Where(e => sim.World.Get<Thing>(e).Def.Name == "WildFlax").ToList();
        Assert.Contains(flax, e => sim.World.Get<Thing>(e).Cell is var c && Math.Abs(c.X - center.X) + Math.Abs(c.Y - center.Y) < 25);
        Assert.True(flax.Count > 4, $"only {flax.Count} wild flax on the map");
    }
}
