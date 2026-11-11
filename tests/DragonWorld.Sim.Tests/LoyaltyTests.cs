using DragonWorld.Sim.Ai;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Saving;
using DragonWorld.Sim.Systems;

namespace DragonWorld.Sim.Tests;

/// <summary>Traits, and companions weighing mood and opinion before doing what they're asked.</summary>
public class LoyaltyTests
{
    private static Simulation Warm()
    {
        var sim = TestWorld.Create();
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    private static Entity Person(Simulation sim, int x, params string[] traits)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), $"P{x}", C(x, 5), ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        TraitRules.Set(sim, e, traits);
        return e;
    }

    private static Entity Avatar(Simulation sim, int x)
    {
        var e = Person(sim, x);
        sim.World.Add(e, new Avatar());
        return e;
    }

    // ---------------- Traits ----------------

    [Fact]
    public void RolledTraitsAreFixedPerPersonAndNeverClash()
    {
        var sim = Warm();
        var counts = new int[TraitRules.MaxTraits + 1];
        for (var i = 0; i < 200; i++)
        {
            var e = new Ecs.Entity(i, 0);
            var a = TraitRules.Roll(sim.Defs, 77, e);
            Assert.Equal(a.List, TraitRules.Roll(sim.Defs, 77, e).List);
            Assert.InRange(a.List.Count, 0, TraitRules.MaxTraits);
            counts[a.List.Count]++;
            if (a.List.Count == 2)
                Assert.False(a.List[0].ConflictsWithTrait(a.List[1]), $"{a.List[0]} and {a.List[1]} clash");
        }
        Assert.All(counts, n => Assert.True(n > 20, "every count from none to two should come up"));
    }

    [Fact]
    public void SpawningAPersonDrawsTraitsFromTheirOwnStream()
    {
        var a = Warm();
        var b = Warm();
        var e = a.SpawnCreature(a.Defs.Creature("Human"), "A", C(5, 5));
        Assert.True(a.World.Has<Traits>(e));
        foreach (var _ in a.Defs.Creature("Human").Needs)
            b.Rand.Range(0.7f, 0.95f); // spawning only rolls starting needs from the main stream
        Assert.Equal(b.Rand.Fingerprint, a.Rand.Fingerprint);
    }

    [Fact]
    public void ChosenTraitsDropUnknownsClashesAndExtras()
    {
        var sim = Warm();
        var e = Person(sim, 5, "Optimist", "Gloomy", "NoSuchTrait", "Hardy", "Kind");
        Assert.Equal(["Optimist", "Hardy"], sim.World.Get<Traits>(e).List.Select(t => t.Name));
    }

    [Fact]
    public void TraitsShiftMoodAndHowThoughtsWeigh()
    {
        var sim = Warm();
        var plain = Person(sim, 5);
        var cheery = Person(sim, 7, "Optimist");
        var hardy = Person(sim, 9, "Hardy");
        sim.Climate.BaseTemperature = -10;
        Array.Fill(sim.Map.Temperature, -10f);
        new MoodSystem().Update(sim);

        var cold = sim.Defs.Thought("Cold").Mood;
        Assert.Equal(0.5f + cold / 100f, sim.World.Get<Mood>(plain).Target, 3);
        Assert.Equal(0.5f + (cold + sim.Defs.Trait("Optimist").Mood) / 100f, sim.World.Get<Mood>(cheery).Target, 3);
        Assert.Equal(0.5f + cold * 0.5f / 100f, sim.World.Get<Mood>(hardy).Target, 3);

        // A trait that ignores a thought keeps it off the list altogether.
        MoodSystem.Remember(sim, hardy, sim.Defs.Thoughts.SleptOutdoors);
        Assert.Equal(cold * 0.5f, MoodSystem.Offset(sim.World.Get<Mood>(hardy), sim.World.Get<Traits>(hardy)), 3);
    }

    [Fact]
    public void ThickSkinShrugsOffASlightAndKindnessRarelyGivesOne()
    {
        var sim = Warm();
        var tough = Person(sim, 5, "ThickSkinned");
        var soft = Person(sim, 7);
        var kind = Person(sim, 9, "Kind");
        var slighted = sim.Defs.Thought("Slighted");
        MoodSystem.Remember(sim, tough, slighted, kind);
        MoodSystem.Remember(sim, soft, slighted, kind);
        Assert.Equal(slighted.Opinion * 0.3f, MoodRules.Opinion(sim, tough, kind) - MoodRules.Compatibility(sim, tough, kind), 3);
        Assert.Equal(slighted.Opinion, MoodRules.Opinion(sim, soft, kind) - MoodRules.Compatibility(sim, soft, kind), 3);

        var slight = sim.Defs.Interaction("Slight");
        Assert.Equal(0.2f, TraitRules.Factor(sim.World.Get<Traits>(kind), slight), 3);
        Assert.Equal(2f, TraitRules.Factor(sim.World.Get<Traits>(kind), sim.Defs.Interaction("Comfort")), 3);
    }

    [Fact]
    public void TraitsSurviveASave()
    {
        var sim = Warm();
        var e = Person(sim, 5, "Glutton", "Loner");
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(["Glutton", "Loner"], loaded.World.Get<Traits>(e).List.Select(t => t.Name));
    }

    // ---------------- Willingness ----------------

    /// <summary>Make <paramref name="who"/> miserable and bitter toward <paramref name="asker"/> (opinion about −50).</summary>
    private static void Sour(Simulation sim, Entity who, Entity asker)
    {
        MoodSystem.Remember(sim, who, sim.Defs.Thoughts.CapturedMe, asker);
        for (var i = 0; i < 4; i++)
            MoodSystem.Remember(sim, who, sim.Defs.Thought("Slighted"), asker);
        sim.World.Get<Mood>(who).Value = 0.05f;
    }

    [Fact]
    public void WillingnessFollowsMoodAndOpinionOfTheAsker()
    {
        var sim = Warm();
        var me = Avatar(sim, 5);
        var friend = Person(sim, 7);
        var (neutral, _) = Requests.Willingness(sim, me, friend, RequestKind.Follow);
        var (forEgg, _) = Requests.Willingness(sim, me, friend, RequestKind.GuardEgg);
        Assert.True(forEgg > neutral, "everyone cares about the egg");

        sim.World.Get<Mood>(friend).Value = 0.1f;
        var (down, why) = Requests.Willingness(sim, me, friend, RequestKind.Follow);
        Assert.True(down < neutral - 0.2f);
        Assert.Contains("miserable", why);

        sim.World.Get<Mood>(friend).Value = 0.5f;
        MoodSystem.Remember(sim, friend, sim.Defs.Thoughts.CapturedMe, me);
        MoodSystem.Remember(sim, friend, sim.Defs.Thought("Slighted"), me);
        var (sour, reason) = Requests.Willingness(sim, me, friend, RequestKind.Follow);
        Assert.True(sour < neutral - 0.15f);
        Assert.Contains("can't stand you", reason);
    }

    [Fact]
    public void ARefusalSticksForAnHour()
    {
        var sim = Warm();
        var me = Avatar(sim, 5);
        var sulky = Person(sim, 7);
        Sour(sim, sulky, me);
        Assert.True(Requests.Willingness(sim, me, sulky, RequestKind.Follow).Chance < 0.2f);
        for (var tries = 0; tries < 20 && !Requests.RecentlyRefused(sim, me, sulky); tries++)
        {
            sim.Enqueue(new RequestCommand(me, sulky, RequestKind.Follow));
            sim.Tick();
            if (sim.World.Has<Request>(sulky))
                sim.Enqueue(new RequestCommand(me, sulky, null));
        }
        Assert.True(Requests.RecentlyRefused(sim, me, sulky));
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("won't"));

        // Within the hour, asking again doesn't even roll.
        sim.Enqueue(new RequestCommand(me, sulky, RequestKind.Follow));
        sim.Tick();
        Assert.False(sim.World.Has<Request>(sulky));
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("already said no"));

        sim.TickMany(SimClock.TicksPerHour + 60);
        Assert.False(Requests.RecentlyRefused(sim, me, sulky));
    }

    [Fact]
    public void AskingSomeoneWhosDownCostsTheirGoodwill()
    {
        var sim = Warm();
        var me = Avatar(sim, 5);
        var friend = Person(sim, 7);
        for (var i = 0; i < 3; i++)
            MoodSystem.Remember(sim, friend, sim.Defs.Thought("DeepTalk"), me); // likes me enough to go along
        for (var tries = 0; tries < 10 && !sim.World.Has<Request>(friend); tries++)
        {
            if (tries > 0)
                sim.TickMany(SimClock.TicksPerHour + 60); // wait out a refusal
            sim.World.Get<Mood>(friend).Value = 0.3f;
            sim.Enqueue(new RequestCommand(me, friend, RequestKind.Follow));
            sim.Tick();
        }
        Assert.True(sim.World.Has<Request>(friend), "they should go along sooner or later");
        Assert.Contains(sim.World.Get<Mood>(friend).Memories, m => m.Def == sim.Defs.Thoughts.BossedAround && m.Other == me);
    }

    [Fact]
    public void AStandingRequestIsDroppedWhenTheyTurnAgainstYou()
    {
        var sim = Warm();
        var me = Avatar(sim, 5);
        var friend = Person(sim, 8);
        sim.World.Add(friend, new Request { Kind = RequestKind.Follow, Target = me, From = me });
        Assert.NotNull(Requests.JobFor(sim, friend));

        Sour(sim, friend, me);
        Assert.Null(Requests.JobFor(sim, friend));
        Assert.False(sim.World.Has<Request>(friend));
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("stops following you"));
    }
}
