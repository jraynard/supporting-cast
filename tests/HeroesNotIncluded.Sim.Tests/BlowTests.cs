using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Blows seen coming (<see cref="Blows"/>): parry, block, dodge, reach, and a player's late-arriving keys.</summary>
public class BlowTests
{
    /// <summary>
    /// A player with a sword and a foe near them (a swordsman beside them by default; an archer a
    /// few steps off; a dragon), its first blow at them just wound up.
    /// </summary>
    private static (Simulation Sim, Entity Me, Entity Raider, Windup Blow) Duel(bool heavy = false, string foe = "Swordsman")
    {
        var (sim, center) = TestWorld.Millbrook(villagers: 0);
        sim.TickMany(SimClock.TicksPerSecond * 3); // a clock well past zero, as a real fight's is
        var me = VillageRound.SpawnHero(sim, new Hero("Arin", null, null), center);
        sim.Equip(me, sim.Defs.Thing("ShortSword"));
        var here = sim.World.Get<Position>(me).Cell;
        var off = foe == "Archer" ? 4 : 1;
        var at = sim.SpiralFrom(here, off + 2).First(c => c.Z == here.Z && sim.Map.Passable(c) && Maps.Map.Distance(c, here) == off
            && CombatRules.LineOfFire(sim, Position.CenterOf(c) + new float3(0, 0, 0.7f), Position.CenterOf(here) + new float3(0, 0, 0.5f)));
        Entity raider;
        if (foe == "Dragon")
        {
            var def = sim.Defs.AllCreatures.First(c => c.Race == Defs.Race.Dragon);
            raider = sim.SpawnCreature(def, "Old Ashjaw", at, Faction.Predator, ageDays: def.LifeStages[^1].MinAgeDays + 8);
            sim.World.Get<Mind>(raider).Drafted = true; // held where it is: its own mind would take wing
        }
        else
        {
            raider = Raids.SpawnRaider(sim, sim.Defs.AllRaiderKinds.First(k => k.Name == foe), "Mott", at, sim.Rand);
            sim.World.Get<Mind>(raider).Drafted = true; // the one blow under test, nothing of its own besides
        }
        // Weapon drawn, facing them (a guard meets only a blow it faces).
        sim.Tick();
        var them = sim.World.Get<Position>(raider).Pos - sim.World.Get<Position>(me).Pos;
        sim.ApplyNow(new DrawWeaponCommand(me, true));
        sim.ApplyNow(new FreeMoveCommand(me, 0f, 0f, MathF.Atan2(them.Y, them.X)));
        var attack = foe == "Archer" ? CombatRules.Attacks(sim, raider).First(a => a.Ranged) : CombatRules.Attacks(sim, raider).First(a => !a.Ranged);
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
        sim.ApplyNow(new GuardCommand(me, up, at));

    /// <summary>A dodge at <paramref name="at"/>: a dash straight back from the way you face, or toward <paramref name="foe"/>.</summary>
    private static void Dodge(Simulation sim, Entity me, long at, Entity? foe = null)
    {
        var dir = float2.Zero;
        if (foe is { } f)
        {
            var d = sim.World.Get<Position>(f).Pos - sim.World.Get<Position>(me).Pos;
            dir = new float2(d.X, d.Y);
        }
        sim.ApplyNow(new DodgeCommand(me, dir.X, dir.Y, at));
    }

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
        Dodge(sim, me, sim.Clock.Tick);
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

    [Fact]
    public void AnArrowCannotBeParriedOnlyBlocked()
    {
        var (sim, me, raider, shot) = Duel(foe: "Archer");
        Assert.True(shot.Impact - shot.Start >= SimClock.TicksPerSecond * 9 / 10, "a bow's draw is seen coming");
        RunTo(sim, shot.Impact - 4);
        Guard(sim, me, true, sim.Clock.Tick);
        RunTo(sim, Blows.LandsAt(sim, shot) + 1);
        Assert.Equal(BlowOutcome.Blocked, First(sim, me));
        Assert.False(Blows.Staggered(sim, raider));
    }

    [Fact]
    public void AnArrowCanBeDodged()
    {
        var (sim, me, _, shot) = Duel(foe: "Archer");
        RunTo(sim, shot.Impact - 6);
        Dodge(sim, me, sim.Clock.Tick);
        RunTo(sim, Blows.LandsAt(sim, shot) + 1);
        Assert.Equal(BlowOutcome.Dodged, First(sim, me));
    }

    [Fact]
    public void ADragonsJawsAreOnlyPartlyBlockedAndNeverParried()
    {
        var (sim, me, dragon, bite) = Duel(foe: "Dragon");
        RunTo(sim, bite.Impact - 4);
        Guard(sim, me, true, sim.Clock.Tick); // a parry's timing, against a sword
        RunTo(sim, Blows.LandsAt(sim, bite) + 1);
        Assert.Equal(BlowOutcome.Blocked, First(sim, me));
        Assert.False(Blows.Staggered(sim, dragon));
        Assert.True(sim.World.Get<Guard>(me).Stamina <= 1f - Blows.BlockCost * 2f + 0.05f, "it costs twice the wind");
    }

    [Fact]
    public void ADragonIsDodgedOnlyLate()
    {
        var (sim, me, dragon, bite) = Duel(foe: "Dragon");
        // Early, as against a man, and in toward it: a moment you'd be clear of a blade, but its jaws find you.
        RunTo(sim, bite.Impact - Blows.DodgeTicks + 3);
        Dodge(sim, me, sim.Clock.Tick, dragon);
        RunTo(sim, Blows.LandsAt(sim, bite) + 1);
        Assert.NotEqual(BlowOutcome.Dodged, First(sim, me));

        var (sim2, me2, _, bite2) = Duel(foe: "Dragon");
        RunTo(sim2, bite2.Impact - 3);
        Dodge(sim2, me2, sim2.Clock.Tick);
        RunTo(sim2, Blows.LandsAt(sim2, bite2) + 1);
        Assert.Equal(BlowOutcome.Dodged, First(sim2, me2));
    }

    [Fact]
    public void ADragonsBreathIsSeenComingAndAGuardOrALateDodgeOnlyTakesSomeOfIt()
    {
        var (sim, me, dragon, _) = Duel(foe: "Dragon");
        Blows.Cancel(sim, dragon);
        var at = sim.World.Get<Position>(me).Cell;
        var drawn = sim.Clock.Tick;
        Guard(sim, me, true, drawn - 30);
        var (_, guarded) = CombatRules.Breathe(sim, dragon, at, drawn);
        Assert.Contains(sim.RecentBlows, b => b.Target == me && b.Outcome == BlowOutcome.Blocked);
        Assert.True(guarded.Single(b => b.Who == me).Damage > 0, "a guard doesn't keep it all off");

        var (sim2, me2, dragon2, _) = Duel(foe: "Dragon");
        Blows.Cancel(sim2, dragon2);
        var drawn2 = sim2.Clock.Tick;
        Dodge(sim2, me2, drawn2 - 2);
        var (_, dodged) = CombatRules.Breathe(sim2, dragon2, sim2.World.Get<Position>(me2).Cell, drawn2);
        Assert.Contains(sim2.RecentBlows, b => b.Target == me2 && b.Outcome == BlowOutcome.Dodged);
        Assert.True(dodged.Single(b => b.Who == me2).Damage > 0, "nobody gets wholly clear of its fire");
    }
}
