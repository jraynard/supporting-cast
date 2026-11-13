using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Free movement and blows by reach and timing (<see cref="FreeMoveSystem"/>, <see cref="Reach"/>):
/// the arena's prototype. Nothing here rolls to hit.
/// </summary>
public class FreeCombatTests
{
    /// <summary>The ring with free movement on, you in the middle and nobody else yet.</summary>
    private static (Simulation Sim, Entity Me) Ring(string weapon = "ShortSword")
    {
        var sim = Arena.NewSimulation(TestWorld.LoadDefs(), 7);
        var center = Arena.Setup(sim);
        var me = VillageRound.SpawnHero(sim, new Hero("Arin", null, null), center);
        sim.Equip(me, sim.Defs.Thing(weapon));
        sim.FreeMove.Enable(sim);
        Place(sim, me, new float2(16.5f, 16.5f));
        sim.TickMany(SimClock.TicksPerSecond);
        return (sim, me);
    }

    private static void Place(Simulation sim, Entity e, float2 at)
    {
        ref var p = ref sim.World.Get<Position>(e);
        p.Pos = new float3(at.X, at.Y, p.Pos.Z);
        p.Cell = new int3((int)MathF.Floor(at.X), (int)MathF.Floor(at.Y), p.Cell.Z);
    }

    private static float2 Where(Simulation sim, Entity e) => new(sim.World.Get<Position>(e).Pos.X, sim.World.Get<Position>(e).Pos.Y);

    /// <summary>A raider standing still (its own mind held) at <paramref name="at"/>.</summary>
    private static Entity Foe(Simulation sim, float2 at, string kind = "Swordsman")
    {
        var cell = new int3((int)MathF.Floor(at.X), (int)MathF.Floor(at.Y), sim.World.Get<Position>(sim.World.Query<Avatar>()[0]).Cell.Z);
        var foe = Raids.SpawnRaider(sim, sim.Defs.AllRaiderKinds.First(k => k.Name == kind), "Mott", cell, sim.Rand);
        sim.World.Get<Mind>(foe).Drafted = true;
        Place(sim, foe, at);
        return foe;
    }

    private static void RunTo(Simulation sim, long tick)
    {
        while (sim.Clock.Tick < tick)
            sim.Tick();
    }

    private static BlowOutcome? Outcome(Simulation sim, Entity attacker) => sim.RecentBlows.LastOrDefault(b => b.Attacker == attacker)?.Outcome;

    private static void Swing(Simulation sim, Entity me, float aim, bool heavy = false)
    {
        sim.ApplyNow(new AttackCommand(me, heavy, aim));
        var blow = sim.World.Get<Windup>(me);
        RunTo(sim, Blows.LandsAt(sim, blow) + 1);
    }

    [Fact]
    public void YouMoveSmoothlyOffTheGrid()
    {
        var (sim, me) = Ring();
        var from = Where(sim, me);
        sim.ApplyNow(new FreeMoveCommand(me, 0.6f, 0.8f, 0f));
        sim.TickMany(SimClock.TicksPerSecond / 2);
        var to = Where(sim, me);
        Assert.True(to.X - from.X > 0.3f && to.Y - from.Y > 0.4f, $"moved from {from} to {to}");
        Assert.True(MathF.Abs(to.X - MathF.Floor(to.X) - 0.5f) > 0.01f || MathF.Abs(to.Y - MathF.Floor(to.Y) - 0.5f) > 0.01f, "between cell middles");
        sim.ApplyNow(new FreeMoveCommand(me, 0f, 0f, 0f));
        sim.TickMany(SimClock.TicksPerSecond / 3);
        var stopped = Where(sim, me);
        sim.TickMany(10);
        Assert.Equal(stopped, Where(sim, me));
    }

    [Fact]
    public void TheFenceStopsYouAndYouSlideAlongIt()
    {
        var (sim, me) = Ring();
        // Out to the east fence and on into it, a little north as well.
        sim.ApplyNow(new FreeMoveCommand(me, 1f, 0f, 0f));
        sim.TickMany(SimClock.TicksPerSecond * 8);
        var atFence = Where(sim, me);
        Assert.True(sim.Map.Passable(sim.World.Get<Position>(me).Cell), "still inside");
        sim.ApplyNow(new FreeMoveCommand(me, 0.7f, 0.7f, 0f));
        sim.TickMany(SimClock.TicksPerSecond / 2);
        var slid = Where(sim, me);
        Assert.True(MathF.Abs(slid.Y - atFence.Y) > 0.2f, $"slid along it, from {atFence} to {slid}");
        Assert.True(FreeMoveSystem.Fits(sim, slid, sim.World.Get<Position>(me).Cell.Z));
    }

    [Fact]
    public void ASwingLandsOnWhoeverIsInItsSweepAndNobodyElse()
    {
        var (sim, me) = Ring();
        var foe = Foe(sim, new float2(17.6f, 16.5f)); // a step east
        for (var i = 0; i < 6; i++)
        {
            Swing(sim, me, 0f);
            Assert.Equal(BlowOutcome.Hit, Outcome(sim, me)); // in its sweep, it lands: no roll
            sim.TickMany(Blows.PlayerRecoveryTicks + 1);
        }
        var hurt = sim.World.Get<Health>(foe).Injuries.Count;
        Assert.True(hurt > 0);
        // Turned the other way, the same swing reaches nobody.
        Swing(sim, me, MathF.PI);
        Assert.Equal(BlowOutcome.Miss, Outcome(sim, me));
    }

