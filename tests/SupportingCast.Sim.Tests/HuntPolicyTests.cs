using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Systems;
using static SupportingCast.Sim.Tests.TestWorld;

namespace SupportingCast.Sim.Tests;

/// <summary>Standing hunting orders: a dragon's <see cref="HuntPolicy"/> and the village <see cref="LarderSystem"/>.</summary>
public class HuntPolicyTests
{
    private static Simulation Mild(int size = 60)
    {
        var sim = Create(size, size);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    private static (Simulation Sim, Entity Keeper, Entity Dragon) WithDragon()
    {
        var sim = Mild();
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Wren", C(10, 10), ageDays: 25 * 365);
        sim.World.Add(me, new Avatar());
        var dragon = Companions.SpawnBondedDragon(sim, me, "Ashwing", null, C(12, 10));
        var food = sim.Defs.Need("Food");
        sim.World.Get<Needs>(dragon).Values[food.Index] = food.UrgentBelow - 0.05f;
        return (sim, me, dragon);
    }

    private static void Policy(Simulation sim, Entity dragon, HuntMode mode, bool home = false, bool dangerous = false, int range = 0) =>
        sim.ApplyNow(new HuntPolicyCommand(dragon, mode, home, dangerous, range));

    private static bool TickUntil(Simulation sim, Func<bool> done, int maxTicks)
    {
        for (var t = 0; t < maxTicks && !done(); t++)
            sim.Tick();
        return done();
    }

    private static Entity Deer(Simulation sim, int3 at, int herd = 0)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Deer"), "Deer", at, Faction.Wild);
        if (herd > 0)
            sim.World.Add(e, new Herd { Id = herd, LeaveTick = long.MaxValue });
        return e;
    }

    [Fact]
    public void HuntFirstSparesTheLarder()
    {
        var (sim, _, dragon) = WithDragon();
        sim.SpawnItem(sim.Thing("Meat"), 40, C(14, 12));
        Deer(sim, C(40, 40));
        Policy(sim, dragon, HuntMode.HuntFirst);
        Assert.IsType<DragonHuntJob>(DragonHunting.Meal(sim, dragon, sim.World.Get<Creature>(dragon).Def));

        Policy(sim, dragon, HuntMode.StoresFirst);
        Assert.IsType<EatJob>(DragonHunting.Meal(sim, dragon, sim.World.Get<Creature>(dragon).Def));
    }

    [Fact]
    public void DontHuntWaitsToBeFedUnlessStarving()
    {
        var (sim, _, dragon) = WithDragon();
        Deer(sim, C(40, 40));
        Policy(sim, dragon, HuntMode.Never);
        Assert.Null(DragonHunting.Hunt(sim, dragon));
        Assert.Null(DragonHunting.Meal(sim, dragon, sim.World.Get<Creature>(dragon).Def));

        var food = sim.Defs.Need("Food");
        sim.World.Get<Needs>(dragon).Values[food.Index] = DragonHunting.StarvingBelow - 0.02f;
        Assert.IsType<DragonHuntJob>(DragonHunting.Meal(sim, dragon, sim.World.Get<Creature>(dragon).Def));
    }

    [Fact]
    public void BoarsOnlyWhenAllowedAndOnlyWithinRange()
    {
        var (sim, _, dragon) = WithDragon();
        sim.SpawnCreature(sim.Defs.Creature("Boar"), "Boar", C(30, 30), Faction.Wild);
        Assert.Null(DragonHunting.Hunt(sim, dragon));
        Policy(sim, dragon, HuntMode.StoresFirst, dangerous: true);
        Assert.NotNull(DragonHunting.Hunt(sim, dragon));

        // A nest at (12, 12): a range of 10 keeps it off a boar 18+ cells away.
        sim.SpawnStructure(sim.Thing("Nest"), C(12, 14));
        Policy(sim, dragon, HuntMode.StoresFirst, dangerous: true, range: 10);
        Assert.Null(DragonHunting.Hunt(sim, dragon));
        Policy(sim, dragon, HuntMode.StoresFirst, dangerous: true, range: 30);
        Assert.NotNull(DragonHunting.Hunt(sim, dragon));
    }

