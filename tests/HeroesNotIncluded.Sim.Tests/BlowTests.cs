using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Blows seen coming (<see cref="Blows"/>): parry, block, dodge, reach, and a player's late-arriving keys.</summary>
public class BlowTests
{
    /// <summary>A player with a sword and a swordsman beside them, his blow at them just wound up.</summary>
    private static (Simulation Sim, Entity Me, Entity Raider, Windup Blow) Duel(bool heavy = false)
    {
        var (sim, center) = TestWorld.Millbrook(villagers: 0);
        var me = VillageRound.SpawnHero(sim, new Hero("Arin", null, null), center);
        sim.Equip(me, sim.Defs.Thing("ShortSword"));
        var here = sim.World.Get<Position>(me).Cell;
        var beside = sim.SpiralFrom(here, 2).First(c => c != here && sim.Map.Passable(c) && Ai.Pathfinder.Touches(c, here) && c.Z == here.Z);
        var raider = Raids.SpawnRaider(sim, sim.Defs.AllRaiderKinds.First(k => k.Name == "Swordsman"), "Mott", beside, sim.Rand);
        var attack = CombatRules.Attacks(sim, raider).First(a => !a.Ranged);
        Assert.True(Blows.Begin(sim, raider, me, attack, heavy));
        return (sim, me, raider, sim.World.Get<Windup>(raider));
    }

    private static void RunTo(Simulation sim, long tick)
    {
        while (sim.Clock.Tick < tick)
            sim.Tick();
    }

    /// <summary>How the first blow at <paramref name="target"/> ended.</summary>
    private static BlowOutcome? First(Simulation sim, Entity target) =>
        sim.RecentBlows.FirstOrDefault(b => b.Target == target)?.Outcome;

    private static void Guard(Simulation sim, Entity me, bool up, long at) =>
        sim.ApplyNow(new CombatInputCommand(me, up ? CombatInput.GuardUp : CombatInput.GuardDown, at));

    [Fact]
    public void AGuardRaisedAsTheBlowLandsParriesItAndLeavesThemOpen()
    {
        var (sim, me, raider, blow) = Duel();
        RunTo(sim, blow.Impact - 5);
        Guard(sim, me, true, sim.Clock.Tick);
        RunTo(sim, Blows.LandsAt(sim, blow) + 1);
        Assert.Equal(BlowOutcome.Parried, First(sim, me));
        Assert.Empty(sim.World.Get<Health>(me).Injuries);
        Assert.True(Blows.Staggered(sim, raider));
        var g = sim.World.Get<Guard>(me);
        Assert.Equal(raider, g.RiposteOn);
    }

    [Fact]
    public void AGuardHeldUpLongBeforeBlocksInsteadAndCostsBreath()
    {
        var (sim, me, raider, blow) = Duel();
        Guard(sim, me, true, sim.Clock.Tick);
        RunTo(sim, Blows.LandsAt(sim, blow) + 1);
        Assert.Equal(BlowOutcome.Blocked, First(sim, me));
        Assert.False(Blows.Staggered(sim, raider));
        Assert.True(sim.World.Get<Guard>(me).Stamina < 1f - Blows.BlockCost * 0.5f);
    }

    [Fact]
    public void AParryPressedInTimeCountsThoughItArrivesLate()
    {
        var (sim, me, _, blow) = Duel();
        // Pressed just before impact on the player's screen; reaches the sim after it (co-op latency).
        RunTo(sim, blow.Impact + Blows.LatencyGrace / 2);
        Guard(sim, me, true, blow.Impact - 3);
        RunTo(sim, Blows.LandsAt(sim, blow) + 1);
        Assert.Equal(BlowOutcome.Parried, First(sim, me));
    }

    [Fact]
    public void AGuardRaisedAfterTheBlowLandsIsTooLate()
    {
        var (sim, me, _, blow) = Duel();
        RunTo(sim, blow.Impact + 2);
        Guard(sim, me, true, blow.Impact + 1);
        RunTo(sim, Blows.LandsAt(sim, blow) + 1);
        Assert.NotEqual(BlowOutcome.Parried, First(sim, me));
        Assert.NotEqual(BlowOutcome.Blocked, First(sim, me));
    }

    [Fact]
    public void StampsFromTheFutureOrLongAgoAreTakenAsNow()
    {
        var (sim, me, _, blow) = Duel();
        RunTo(sim, blow.Impact - 2);
        Guard(sim, me, true, long.MaxValue); // claims a raise right at impact: it's now, a parry anyway
        Assert.Equal(sim.Clock.Tick, sim.World.Get<Guard>(me).RaisedAt);
        Guard(sim, me, false, long.MinValue);
        Assert.Equal(sim.Clock.Tick, sim.World.Get<Guard>(me).LoweredAt);
    }

    [Fact]
    public void SteppingOutOfReachMakesItMiss()
    {
        var (sim, me, raider, blow) = Duel();
        var at = sim.World.Get<Position>(raider).Cell;
        var away = sim.SpiralFrom(at, 12).First(c => sim.Map.Passable(c) && Maps.Map.Distance(c, at) >= 10 && c.Z == at.Z);
        sim.World.Get<Position>(me) = Position.At(away);
        RunTo(sim, Blows.LandsAt(sim, blow) + 1);
        Assert.Equal(BlowOutcome.Miss, First(sim, me));
        Assert.Empty(sim.World.Get<Health>(me).Injuries);
    }

    [Fact]
    public void ADodgeAsItLandsLetsItPassThrough()
    {
        var (sim, me, _, blow) = Duel();
        RunTo(sim, blow.Impact - 4);
        sim.ApplyNow(new CombatInputCommand(me, CombatInput.Dodge, sim.Clock.Tick));
        RunTo(sim, Blows.LandsAt(sim, blow) + 1);
        Assert.Equal(BlowOutcome.Dodged, First(sim, me));
        Assert.Empty(sim.World.Get<Health>(me).Injuries);
    }

    [Fact]
    public void AHeavyBlowBreaksATiredGuard()
    {
        var (sim, me, _, blow) = Duel(heavy: true);
        Guard(sim, me, true, sim.Clock.Tick);
        Blows.GuardOf(sim, me).Stamina = 0.1f;
        RunTo(sim, Blows.LandsAt(sim, blow) + 1);
        Assert.Equal(BlowOutcome.GuardBroken, First(sim, me));
        Assert.True(Blows.Staggered(sim, me));
    }

    [Fact]
    public void AHeavyBlowIsSlowerToComeThanALightOne()
    {
        var (_, _, _, light) = Duel();
        var (_, _, _, heavy) = Duel(heavy: true);
        Assert.True(heavy.Impact - heavy.Start > light.Impact - light.Start);
    }

    [Fact]
    public void ABlowWoundUpSurvivesASave()
    {
        var (sim, me, raider, blow) = Duel();
        Guard(sim, me, true, sim.Clock.Tick);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(blow.Impact, loaded.World.Get<Windup>(raider).Impact);
        Assert.Equal(me, loaded.World.Get<Windup>(raider).Target);
        Assert.Equal(sim.World.Get<Guard>(me).RaisedAt, loaded.World.Get<Guard>(me).RaisedAt);
    }
}
