using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Healing;
using HeroesNotIncluded.Sim.Work;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

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

    /// <summary>
    /// Bind by hand: each turn of cloth pulled to <paramref name="pullTo"/> of the way from the
    /// band's bottom (0) to its top (1), then let go. Returns the dressings' quality.
    /// </summary>
    private static float BindByHand(Simulation sim, Entity binder, Entity patient, float pullTo)
    {
        sim.ApplyNow(new BandageCommand(binder, patient));
        Assert.IsType<BandageSession>(sim.World.Get<WorkSession>(binder));
        var predictor = new WorkPredictor();
        for (var t = 0; t < SimClock.TicksPerSecond * 60 && sim.World.Has<WorkSession>(binder); t++)
        {
            sim.Tick();
            var held = predictor.Session is BandageSession b && b.Tension < b.Low + (b.High - b.Low) * pullTo;
            predictor.Update(sim, binder, 1, false, held, 0, c => sim.ApplyNow(c));
        }
        sim.Tick();
        return sim.World.Get<Health>(patient).Injuries.Where(i => i.Tended).Average(i => i.Quality);
    }

    [Fact]
    public void YouBindByHandATurnOfClothAtATime()
    {
        var (sim, me) = Wounded(bandages: 5, cuts: 2);
        BindByHand(sim, me, me, 0.5f);
        Assert.Equal(0, Bandaging.WoundsToBind(sim, me));
        Assert.Equal(3, Bandaging.BandagesOn(sim, me));
    }

    [Fact]
    public void TurnsThatSitRightMakeABetterDressingThanSlackOrCrushingOnes()
    {
        var (a, me) = Wounded();
        var right = BindByHand(a, me, me, 0.5f);
        var (b, me2) = Wounded();
        var slack = BindByHand(b, me2, me2, -0.6f);
        var (c, me3) = Wounded();
        var tight = BindByHand(c, me3, me3, 3f); // past the band, into the crush
        Assert.True(right > slack + 0.15f, $"right {right} vs slack {slack}");
        Assert.True(right > tight + 0.15f, $"right {right} vs too tight {tight}");
        Assert.Contains(c.Messages.Messages, m => m.Text.Contains("too tight"));
    }

    [Fact]
    public void ABandageCanComeFromAChestWithinReach()
    {
        var (sim, me) = Wounded(bandages: 0, cuts: 1);
        Assert.Equal("no bandages", Bandaging.CantBind(sim, me, me));
        var chest = sim.SpawnStructure(sim.Thing("Chest"), C(6, 5));
        sim.RebuildDerivedIfNeeded();
        sim.Store(chest, sim.CreateItem(sim.Thing("Bandage"), 3));
        Assert.Equal(3, Bandaging.BandagesOn(sim, me));
        BindByHand(sim, me, me, 0.5f);
        Assert.Equal(0, Bandaging.WoundsToBind(sim, me));
        Assert.Equal(2, sim.StoredIn(chest).Sum(i => sim.World.Get<Stack>(i).Count));
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
    public void ClothTearsIntoBandagesFasterThanFiberWeaves()
    {
        var (sim, me) = Wounded(bandages: 0, cuts: 2);
        sim.Store(sim.PackOf(me), sim.CreateItem(sim.Thing("Cloth"), 1));
        Assert.True(sim.Defs.Recipe("TearBandages").Work < sim.Defs.Recipe("MakeBandages").Work);

        sim.ApplyNow(new HandCraftCommand(me, sim.Defs.Recipe("TearBandages")));
        sim.ApplyNow(new AvatarWorkCommand(me, true));
        sim.TickMany(SimClock.TicksPerSecond * 12);

        Assert.Equal(3, Bandaging.BandagesOn(sim, me));
    }
}
