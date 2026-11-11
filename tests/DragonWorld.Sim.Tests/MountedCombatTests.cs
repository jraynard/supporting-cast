using DragonWorld.Sim.Combat;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Systems;
using static DragonWorld.Sim.Tests.TestWorld;

namespace DragonWorld.Sim.Tests;

/// <summary>A keeper fighting from their dragon's back, and what neglect does to a dragon's trust.</summary>
public class MountedCombatTests
{
    private static (Simulation Sim, Entity Rider, Entity Dragon) Mounted(ulong seed = 1)
    {
        var sim = Create(40, 20, seed, depth: 6);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        var rider = sim.SpawnCreature(sim.Defs.Creature("Human"), "Aldric", C(8, 10), ageDays: 25 * 365);
        sim.SetSkills(rider);
        sim.Equip(rider, sim.Thing("Bow"));
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Vesper", C(8, 10), ageDays: 14);
        sim.World.Get<Dragon>(dragon).Affinity = 2f;
        EggSystem.AssignKeeper(sim, dragon, rider);
        foreach (var e in sim.World.Query<Needs>())
            Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        RidingSystem.MountUp(sim, rider, dragon);
        return (sim, rider, dragon);
    }

    private static Entity Raider(Simulation sim, int3 at, string weapon = "Club")
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), "Bandit", at, Faction.Raider, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        sim.SetSkills(e);
        sim.Equip(e, sim.Thing(weapon));
        return e;
    }

    [Fact]
    public void ThePairFightsAsTwoUnitsThatMoveAsOne()
    {
        var (sim, rider, dragon) = Mounted();
        var raider = Raider(sim, C(24, 10));
        sim.Combat.Start(sim);
        var enc = sim.Combat;
        Assert.NotNull(enc.Unit(rider));
        Assert.NotNull(enc.Unit(dragon));
        Assert.True(sim.World.Has<Riding>(rider), "they stay mounted when the fight starts");

        Assert.NotNull(enc.Move(sim, rider, C(10, 10))); // the rider doesn't walk off the dragon's back
        Assert.Null(enc.Move(sim, dragon, C(12, 10)));
        enc.FinishAnimation(sim);
        Assert.Equal(C(12, 10), sim.World.Get<Position>(rider).Cell);

        // Both still act this turn: the rider shoots, the dragon can breathe.
        var bow = CombatRules.Attacks(sim, rider)[0];
        Assert.NotNull(enc.Attack(sim, rider, raider, bow, out var error));
        Assert.Null(error);
        Assert.Null(enc.CanCommand(sim, dragon));
    }

    [Fact]
    public void ARiderSeesAndShootsOverAWall()
    {
        var (sim, rider, _) = Mounted();
        var raider = Raider(sim, C(18, 10));
        var onFoot = sim.SpawnCreature(sim.Defs.Creature("Human"), "Brenna", C(8, 11), ageDays: 25 * 365);
        sim.Equip(onFoot, sim.Thing("Bow"));
        sim.FillBlocks(new int3(13, 0, 1), new int3(13, 19, 1)); // a one-level wall of rock between them
        var bow = CombatRules.Attacks(sim, rider)[0];
        Assert.True(CombatRules.Chance(sim, rider, raider, bow).Possible, "from the dragon's back the rider sees over it");
        Assert.Equal("no line of fire", CombatRules.Chance(sim, onFoot, raider, bow).Blocked);
    }

    [Fact]
    public void SomeBlowsAimedAtTheRiderHitTheDragon()
    {
        // One blow each in forty fresh fights (a rider doesn't last forty blows).
        var onDragon = 0;
        for (var seed = 1UL; seed <= 40; seed++)
        {
            var (sim, rider, dragon) = Mounted(seed);
            var raider = Raider(sim, C(9, 10), "ShortSword");
            if (CombatRules.Resolve(sim, raider, rider, CombatRules.Attacks(sim, raider)[0]).Target == dragon)
                onDragon++;
        }
        Assert.InRange(onDragon, 2, 20); // about one in five
    }

    [Fact]
    public void ADragonsBreathSparesItsRider()
    {
        var (sim, rider, dragon) = Mounted();
        var raider = Raider(sim, C(13, 10));
        var (_, burned) = CombatRules.Breathe(sim, dragon, C(13, 10));
        Assert.DoesNotContain(burned, b => b.Who == rider);
        Assert.Contains(burned, b => b.Who == raider);
    }

    [Fact]
    public void AMountThatGoesDownMidFightUnseatsItsRider()
    {
        var (sim, rider, dragon) = Mounted();
        Raider(sim, C(24, 10));
        sim.Combat.Start(sim);
        ref var c = ref sim.World.Get<Creature>(dragon);
        HealthSystem.SetDowned(sim, dragon, ref c, true);
        Assert.False(sim.World.Has<Riding>(rider));
        Assert.False(float.IsInfinity(TimeUnits.WalkCost(sim, rider)), "on foot, the rider moves on their own again");
    }

    [Fact]
    public void NeglectWearsAwayADragonsTrust()
    {
        var (sim, rider, dragon) = Mounted();
        RidingSystem.Dismount(sim, rider);
        sim.World.Get<Mind>(rider).Drafted = true; // the keeper is busy elsewhere: nobody bonds with it
        sim.World.Get<Dragon>(dragon).Affinity = 0.6f;
        var bond = sim.Defs.Need("Bond");
        for (var hour = 0; hour < 24; hour++)
        {
            var needs = sim.World.Get<Needs>(dragon);
            Array.Fill(needs.Values, 1f);
            needs.Values[bond.Index] = 0.05f; // lonely all day
            sim.TickMany(SimClock.TicksPerHour);
        }
        var affinity = sim.World.Get<Dragon>(dragon).Affinity;
        Assert.InRange(affinity, 0.6f - DragonSystem.LonelyAffinityPerDay - 0.02f, 0.6f - DragonSystem.LonelyAffinityPerDay + 0.02f);
        Assert.Contains("affinity", RidingSystem.CantMount(sim, rider, dragon));
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("seems distant"));
    }
}
