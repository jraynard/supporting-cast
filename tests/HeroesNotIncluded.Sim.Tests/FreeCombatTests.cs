using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;

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
        var me = VillageRound.SpawnHero(sim, new Hero("Arin", null), center);
        sim.Equip(me, sim.Defs.Thing(weapon));
        sim.ApplyNow(new DrawWeaponCommand(me, true));
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
    public void ClaimingToBeAheadCantOutrunWalking()
    {
        // A modified co-op client claims to be 2.4 cells ahead in every turn's command.
        var (sim, me) = Ring();
        var from = Where(sim, me);
        var speed = FreeMoveSystem.Wanted(sim, me, new float2(1, 0), true, null).X;
        const int ticks = SimClock.TicksPerSecond / 2;
        for (var t = 0; t < ticks; t += 2)
        {
            var at = Where(sim, me);
            sim.ApplyNow(new FreeMoveCommand(me, 1f, 0f, 0f, at.X + 2.4f, at.Y, sim.World.Get<Position>(me).Cell.Z));
            sim.TickMany(2);
        }
        var walked = speed * ticks;
        var gained = Where(sim, me).X - from.X - walked;
        Assert.True(gained <= FreeMoveSystem.MaxClaim + FreeMoveSystem.ClaimRefill * ticks + 0.05f,
            $"walking goes {walked:F2} cells in half a second; claims took it {gained:F2} further");
    }

    [Fact]
    public void AnHonestClientsClaimsAreTakenWhole()
    {
        // A client predicting its own walk runs 150 ms ahead of the sim, and says where it is every 0.2 s.
        var (sim, me) = Ring();
        var speed = FreeMoveSystem.Wanted(sim, me, new float2(1, 0), true, null).X;
        var client = Where(sim, me).X + speed * 9;
        sim.ApplyNow(new FreeMoveCommand(me, 1f, 0f, 0f));
        for (var t = 0; t < SimClock.TicksPerSecond * 2; t++)
        {
            if (t % 12 == 0)
            {
                sim.ApplyNow(new FreeMoveCommand(me, 1f, 0f, 0f, client, Where(sim, me).Y, sim.World.Get<Position>(me).Cell.Z));
                Assert.Equal(client, Where(sim, me).X, 3);
            }
            sim.Tick();
            client += speed;
        }
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
    public void ABlowAtSomeoneStandingGoesOverWhoeverLiesAtTheirFeet()
    {
        var (sim, me) = Ring();
        var foe = Foe(sim, new float2(17.6f, 16.5f));
        var fallen = Foe(sim, new float2(17.3f, 16.7f));
        ref var c = ref sim.World.Get<Creature>(fallen);
        HealthSystem.SetDowned(sim, fallen, ref c, true);
        var attack = CombatRules.Attacks(sim, me).First(a => !a.Ranged);
        var struck = Reach.Struck(sim, me, attack, 0f);
        Assert.Contains(foe, struck);
        Assert.DoesNotContain(fallen, struck);

        // With nobody standing in it, a blow at the fallen lands.
        Place(sim, foe, new float2(16.5f, 19.5f));
        Assert.Equal([fallen], Reach.Struck(sim, me, attack, 0f));
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
        sim.ApplyNow(new BowCommand(me, true, false, 0f, sim.Clock.Tick));
        sim.TickMany(Archery.DrawTicks(sim, me, Archery.Bow(sim, me)!));
        sim.ApplyNow(new BowCommand(me, true, true, 0f, sim.Clock.Tick));
        sim.TickMany(2);
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
        sim.ApplyNow(new GuardCommand(me, true, sim.Clock.Tick));
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
        Assert.Equal(sim.World.Get<FreeMover>(me).Aim, loaded.World.Get<FreeMover>(me).Aim);
        Assert.Equal(sim.World.Get<Position>(me).Pos, loaded.World.Get<Position>(me).Pos);
    }

    // ---- In the village (the setting): the town's levels, its jobs, its people. ----

    /// <summary>Millbrook with free movement on and you in the square, weapon sheathed.</summary>
    private static (Simulation Sim, Entity Me) Village()
    {
        var (sim, center) = TestWorld.Millbrook(villagers: 3);
        var me = VillageRound.SpawnHero(sim, new Hero("Arin", null), center);
        sim.Equip(me, sim.Defs.Thing("ShortSword"));
        sim.TickMany(10);
        return (sim, me);
    }

    [Fact]
    public void AClickedWalkStillTakesYouThere()
    {
        var (sim, me) = Village();
        var here = sim.World.Get<Position>(me).Cell;
        var there = sim.SpiralFrom(here, 8).First(c => Maps.Map.Distance(c, here) >= 5 && sim.Map.Passable(c) && sim.Regions.CanReach(here, c, PathEnd.OnTile));
        sim.ApplyNow(new AvatarGoToCommand(me, there));
        for (var t = 0; t < SimClock.TicksPerSecond * 20 && sim.World.Get<Position>(me).Cell != there; t++)
            sim.Tick();
        Assert.Equal(there, sim.World.Get<Position>(me).Cell);
    }

    [Fact]
    public void WalkingOntoAStairOrSlopeTakesYouUpOrDownIt()
    {
        var (sim, me) = Village();
        var map = sim.Map;
        // A level change the walking links allow, from the open ground: one cell to a neighbour a level up or down.
        Span<Narsil.Sim.Grid.NavGraph.Step> steps = stackalloc Narsil.Sim.Grid.NavGraph.Step[Narsil.Sim.Grid.NavGraph.MaxSteps];
        (int3 From, int3 To)? climb = null;
        for (var i = 0; i < map.Block.Length && climb == null; i++)
        {
            var c = map.CellOf(i);
            if (!map.Passable(c) || !map.Standable(i))
                continue;
            var n = Narsil.Sim.Grid.NavGraph.Neighbors(new Maps.MapNav(map), i, steps);
            // Straight (not diagonal), with no step on the level that way: a step there must climb.
            for (var k = 0; k < n && climb == null; k++)
            {
                var to = map.CellOf(steps[k].Index);
                if (to.Z == c.Z || (to.X == c.X) == (to.Y == c.Y))
                    continue;
                var level = false;
                for (var j = 0; j < n; j++)
                    level |= map.CellOf(steps[j].Index) == to with { Z = c.Z };
                if (!level)
                    climb = (c, to);
            }
        }
        Assert.True(climb != null, "Millbrook has a slope or stair somewhere");
        var (from, dest) = climb!.Value;
        sim.World.Get<Position>(me) = Position.At(from);
        sim.ApplyNow(new AvatarMoveCommand(me, new int2(dest.X - from.X, dest.Y - from.Y)));
        var trace = new List<string>();
        for (var t = 0; t < SimClock.TicksPerSecond * 2 && sim.World.Get<Position>(me).Cell.Z == from.Z; t++)
        {
            sim.Tick();
            if (t % 10 == 0)
                trace.Add($"{sim.World.Get<Position>(me).Pos} {sim.World.Get<Position>(me).Cell} vel={sim.World.Get<FreeMover>(me).Vel}");
        }
        Assert.True(dest.Z == sim.World.Get<Position>(me).Cell.Z, $"{from} -> {dest}\n" + string.Join("\n", trace));
        Assert.Equal(dest.Z, sim.World.Get<Position>(me).Pos.Z);
    }

    [Fact]
    public void OffTheTopOfAStairIsTheFloorAboveNotTheRoomUnderIt()
    {
        var (sim, me) = Village();
        var map = sim.Map;
        // A stair whose climb has the room below open too: walking on, the floor above is where you go.
        int3? stair = null;
        for (var i = 0; i < map.Block.Length && stair == null; i++)
        {
            if (map.Climb[i] != Narsil.Sim.Grid.ClimbKind.Ramp)
                continue;
            var c = map.CellOf(i);
            var d = map.ClimbOf(c.X, c.Y, c.Z);
            var ahead = new int3(c.X + d.X, c.Y + d.Y, c.Z);
            if (map.Passable(ahead) && map.Standable(map.Idx(ahead)) && map.Standable(map.Idx(ahead with { Z = c.Z + 1 })))
                stair = c;
        }
        Assert.True(stair != null, "Millbrook has a stair up from a room");
        var from = stair!.Value;
        var climb = map.ClimbOf(from.X, from.Y, from.Z);
        sim.World.Get<Position>(me) = Position.At(from);
        sim.ApplyNow(new AvatarMoveCommand(me, climb));
        for (var t = 0; t < SimClock.TicksPerSecond * 2 && sim.World.Get<Position>(me).Cell.Z == from.Z; t++)
            sim.Tick();
        Assert.Equal(from.Z + 1, sim.World.Get<Position>(me).Cell.Z);
        Assert.Equal(from.Z + 1, sim.World.Get<Position>(me).Pos.Z);
    }

    [Fact]
    public void ASheathedWeaponDoesntStrikeAndABlowDrawsIt()
    {
        var (sim, me) = Village();
        Assert.False(sim.World.Get<FreeMover>(me).Drawn);
        sim.ApplyNow(new AttackCommand(me, false, 0f));
        Assert.False(sim.World.Has<Windup>(me), "sheathed: nothing to swing");
        var at = sim.World.Get<Position>(me).Pos;
        var foe = Foe(sim, new float2(at.X + 1f, at.Y));
        var attack = CombatRules.Attacks(sim, foe).First(a => !a.Ranged);
        Assert.True(Blows.Begin(sim, foe, me, attack));
        RunTo(sim, Blows.LandsAt(sim, sim.World.Get<Windup>(foe)) + 1);
        Assert.True(sim.World.Get<FreeMover>(me).Drawn, "a blow landing draws your weapon");
        sim.ApplyNow(new DrawWeaponCommand(me, false));
        Assert.False(sim.World.Get<FreeMover>(me).Drawn);
    }

    [Fact]
    public void AFreeSwingCutsWhoeverIsInItFriendOrFoe()
    {
        var (sim, me) = Village();
        sim.ApplyNow(new DrawWeaponCommand(me, true));
        var at = sim.World.Get<Position>(me).Pos;
        // A neighbour standing in the way, and a hare.
        var friend = sim.World.Query<Creature>().First(e => e != me && sim.World.Get<Creature>(e).Faction == Faction.Village);
        sim.World.Get<Mind>(friend).Drafted = true;
        Systems.AiSystem.EndJob(sim, friend, sim.World.Get<Mind>(friend), false);
        sim.World.Get<Mover>(friend).Stop(); // stands where put, on your level
        sim.World.Get<Position>(friend) = sim.World.Get<Position>(me);
        Place(sim, friend, new float2(at.X + 1f, at.Y));
        Swing(sim, me, 0f);
        Assert.True(sim.RecentBlows.Any(b => b.Attacker == me && b.Target == friend && b.Outcome != BlowOutcome.Miss),
            $"me {sim.World.Get<Position>(me).Pos} {sim.World.Get<Position>(me).Cell} friend {sim.World.Get<Position>(friend).Pos} {sim.World.Get<Position>(friend).Cell} aim {sim.World.Get<FreeMover>(me).Aim}");
        Assert.NotEmpty(sim.World.Get<Health>(friend).Injuries);

        var hare = sim.SpawnCreature(sim.Defs.Creature("Hare"), "Hare", sim.World.Get<Position>(me).Cell, Faction.Wild);
        sim.World.Get<Mind>(hare).Drafted = true;
        Place(sim, hare, new float2(at.X, at.Y - 1f));
        sim.TickMany(Blows.PlayerRecoveryTicks + 1);
        Swing(sim, me, -MathF.PI / 2);
        Assert.Contains(sim.RecentBlows, b => b.Attacker == me && b.Target == hare);
    }

    // ---- Friendly fire (RoundOptions): settled where the blow lands, the same on every peer. ----

    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bea = new("00000000-0000-0000-0000-00000000000b");

    /// <summary>A round under way in Millbrook, Ana and Bea dealt in, Ana's sword drawn; friendly fire as asked.</summary>
    private static (Simulation Sim, Entity Me, Entity Other) Round(bool friendlyFire) => Round(friendlyFire, out _);

    private static (Simulation Sim, Entity Me, Entity Other) Round(bool friendlyFire, out int3 square)
    {
        var (sim, center) = TestWorld.Millbrook(villagers: 4);
        square = center;
        sim.Round.OpenLobby();
        sim.ApplyNow(new SetRoundOptionsCommand(friendlyFire, false));
        foreach (var (who, name) in new[] { (Ana, "Ana"), (Bea, "Bea") })
            sim.ApplyNow(new JoinLobbyCommand(who, name, default));
        sim.ApplyNow(new StartRoundCommand());
        Assert.True(sim.Round.Live);
        var me = Players.CharacterOf(sim, Ana);
        var other = Players.CharacterOf(sim, Bea);
        sim.Equip(me, sim.Defs.Thing("ShortSword"));
        if (Combat.HouseRules.NoSteel(sim, me)) // dealt the inn: out to the square, where steel's allowed
        {
            var open = sim.SpiralFrom(center, 6).First(c => sim.Map.Passable(c) && !Combat.HouseRules.NoSteelAt(sim, c));
            sim.World.Get<Position>(me).Cell = open;
            sim.World.Get<Position>(me).Pos = Position.CenterOf(open);
        }
        sim.ApplyNow(new DrawWeaponCommand(me, true));
        sim.TickMany(10);
        return (sim, me, other);
    }

    /// <summary>Holds <paramref name="e"/> still at <paramref name="offset"/> from <paramref name="me"/>, on their level.</summary>
    private static void StandBy(Simulation sim, Entity me, Entity e, float2 offset)
    {
        sim.World.Get<Mind>(e).Drafted = true;
        Systems.AiSystem.EndJob(sim, e, sim.World.Get<Mind>(e), false);
        sim.World.Get<Mover>(e).Stop();
        sim.World.Get<Position>(e) = sim.World.Get<Position>(me);
        var at = sim.World.Get<Position>(me).Pos;
        Place(sim, e, new float2(at.X + offset.X, at.Y + offset.Y));
    }

    private static Entity Townsfolk(Simulation sim, params Entity[] not) =>
        sim.Creatures().First(e => !not.Contains(e) && !sim.World.Has<PlayerCharacter>(e)
            && sim.World.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human, Dead: false });

    [Fact]
    public void WithFriendlyFireOffYourSwingPassesThroughTheTownButNotARaider()
    {
        var (sim, me, other) = Round(friendlyFire: false);
        var folk = Townsfolk(sim, me, other);
        StandBy(sim, me, other, new float2(1f, 0.3f));
        StandBy(sim, me, folk, new float2(1f, -0.3f));
        var at = sim.World.Get<Position>(me).Pos;
        var foe = Foe(sim, new float2(at.X + 1.2f, at.Y));
        Swing(sim, me, 0f);
        Assert.Empty(sim.World.Get<Health>(other).Injuries);
        Assert.Empty(sim.World.Get<Health>(folk).Injuries);
        Assert.DoesNotContain(sim.RecentBlows, b => b.Attacker == me && (b.Target == other || b.Target == folk));
        Assert.Contains(sim.RecentBlows, b => b.Attacker == me && b.Target == foe);
    }

    [Fact]
    public void WithFriendlyFireOnYourSwingCutsAnotherPlayer()
    {
        var (sim, me, other) = Round(friendlyFire: true);
        StandBy(sim, me, other, new float2(1f, 0f));
        Swing(sim, me, 0f);
        Assert.Contains(sim.RecentBlows, b => b.Attacker == me && b.Target == other && b.Outcome != BlowOutcome.Miss);
    }

    [Fact]
    public void WithFriendlyFireOffAnArrowFliesPastANeighbourToTheRaiderBehind()
    {
        var (sim, me, other) = Round(friendlyFire: false, out var square);
        sim.Equip(me, sim.Defs.Thing("Bow"));
        // Out into the open: a row of five clear cells east from somewhere near the square.
        var here = sim.World.Get<Position>(me);
        var start = sim.SpiralFrom(square, 30)
            .First(c => Enumerable.Range(0, 6).All(dx => sim.Map.Passable(c + new int3(dx, 0, 0))));
        var meAt = here;
        meAt.Cell = start;
        meAt.Pos = new float3(start.X + 0.5f, start.Y + 0.5f, here.Pos.Z - here.Cell.Z + start.Z);
        sim.World.Get<Position>(me) = meAt;
        sim.TickMany(2);
        StandBy(sim, me, other, new float2(2f, 0f));
        var at = sim.World.Get<Position>(me).Pos;
        var foe = Foe(sim, new float2(at.X + 4f, at.Y));
        Assert.True(CombatRules.LineOfFire(sim, at + new float3(0, 0, 0.7f), sim.World.Get<Position>(foe).Pos + new float3(0, 0, 0.5f)),
            "the test needs a clear line down the square");
        sim.ApplyNow(new BowCommand(me, true, false, 0f, sim.Clock.Tick));
        sim.TickMany(Archery.DrawTicks(sim, me, Archery.Bow(sim, me)!));
        sim.ApplyNow(new BowCommand(me, true, true, 0f, sim.Clock.Tick));
        sim.TickMany(2);
        Assert.Equal(foe, sim.RecentBlows.Last(b => b.Attacker == me).Target);
        Assert.Empty(sim.World.Get<Health>(other).Injuries);
    }

    [Fact]
    public void WithFriendlyFireOffARaiderStillCutsTheTown()
    {
        var (sim, me, _) = Round(friendlyFire: false);
        var at = sim.World.Get<Position>(me).Pos;
        var foe = Foe(sim, new float2(at.X + 1f, at.Y));
        var attack = CombatRules.Attacks(sim, foe).First(a => !a.Ranged);
        Assert.True(Blows.Begin(sim, foe, me, attack));
        RunTo(sim, Blows.LandsAt(sim, sim.World.Get<Windup>(foe)) + 1);
        Assert.Equal(BlowOutcome.Hit, Outcome(sim, foe));
    }

    [Fact]
    public void WithFriendlyFireOffAVillagerDoesntHoldItsBlowForAFriend()
    {
        var (sim, me, other) = Round(friendlyFire: false);
        StandBy(sim, me, other, new float2(1f, 0f));
        var at = sim.World.Get<Position>(me).Pos;
        var foe = Foe(sim, new float2(at.X + 2f, at.Y));
        var attack = CombatRules.Attacks(sim, me).First(a => !a.Ranged);
        Assert.False(Reach.FriendInTheWay(sim, me, attack, 0f, foe));
        sim.ApplyNow(new SetRoundOptionsCommand(true, false)); // too late: the lobby's closed
        Assert.False(Reach.FriendInTheWay(sim, me, attack, 0f, foe));
    }

    [Fact]
    public void OutsideARoundTheTownsBlowsStillHurtTheTown()
    {
        var (sim, _) = TestWorld.Millbrook(villagers: 2);
        Assert.False(sim.Round.Options.FriendlyFire);
        Assert.True(sim.Round.FriendlyFire, "tests, demos and the bench play as they always have");
    }

    [Fact]
    public void AnAiHoldsItsBlowWhileAFriendIsInTheWay()
    {
        var (sim, me) = Village();
        var at = sim.World.Get<Position>(me).Pos;
        var foe = Foe(sim, new float2(at.X + 2f, at.Y));
        var mate = Foe(sim, new float2(at.X + 1f, at.Y), "Brawler"); // between them
        var attack = CombatRules.Attacks(sim, foe).First(a => !a.Ranged);
        Assert.False(Blows.Begin(sim, foe, me, attack), "its own mate is in the sweep");
        Place(sim, mate, new float2(at.X + 2f, at.Y + 2f));
        Place(sim, foe, new float2(at.X + 1f, at.Y));
        Assert.True(Blows.Begin(sim, foe, me, attack));
    }

    [Fact]
    public void ACharacterWhoJoinsLaterMovesFreelyToo()
    {
        var (sim, _) = Village();
        var late = VillageRound.SpawnHero(sim, new Hero("Bea", null), sim.World.Get<Position>(sim.World.Query<Avatar>()[0]).Cell);
        sim.Tick();
        Assert.True(sim.World.Has<FreeMover>(late));
        sim.World.Remove<Avatar>(late); // they left: the AI has them again
        sim.Tick();
        Assert.False(sim.World.Has<FreeMover>(late));
    }

    // ---- AI fighters with free movement (Combat.FreeFight) ----

    [Fact]
    public void ARaiderClosesSmoothlyAndStrikesFromItsReach()
    {
        var (sim, me) = Ring();
        var foe = Arena.Bout(sim, me, "Swordsman");
        sim.World.Get<Mind>(me).Drafted = true;
        var offGrid = false;
        BlowEvent? first = null;
        for (var t = 0; t < SimClock.TicksPerSecond * 20 && first == null; t++)
        {
            sim.Tick();
            offGrid |= sim.World.TryGet<FreeMover>(foe, out var fm) && fm.Wish != float2.Zero; // steering, not stepping cells
            first = sim.RecentBlows.FirstOrDefault(b => b.Attacker == foe);
        }
        Assert.NotNull(first);
        Assert.True(sim.World.Has<FreeMover>(foe), "it fights off the grid");
        Assert.True(offGrid, "it steered in close");
        var d = Where(sim, foe) - Where(sim, me);
        Assert.True(MathF.Sqrt(d.X * d.X + d.Y * d.Y) <= Reach.ShapeOf(sim, foe, CombatRules.Attacks(sim, foe)[0]).Length + Reach.BodyRadius + 0.2f);
    }

    [Fact]
    public void YouCanSidestepAnAiBlow()
    {
        var (sim, me) = Ring();
        var foe = Arena.Bout(sim, me, "Swordsman");
        var dodged = 0;
        for (var t = 0; t < SimClock.TicksPerSecond * 40 && dodged == 0; t++)
        {
            // Whenever it winds up, step smartly to the side of its aim.
            if (sim.World.TryGet<Windup>(foe, out var blow))
            {
                var side = new float2(-MathF.Sin(blow.Aim), MathF.Cos(blow.Aim));
                sim.ApplyNow(new FreeMoveCommand(me, side.X, side.Y, 0f));
            }
            else
                sim.ApplyNow(new FreeMoveCommand(me, 0f, 0f, 0f));
            sim.Tick();
            dodged = sim.RecentBlows.Count(b => b.Attacker == foe && b.Outcome == BlowOutcome.Miss);
        }
        Assert.True(dodged > 0, "stepping out of its sweep makes it miss");
    }

    [Fact]
    public void AnArcherKeepsItsRangeAndBacksOffWhenYouClose()
    {
        var (sim, me) = Ring();
        var archer = Arena.Bout(sim, me, "Archer");
        sim.TickMany(SimClock.TicksPerSecond * 4);
        var d0 = Where(sim, archer) - Where(sim, me);
        Assert.True(MathF.Sqrt(d0.X * d0.X + d0.Y * d0.Y) > 2.5f, "it stands off");
        // Run at it: it gives ground.
        var start = Where(sim, archer);
        for (var t = 0; t < SimClock.TicksPerSecond * 2; t++)
        {
            var to = Where(sim, archer) - Where(sim, me);
            sim.ApplyNow(new FreeMoveCommand(me, to.X, to.Y, 0f));
            sim.Tick();
        }
        var moved = Where(sim, archer) - start;
        Assert.True(MathF.Sqrt(moved.X * moved.X + moved.Y * moved.Y) > 0.5f, "it backed off");
    }

    [Fact]
    public void ARaidWithFreeMovementIsFoughtFreelyAndOnlyFightersLeaveTheGrid()
    {
        var (sim, me) = Village();
        var w = sim.World;
        // The town's people armed, so they go out to meet it (Ai/Defence), and a band they can
        // stand up to: the test is the fight's shape, not its odds (the game's raids are harder).
        var defenders = sim.Creatures().Where(e => e != me && w.Get<Creature>(e).Faction == Faction.Village && w.Get<Creature>(e).Def.Name == "Human").ToList();
        Assert.NotEmpty(defenders);
        foreach (var e in defenders)
            sim.Equip(e, sim.Defs.Thing("ShortSword"));
        var home = w.Get<Position>(me).Cell;
        var band = Raids.Spawn(sim, Enumerable.Repeat(sim.Defs.AllRaiderKinds.First(k => k.Name == "Swordsman"), 2).ToList(),
            Raids.EdgeEntry(sim, 2, home)!.Value, home, sim.Rand);
        var defended = false;
        var sawFree = false;
        for (var t = 0; t < SimClock.TicksPerSecond * 120; t++)
        {
            sim.Tick();
            defended |= sim.RecentBlows.Any(b => defenders.Contains(b.Attacker) && band.Contains(b.Target)
                && b.Outcome is not (BlowOutcome.Miss or BlowOutcome.Dodged or BlowOutcome.Blocked or BlowOutcome.Parried));
            foreach (var e in sim.World.Query<FreeMover>())
            {
                if (sim.World.Has<Avatar>(e))
                    continue;
                sawFree = true;
                // Off the grid only while fighting: a fight job, else on its way back this tick.
                Assert.True(sim.World.Get<Mind>(e).Job is Ai.AssaultJob or Ai.DefendJob or Ai.AnimalAttackJob,
                    $"{sim.World.Get<Creature>(e).Name} is off the grid doing {sim.World.Get<Mind>(e).Job?.Report ?? "nothing"}");
            }
            if (t == SimClock.TicksPerSecond * 60)
            {
                // Jobs aren't saved: a load ends every fight, so the AI's back on the grid and only you move freely.
                var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
                Assert.True(loaded.World.Has<FreeMover>(me));
                Assert.DoesNotContain(loaded.World.Query<FreeMover>(), e => !loaded.World.Has<Avatar>(e));
            }
        }
        Assert.True(sawFree, "the fight was fought freely");
        Assert.True(defended, "the town's armed went out and landed blows on the band\n" + string.Join("\n", sim.Messages.Messages.TakeLast(15).Select(m => m.Text)));
        Assert.Contains(band, r => !sim.World.IsAlive(r) || !sim.World.Get<Creature>(r).Able || sim.World.Get<Health>(r).Injuries.Count > 0);
    }

    [Fact]
    public void BotsDrawAndFightARaidTheFreeWay()
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        var bots = new List<Bots.BotPlayer>();
        for (var i = 0; i < 2; i++)
        {
            var id = new Guid(i + 1, 0, 0, new byte[8]);
            sim.ApplyNow(new JoinLobbyCommand(id, $"Bot {i + 1}", default));
            bots.Add(new Bots.BotPlayer(() => sim, id, c => sim.ApplyNow(c), 50 + (ulong)i));
        }
        sim.ApplyNow(new StartRoundCommand());
        var players = sim.World.Query<Avatar>();
        foreach (var e in players)
            sim.Equip(e, sim.Defs.Thing("ShortSword"));
        var at = sim.World.Get<Position>(players[0]).Cell;
        var band = Raids.Spawn(sim, [sim.Defs.AllRaiderKinds.First(k => k.Name == "Brawler")], sim.SpiralFrom(at, 6).First(c => Maps.Map.Distance(c, at) >= 4 && sim.Map.Passable(c)), at, sim.Rand);
        var struck = false;
        for (var t = 0; t < SimClock.TicksPerSecond * 60 && !struck; t++)
        {
            sim.Tick();
            foreach (var b in bots)
                b.Tick();
            struck = sim.RecentBlows.Any(b => players.Contains(b.Attacker) && band.Contains(b.Target) && b.Outcome != BlowOutcome.Miss);
        }
        Assert.True(struck, "a bot drew and struck the raider");
    }

    [Fact]
    public void AClaimedPositionIsTakenOnlyWhenItsNearAndSomewhereToStand()
    {
        var (sim, me) = Ring();
        var at = Where(sim, me);
        var z = sim.World.Get<Position>(me).Cell.Z;
        sim.ApplyNow(new FreeMoveCommand(me, 0f, 0f, 0f, at.X + 1f, at.Y, z));
        Assert.Equal(at.X + 1f, Where(sim, me).X, 3);
        // Too far, into the fence, nonsense: ignored.
        sim.ApplyNow(new FreeMoveCommand(me, 0f, 0f, 0f, at.X + 9f, at.Y, z));
        sim.ApplyNow(new FreeMoveCommand(me, 0f, 0f, 0f, float.PositiveInfinity, at.Y, z));
        sim.ApplyNow(new FreeMoveCommand(me, 0f, 0f, 0f, at.X + 1f, at.Y, z + 5));
        Assert.Equal(at.X + 1f, Where(sim, me).X, 3);
        Place(sim, me, new float2(25.5f, 16.5f)); // by the east fence
        sim.ApplyNow(new FreeMoveCommand(me, 0f, 0f, 0f, 27.2f, 16.5f, z));
        Assert.Equal(25.5f, Where(sim, me).X, 3);
    }

    [Fact]
    public void ALateSwingIsJudgedAgainstWhereTheyWereWhenItWasPressed()
    {
        // In co-op the swing reaches the host a moment after it was pressed: a foe in reach then,
        // who's stepped out of it since, is still struck. Without the lag, the same swing misses.
        foreach (var lagged in new[] { true, false })
        {
            var (sim, me) = Ring();
            var foe = Foe(sim, new float2(17.5f, 16.5f)); // a step east, in reach
            sim.TickMany(TrailSystem.Every * 4); // its trail has it there
            var pressed = sim.Clock.Tick;
            sim.TickMany(20); // the command's on its way
            sim.ApplyNow(new AttackCommand(me, false, 0f, Pressed: lagged ? pressed : -1));
            var blow = sim.World.Get<Windup>(me);
            // Still there when it would have landed, had it gone out as it was pressed; gone by the time it does.
            RunTo(sim, blow.Impact - 8);
            Place(sim, foe, new float2(16.5f, 19.5f));
            RunTo(sim, Blows.LandsAt(sim, blow) + 1);
            var outcome = sim.RecentBlows.Last(b => b.Attacker == me).Outcome;
            if (lagged)
                Assert.NotEqual(BlowOutcome.Miss, outcome);
            else
                Assert.Equal(BlowOutcome.Miss, outcome);
        }
    }

    [Fact]
    public void ATrailRemembersWhereSomeoneWas()
    {
        var (sim, me) = Ring();
        var from = sim.World.Get<Position>(me).Pos;
        sim.ApplyNow(new FreeMoveCommand(me, 1f, 0f, 0f));
        sim.TickMany(40);
        var now = sim.World.Get<Position>(me).Pos;
        var then = Trails.At(sim, me, 40);
        Assert.True(MathF.Abs(then.X - from.X) < 0.15f, $"40 ticks ago at {then}, started from {from}");
        Assert.Equal(now, Trails.At(sim, me, 0));
    }

    // ---- A player's bow (Combat.Archery) ----

    [Fact]
    public void ABowIsDrawnHeldAndLoosedOnlyOnceFullyDrawn()
    {
        var (sim, me) = Ring("Bow");
        var foe = Foe(sim, new float2(21.5f, 16.5f)); // five cells east
        var bow = Archery.Bow(sim, me)!;
        // A left click alone does nothing with a bow.
        sim.ApplyNow(new AttackCommand(me, false, 0f));
        Assert.False(sim.World.Has<Windup>(me));

        sim.ApplyNow(new BowCommand(me, true, false, 0f, sim.Clock.Tick));
        Assert.True(sim.World.Has<Aiming>(me));
        // Too soon: the string's not back yet.
        sim.TickMany(3);
        sim.ApplyNow(new BowCommand(me, true, true, 0f, sim.Clock.Tick));
        Assert.False(sim.World.Has<Windup>(me));
        // Fully drawn: it flies, at once, and the next is drawn while it's held.
        sim.TickMany(Archery.DrawTicks(sim, me, bow));
        Assert.True(Archery.Ready(sim, me));
        sim.ApplyNow(new BowCommand(me, true, true, 0f, sim.Clock.Tick));
        sim.TickMany(2);
        Assert.Contains(sim.RecentBlows, b => b.Attacker == me && b.Target == foe && b.Outcome != BlowOutcome.Miss);
        Assert.True(sim.World.Has<Aiming>(me) && !Archery.Ready(sim, me), "drawing the next");

        // Aimed wide, it misses.
        sim.TickMany(Archery.DrawTicks(sim, me, bow));
        sim.ApplyNow(new BowCommand(me, true, true, MathF.PI / 2, sim.Clock.Tick));
        sim.TickMany(2);
        Assert.Equal(BlowOutcome.Miss, Outcome(sim, me));

        // Let down, or a guard: no longer drawn.
        sim.ApplyNow(new BowCommand(me, false, false, 0f, sim.Clock.Tick));
        Assert.False(sim.World.Has<Aiming>(me));
        sim.ApplyNow(new BowCommand(me, true, false, 0f, sim.Clock.Tick));
        sim.ApplyNow(new GuardCommand(me, true, sim.Clock.Tick));
        Assert.False(sim.World.Has<Aiming>(me));
    }

    [Fact]
    public void ACrossbowStaysLoadedOnceCrankedUntilItsLoosed()
    {
        var (sim, me) = Ring("Crossbow");
        var foe = Foe(sim, new float2(21.5f, 16.5f));
        var crossbow = Archery.Bow(sim, me)!;
        var item = Archery.Crossbow(sim, me);
        Assert.False(item.IsNull);

        // Let go halfway: nothing's held, and it isn't loaded.
        sim.ApplyNow(new BowCommand(me, true, false, 0f, sim.Clock.Tick));
        sim.TickMany(Archery.DrawTicks(sim, me, crossbow) / 2);
        sim.ApplyNow(new BowCommand(me, false, false, 0f, sim.Clock.Tick));
        Assert.False(Archery.IsLoaded(sim, me));

        // Cranked all the way, then let go: it stays loaded, through a save too.
        sim.ApplyNow(new BowCommand(me, true, false, 0f, sim.Clock.Tick));
        sim.TickMany(Archery.DrawTicks(sim, me, crossbow));
        sim.ApplyNow(new BowCommand(me, false, false, 0f, sim.Clock.Tick));
        Assert.False(sim.World.Has<Aiming>(me));
        Assert.True(Archery.IsLoaded(sim, me));
        Assert.True(sim.World.Has<Loaded>(item));
        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), sim.Defs);
        Assert.True(loaded.World.Has<Loaded>(item));

        // A click fires it, right button held or not; then it's empty.
        sim.TickMany(30);
        sim.ApplyNow(new BowCommand(me, false, true, 0f, sim.Clock.Tick));
        sim.TickMany(2);
        Assert.Contains(sim.RecentBlows, b => b.Attacker == me && b.Target == foe);
        Assert.False(Archery.IsLoaded(sim, me));
        sim.ApplyNow(new BowCommand(me, false, true, 0f, sim.Clock.Tick));
        Assert.False(sim.World.Has<Windup>(me), "nothing to loose till it's cranked again");
    }

    [Fact]
    public void YouGoSlowerWithABowDrawn()
    {
        var (sim, me) = Ring("Bow");
        var free = FreeMoveSystem.Wanted(sim, me, new float2(1, 0), true, null);
        sim.ApplyNow(new BowCommand(me, true, false, 0f, sim.Clock.Tick));
        var drawn = FreeMoveSystem.Wanted(sim, me, new float2(1, 0), true, null);
        Assert.True(drawn.X < free.X * 0.8f);
    }

    [Fact]
    public void AnArrowThatFindsNobodyStillFlies()
    {
        var (sim, me) = Ring("Bow");
        sim.ApplyNow(new BowCommand(me, true, false, 0f, sim.Clock.Tick));
        sim.TickMany(Archery.DrawTicks(sim, me, Archery.Bow(sim, me)!));
        sim.ApplyNow(new BowCommand(me, true, true, 0f, sim.Clock.Tick));
        sim.TickMany(2);
        // Seen and heard flying, to the fence east.
        var shot = Assert.Single(sim.RecentShots);
        Assert.True(shot.Ranged && !shot.Hit);
        Assert.True(shot.To.X > 24f && shot.To.X < 28f, $"stopped at {shot.To}");
    }

    [Fact]
    public void WindComesBackOnlyAWhileAfterItsSpentAndSlowly()
    {
        var (sim, me) = Ring();
        sim.ApplyNow(new DodgeCommand(me, 1f, 0f, sim.Clock.Tick));
        var after = sim.World.Get<Guard>(me).Stamina;
        sim.TickMany(Blows.RegenDelay - 2);
        Assert.Equal(after, sim.World.Get<Guard>(me).Stamina); // still catching your breath
        sim.TickMany(SimClock.TicksPerSecond);
        var back = sim.World.Get<Guard>(me).Stamina - after;
        Assert.True(back > 0.05f && back < 0.25f, $"a second's breath brought back {back}");
    }

    [Fact]
    public void AFriendInTheWayIsSteppedRoundNotWaitedBehind()
    {
        var (sim, me) = Ring();
        // Mott between: the second raider, coming for you, has him in the line of its blow.
        var mott = Foe(sim, new float2(15.3f, 16.5f));
        var cell = sim.World.Get<Position>(me).Cell;
        var brute = Raids.SpawnRaider(sim, sim.Defs.AllRaiderKinds.First(k => k.Name == "Swordsman"), "Grimsby", cell with { X = 13 }, sim.Rand);
        Place(sim, brute, new float2(14.2f, 16.5f));
        var wide = 0f;
        for (var t = 0; t < SimClock.TicksPerSecond * 3; t++)
        {
            sim.Tick();
            wide = Math.Max(wide, MathF.Abs(Where(sim, brute).Y - 16.5f));
        }
        Assert.True(wide > 0.5f, $"stepped only {wide:F2} off the line");
        Assert.Empty(sim.World.Get<Components.Health>(mott).Injuries);
    }
}
