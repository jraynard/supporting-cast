using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Systems;
using static SupportingCast.Sim.Tests.TestWorld;

namespace SupportingCast.Sim.Tests;

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
}