    [Fact]
    public void ABlowCommittedOneWayMissesWhoeverStepsOutOfIt()
    {
        var (sim, me) = Ring();
        var foe = Foe(sim, new float2(17.5f, 16.5f));
        var attack = CombatRules.Attacks(sim, foe).First(a => !a.Ranged);
        Assert.True(Blows.Begin(sim, foe, me, attack));
        var blow = sim.World.Get<Windup>(foe);
        // Step aside, two cells north, before it lands.
        Place(sim, me, new float2(16.5f, 18.6f));
        RunTo(sim, Blows.LandsAt(sim, blow) + 1);
        Assert.Equal(BlowOutcome.Miss, Outcome(sim, foe));
        Assert.Empty(sim.World.Get<Health>(me).Injuries);
    }

    [Fact]
    public void ADodgeDashesYouClearAndBlowsPassThroughIt()
    {
        var (sim, me) = Ring();
        var foe = Foe(sim, new float2(17.5f, 16.5f));
        var attack = CombatRules.Attacks(sim, foe).First(a => !a.Ranged);
        Assert.True(Blows.Begin(sim, foe, me, attack));
        var blow = sim.World.Get<Windup>(foe);
        RunTo(sim, blow.Impact - 4);
        var from = Where(sim, me);
        sim.ApplyNow(new DodgeCommand(me, 0f, 0f, sim.Clock.Tick)); // standing still: straight back from the aim
        RunTo(sim, Blows.LandsAt(sim, blow) + 1);
        Assert.Equal(BlowOutcome.Dodged, Outcome(sim, foe));
        sim.TickMany(FreeMoveSystem.DashTicks);
        var d = Where(sim, me) - from;
        Assert.True(MathF.Sqrt(d.X * d.X + d.Y * d.Y) > 1.5f, $"dashed {d}");
    }

    [Fact]
    public void ASpearReachesFarStraightAheadButNotToTheSide()
    {
        var (sim, me) = Ring("IronSpear");
        Foe(sim, new float2(18.4f, 16.5f)); // two steps east
        Swing(sim, me, 0f);
        Assert.NotEqual(BlowOutcome.Miss, Outcome(sim, me));
        var (sim2, me2) = Ring("IronSpear");
        Foe(sim2, new float2(17.4f, 17.4f)); // a step off to the north-east: outside a spear's line
        Swing(sim2, me2, 0f);
        Assert.Equal(BlowOutcome.Miss, Outcome(sim2, me2));
        var (sim3, me3) = Ring();
        Foe(sim3, new float2(18.4f, 16.5f)); // two steps east: beyond a short sword
        Swing(sim3, me3, 0f);
        Assert.Equal(BlowOutcome.Miss, Outcome(sim3, me3));
    }

    [Fact]
    public void AnArrowFliesStraightAndStrikesTheFirstInItsLine()
    {
        var (sim, me) = Ring("Bow");
        var near = Foe(sim, new float2(20.5f, 16.5f));
        var far = Foe(sim, new float2(23.5f, 16.5f));
        Swing(sim, me, 0f);
        var shot = sim.RecentBlows.Last(b => b.Attacker == me);
        Assert.Equal(near, shot.Target);
        Assert.DoesNotContain(sim.RecentBlows, b => b.Target == far);
    }

    [Fact]
    public void AGuardMeetsOnlyBlowsItFaces()
    {
        var (sim, me) = Ring();
        var foe = Foe(sim, new float2(17.5f, 16.5f));
        var attack = CombatRules.Attacks(sim, foe).First(a => !a.Ranged);
        // Guard up, facing west: the blow comes from the east, behind you.
        sim.ApplyNow(new FreeMoveCommand(me, 0f, 0f, MathF.PI));
        sim.ApplyNow(new CombatInputCommand(me, CombatInput.GuardUp, sim.Clock.Tick));
        Assert.True(Blows.Begin(sim, foe, me, attack));
        RunTo(sim, Blows.LandsAt(sim, sim.World.Get<Windup>(foe)) + 1);
        Assert.Equal(BlowOutcome.Hit, Outcome(sim, foe));
        // Turned to face it, the next is blocked.
        sim.ApplyNow(new FreeMoveCommand(me, 0f, 0f, 0f));
        sim.TickMany(Blows.StaggerTicks);
        Assert.True(Blows.Begin(sim, foe, me, attack));
        RunTo(sim, Blows.LandsAt(sim, sim.World.Get<Windup>(foe)) + 1);
        Assert.Equal(BlowOutcome.Blocked, Outcome(sim, foe));
    }

    [Fact]
    public void FreeMovementAndItsAimSurviveASave()
    {
        var (sim, me) = Ring();
        sim.ApplyNow(new FreeMoveCommand(me, 1f, 0f, 1.2f));
        sim.TickMany(7);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.True(loaded.FreeMove.Enabled);
        Assert.Equal(sim.World.Get<FreeMover>(me).Aim, loaded.World.Get<FreeMover>(me).Aim);
        Assert.Equal(sim.World.Get<Position>(me).Pos, loaded.World.Get<Position>(me).Pos);
    }
}
