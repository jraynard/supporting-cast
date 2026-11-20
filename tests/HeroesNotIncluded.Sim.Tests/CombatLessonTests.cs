using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Scenarios.Lessons;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// The tutorial's fight (<see cref="CombatLesson"/>), shared by every trade: set out in the practice
/// yard (<see cref="Sparring"/>), then played through with the commands a client sends: fists up, a
/// blow on the dummy, and one of the sparring partner's punches blocked, one parried, one dodged.
/// </summary>
public class CombatLessonTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private sealed record Yard(Simulation Sim, Entity Me, LessonRun Run)
    {
        public LessonStage Stage => Lessons.Stage(Sim, Me, Run);
        public Entity Dummy => Sparring.Of(Sim).Dummy;
        public Entity Hobb => Sparring.Of(Sim).Partner;

        public void StandAt(float3 pos)
        {
            var cell = new int3((int)MathF.Floor(pos.X), (int)MathF.Floor(pos.Y), (int)MathF.Floor(pos.Z));
            Sim.World.Get<Position>(Me).Cell = cell;
            Sim.World.Get<Position>(Me).Pos = pos;
        }

        public float AimAt(float3 at)
        {
            var me = Sim.World.Get<Position>(Me).Pos;
            return DetMath.Atan2(at.Y - me.Y, at.X - me.X);
        }

        /// <summary>Stand still facing Hobb for a tick, as a player watching him would.</summary>
        public void Face()
        {
            Sim.ApplyNow(new FreeMoveCommand(Me, 0, 0, AimAt(Sim.World.Get<Position>(Hobb).Pos)));
            Sim.Tick();
        }

        /// <summary>Face Hobb till his next punch is wound up at you; its windup.</summary>
        public Windup NextPunch()
        {
            for (var t = 0; t < SimClock.TicksPerSecond * 10; t++)
            {
                if (Sim.World.TryGet<Windup>(Hobb, out var windup) && windup.Target == Me)
                    return windup;
                Face();
            }
            throw new Exception($"Hobb never swung (at '{Stage.Step}')");
        }

        /// <summary>Face him till the punch has landed one way or another.</summary>
        public void Until(long tick)
        {
            while (Sim.Clock.Tick <= tick + Blows.Grace(Sim) + 1)
                Face();
        }
    }

    private static Yard Begin(string trade = "Blacksmith")
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(trade)], []));
        sim.ApplyNow(new StartRoundCommand());
        sim.Round.Practice = true;
        sim.TickMany(2); // a first tick: free movement
        var me = Players.CharacterOf(sim, Ana);
        var lesson = sim.Defs.Shared(CombatLesson.Id)!;
        var run = new LessonRun(sim, me, sim.Defs.Trade(trade), lesson);
        sim.ApplyNow(new PracticeCommand(me, PracticeAction.SharedLesson, sim.Defs.SharedLessons.ToList().IndexOf(lesson)));
        return new Yard(sim, me, run);
    }

    [Fact]
    public void TheFightIsASharedCoreChapterAfterTheBasicsKeptOnce()
    {
        var defs = LoadDefs();
        var smith = defs.Trade("Blacksmith");
        var combat = defs.Shared(CombatLesson.Id);
        Assert.NotNull(combat);
        Assert.True(combat!.Core && combat.Sparring);
        foreach (var step in CombatLesson.Lesson.Steps)
            Assert.True(combat.IndexOf(step) >= 0, $"the fight's '{step}' has no words");

        var fresh = Tutorial.Chapters(defs, smith, _ => false);
        Assert.Same(defs.Basics, fresh[0]);
        Assert.Same(combat, fresh[1]);
        Assert.Equal("Basics/combat", LessonRun.KeyOf(smith, combat));
        // Learned as the smith, it's learned as the cook too.
        var cook = defs.Trade("Cook");
        Assert.DoesNotContain(combat, Tutorial.Chapters(defs, cook, key => key == LessonRun.KeyOf(smith, combat)));
    }

    [Fact]
    public void ASharedChaptersIdIsntATradesToTake()
    {
        const string json = """
            { "shared": [ { "name": "brawling", "steps": [ { "id": "a", "text": "x" } ] } ],
              "trades": [ { "name": "Odd", "lessons": [ { "id": "brawling", "steps": [ { "id": "a", "text": "x" } ] } ] } ] }
            """;
        var e = Assert.ThrowsAny<Exception>(() => Defs.DefDatabase.FromJson(json));
        Assert.Contains("brawling", e.Message);
    }

    [Fact]
    public void TheYardSetsOutADummyAndAPartnerAndItsBareKnuckles()
    {
        var yard = Begin();
        var w = yard.Sim.World;
        Assert.False(yard.Dummy.IsNull);
        Assert.False(yard.Hobb.IsNull);
        Assert.Equal(Faction.Stranger, w.Get<Creature>(yard.Hobb).Faction);
        Assert.False(w.TryGet<Equipment>(yard.Me, out var eq) && w.IsAlive(eq.Weapon)); // in the pack
        Assert.True(Lessons.OnMe(yard.Sim, yard.Me, "Hammer") > 0);
        Assert.False(w.Get<FreeMover>(yard.Me).Drawn);

        // Set up again: still one of each.
        yard.Sim.ApplyNow(new PracticeCommand(yard.Me, PracticeAction.SharedLesson, 0));
        Assert.Single(yard.Sim.Creatures(), e => w.Get<Creature>(e).Name == Sparring.PartnerName && !w.Get<Creature>(e).Dead);
        Assert.Single(w.Query<Thing>(), e => w.Get<Thing>(e).Def.Name == Sparring.DummyDef);
    }

    [Fact]
    public void FistsUpAndABlowOnTheDummyThenBlockParryAndDodgeHobb()
    {
        var yard = Begin();
        var sim = yard.Sim;
        var w = sim.World;
        Assert.Equal(new LessonStage("draw", yard.Dummy), yard.Stage);

        sim.ApplyNow(new DrawWeaponCommand(yard.Me, true));
        Assert.Equal(new LessonStage("dummy", yard.Dummy), yard.Stage);

        // Beside the dummy, a punch at it: a blow landed, and nobody hurt.
        var dummyAt = Position.CenterOf(w.Get<Thing>(yard.Dummy).Cell);
        var beside = sim.SpiralFrom(w.Get<Thing>(yard.Dummy).Cell, 2).First(c => sim.Map.Passable(c) && Math.Abs(c.X - w.Get<Thing>(yard.Dummy).Cell.X) + Math.Abs(c.Y - w.Get<Thing>(yard.Dummy).Cell.Y) == 1);
        yard.StandAt(Position.CenterOf(beside));
        sim.ApplyNow(new AttackCommand(yard.Me, false, yard.AimAt(dummyAt)));
        sim.TickMany(SimClock.TicksPerSecond);
        Assert.Contains(sim.RecentBlows, b => b.Attacker == yard.Me && b.Target == yard.Dummy && b.Outcome == BlowOutcome.Hit);
        Assert.Equal(new LessonStage("brawl", yard.Hobb), yard.Stage);

        // Up to Hobb, fists up: he squares up and swings.
        var hobbAt = w.Get<Position>(yard.Hobb).Pos;
        yard.StandAt(hobbAt + new float3(1.2f, 0, 0));
        var punch = yard.NextPunch();
        Assert.Equal("block", yard.Stage.Step);

        // Block: a guard held up well before it lands.
        sim.ApplyNow(new GuardCommand(yard.Me, true, sim.Clock.Tick));
        yard.Until(punch.Impact);
        sim.ApplyNow(new GuardCommand(yard.Me, false, sim.Clock.Tick));
        Assert.Contains(sim.RecentBlows, b => b.Target == yard.Me && b.Outcome == BlowOutcome.Blocked);
        Assert.Equal("parry", yard.Stage.Step);

        // Parry: raised just as it lands.
        punch = yard.NextPunch();
        while (sim.Clock.Tick < punch.Impact - 2)
            yard.Face();
        sim.ApplyNow(new GuardCommand(yard.Me, true, sim.Clock.Tick));
        yard.Until(punch.Impact);
        sim.ApplyNow(new GuardCommand(yard.Me, false, sim.Clock.Tick));
        Assert.True(sim.RecentBlows.Exists(b => b.Target == yard.Me && b.Outcome == BlowOutcome.Parried),
            string.Join(", ", sim.RecentBlows.Select(b => $"{b.Tick}:{b.Outcome}")) + $" impact {punch.Impact} start {punch.Start}");
        Assert.Equal("dodge", yard.Stage.Step);

        // Dodge: a dash just before it lands.
        punch = yard.NextPunch();
        while (sim.Clock.Tick < punch.Impact - 3)
            yard.Face();
        sim.ApplyNow(new DodgeCommand(yard.Me, 0, 1, sim.Clock.Tick));
        yard.Until(punch.Impact);
        Assert.True(yard.Stage.Done, $"still at '{yard.Stage.Step}'");
    }

    [Fact]
    public void WhenTheChaptersOverHobbStandsDown()
    {
        var yard = Begin();
        var sim = yard.Sim;
        sim.ApplyNow(new DrawWeaponCommand(yard.Me, true));
        yard.StandAt(sim.World.Get<Position>(yard.Hobb).Pos + new float3(1.2f, 0, 0));
        yard.NextPunch();

        sim.ApplyNow(new PracticeCommand(yard.Me, PracticeAction.EndSparring));
        Assert.IsNotType<SparJob>(sim.World.Get<Mind>(yard.Hobb).Job);
        var from = sim.Clock.Tick;
        for (var t = 0; t < SimClock.TicksPerSecond * 15; t++)
            yard.Face(); // fists still up, right in front of him
        Assert.DoesNotContain(sim.RecentBlows, b => b.Attacker == yard.Hobb && b.Tick > from + SimClock.TicksPerSecond);
        Assert.False(sim.World.Has<Windup>(yard.Hobb));
    }

    [Fact]
    public void TheTownLeavesHobbBe()
    {
        var yard = Begin();
        var sim = yard.Sim;
        sim.ApplyNow(new DrawWeaponCommand(yard.Me, true));
        yard.StandAt(sim.World.Get<Position>(yard.Hobb).Pos + new float3(1.2f, 0, 0));
        for (var t = 0; t < SimClock.TicksPerSecond * 20; t++)
            yard.Face();
        // Only the player and Hobb have been at it: no villager joined in.
        Assert.All(sim.RecentBlows, b => Assert.True(b.Attacker == yard.Me || b.Attacker == yard.Hobb, $"{sim.World.Get<Creature>(b.Attacker).Name} joined in"));
        Assert.Null(Ai.Defence.Threat(sim, yard.Me, Ai.Defence.VillagerRange));
    }
}