    [Fact]
    public void BringKillsHomeCarriesTheCarcassToTheButcherSpot()
    {
        var (sim, _, dragon) = WithDragon();
        var spot = sim.SpawnStructure(sim.Thing("ButcherSpot"), C(8, 20));
        var deer = Deer(sim, C(40, 40));
        Policy(sim, dragon, HuntMode.StoresFirst, home: true);

        Assert.True(TickUntil(sim, () => sim.World.Has<Carried>(deer), SimClock.HoursToTicks(2)), "the dragon should pick its kill up");
        Assert.True(TickUntil(sim, () => !sim.World.IsAlive(deer), SimClock.HoursToTicks(1)), "and eat it at home");
        var meat = sim.SpawnedItems().Where(e => sim.World.Get<Thing>(e).Def.Name == "Meat").ToList();
        Assert.NotEmpty(meat);
        Assert.All(meat, m => Assert.Equal(spot, ButcherSpots.SpotAt(sim, sim.World.Get<Thing>(m).Cell)));
    }

    [Fact]
    public void ThePolicySurvivesASave()
    {
        var (sim, _, dragon) = WithDragon();
        Policy(sim, dragon, HuntMode.HuntFirst, home: true, dangerous: true, range: 25);
        sim.ApplyNow(new LarderCommand(60));
        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), sim.Defs);
        var p = loaded.World.Get<HuntPolicy>(dragon);
        Assert.Equal((HuntMode.HuntFirst, true, true, 25), (p.Mode, p.BringHome, p.Dangerous, p.Range));
        Assert.Equal(60, loaded.Larder.KeepMeat);
    }

    [Fact]
    public void TheLarderMarksHuntsWhileStoresAreLowButNeverTheLastOfAHerd()
    {
        var sim = Mild();
        sim.SpawnCreature(sim.Defs.Creature("Human"), "Aldric", C(10, 10));
        var near = Deer(sim, C(20, 10), herd: 1);
        var far = Deer(sim, C(40, 10), herd: 1);
        var farthest = Deer(sim, C(50, 10), herd: 1);
        var loner = Deer(sim, C(12, 12), herd: 2); // closest, but alone
        var boar = sim.SpawnCreature(sim.Defs.Creature("Boar"), "Boar", C(11, 10), Faction.Wild);
        sim.World.Add(boar, new Herd { Id = 3, LeaveTick = long.MaxValue });

        sim.Larder.Update(sim);
        Assert.False(sim.World.Has<HuntOrder>(near), "off by default");

        sim.ApplyNow(new LarderCommand(50));
        sim.Larder.Update(sim);
        Assert.True(sim.World.Has<HuntOrder>(near));
        Assert.False(sim.World.Has<HuntOrder>(loner) || sim.World.Has<HuntOrder>(boar));

        // A deer on order brings 30; 50 wanted: one more. Then the herd's down to its last: no more.
        sim.Larder.Update(sim);
        Assert.True(sim.World.Has<HuntOrder>(far));
        sim.World.Remove<HuntOrder>(near);
        sim.World.Get<Creature>(near).Dead = true;
        sim.Larder.Update(sim);
        Assert.False(sim.World.Has<HuntOrder>(farthest), "never the last of a herd");
        sim.World.Get<Creature>(near).Dead = false;

        // Enough in store: nothing more is marked.
        sim.World.Remove<HuntOrder>(near);
        sim.World.Remove<HuntOrder>(far);
        sim.SpawnItem(sim.Thing("Meat"), 30, C(5, 5));
        sim.SpawnItem(sim.Thing("RawFish"), 25, C(6, 5));
        sim.Larder.Update(sim);
        Assert.False(sim.World.Has<HuntOrder>(near) || sim.World.Has<HuntOrder>(far));
    }
}
