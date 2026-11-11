using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Systems;
using static SupportingCast.Sim.Tests.TestWorld;

namespace SupportingCast.Sim.Tests;

/// <summary>Hunting from dragonback in real time: the rider shooting from the saddle, the mount giving chase.</summary>
public class MountedHuntingTests
{
    private static (Simulation Sim, Entity Me, Entity Dragon) Mounted(float affinity = 1f)
    {
        var sim = Create(60, 60, depth: 8); // sky to take off into: on foot a deer outruns a dragon
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Wren", C(20, 20), ageDays: 25 * 365);
        sim.World.Add(me, new Avatar());
        sim.SetSkills(me);
        sim.Equip(me, sim.Thing("Bow"));
        var dragon = Companions.SpawnBondedDragon(sim, me, "Ashwing", null, C(21, 20));
        sim.World.Get<Dragon>(dragon).Affinity = affinity;
        RidingSystem.MountUp(sim, me, dragon);
        return (sim, me, dragon);
    }

    private static Entity Deer(Simulation sim, int3 at) => sim.SpawnCreature(sim.Defs.Creature("Deer"), "Deer", at, Faction.Wild);

    private static bool TickUntil(Simulation sim, Func<bool> done, int maxTicks)
    {
        for (var t = 0; t < maxTicks && !done(); t++)
            sim.Tick();
        return done();
    }

    [Fact]
    public void TheRiderShootsGameFromTheSaddle()
    {
        var (sim, me, _) = Mounted();
        // Just past where the deer would bolt (its flee radius), within the bow's 14 cells.
        var deer = Deer(sim, C(33, 20));
        var action = AvatarSystem.ActionAt(sim, me, C(33, 20));
        Assert.Equal(AvatarActionKind.Hunt, action.Kind);

        sim.ApplyNow(new AvatarWorkCommand(me, true, C(33, 20)));
        Assert.IsType<AvatarHuntJob>(sim.World.Get<Mind>(me).Job);
        sim.Tick();
        // An arrow loosed from the saddle (a miss sends the deer running: the dragon gives chase for the rest).
        var shot = Assert.Single(sim.RecentShots);
        Assert.True(shot.Ranged);
        Assert.Equal(sim.World.Get<Position>(me).Pos, shot.From);
        Assert.True(sim.World.Has<Riding>(me), "still in the saddle");
        Assert.True(sim.World.Get<Health>(deer).Injuries.Count > 0 || sim.World.Has<Provoked>(deer));
    }

    [Fact]
    public void FromTheSaddleFOnlyHunts()
    {
        var (sim, me, _) = Mounted();
        sim.SpawnItem(sim.Thing("Berries"), 5, C(20, 21));
        Assert.Equal(AvatarActionKind.None, AvatarSystem.ActionAt(sim, me, C(20, 21)).Kind);
        var carcass = Deer(sim, C(21, 21));
        NeedsSystem.Kill(sim, carcass, ref sim.World.Get<Creature>(carcass));
        Assert.Equal(AvatarActionKind.None, AvatarSystem.ActionAt(sim, me, C(21, 21)).Kind); // get down to butcher
    }

    [Fact]
    public void TheMountGivesChaseAndLeavesTheKillForItsRider()
    {
        var (sim, me, dragon) = Mounted();
        var deer = Deer(sim, C(40, 30));
        sim.ApplyNow(new MountedHuntCommand(me, deer));
        Assert.IsType<DragonHuntJob>(sim.World.Get<Mind>(dragon).Job);
        Assert.True(TickUntil(sim, () => sim.World.Get<Creature>(deer).Dead, SimClock.HoursToTicks(1)), "the dragon should run the deer down");
        sim.TickMany(300);
        Assert.True(sim.World.IsAlive(deer), "it leaves the kill rather than eating it");
        Assert.True(sim.World.Has<Riding>(me));
        Assert.Equal(sim.World.Get<Position>(dragon).Cell, sim.World.Get<Position>(me).Cell);
    }

    [Fact]
    public void ASulkingMountWontGiveChaseAndAHungryOneDoesntDropItsRiderMidHunt()
    {
        var (sim, me, dragon) = Mounted();
        var deer = Deer(sim, C(40, 30));
        Bonding.Care(sim, dragon).Sulking = true;
        sim.ApplyNow(new MountedHuntCommand(me, deer));
        Assert.False(sim.World.Get<Mind>(dragon).Job is DragonHuntJob);
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("won't give chase"));

        Bonding.Care(sim, dragon).Sulking = false;
        var food = sim.Defs.Need("Food");
        sim.World.Get<Needs>(dragon).Values[food.Index] = food.UrgentBelow - 0.05f;
        sim.ApplyNow(new MountedHuntCommand(me, deer));
        for (var t = 0; t < 600 && sim.World.Get<Mind>(dragon).Job is DragonHuntJob; t++)
        {
            sim.Tick();
            Assert.True(sim.World.Has<Riding>(me) || !(sim.World.Get<Mind>(dragon).Job is DragonHuntJob), "no getting down mid-hunt");
        }
    }

    [Fact]
    public void HuntWithMeIsATrickForGivingChase()
    {
        var sim = Create();
        var trick = sim.Defs.AllTricks.Single(t => t.Name == "HuntWithMe");
        Assert.Contains(Defs.WorkType.Hunt, trick.Work);
    }
}

