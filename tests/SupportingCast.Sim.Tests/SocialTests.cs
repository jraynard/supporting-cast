using SupportingCast.Sim.Components;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Tests;

public class SocialTests
{
    private static Entity Person(Simulation sim, int x, int y = 5)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), $"P{x}", C(x, y), ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        return e;
    }

    /// <summary>Run the social system on its own for a number of updates, everyone standing still.</summary>
    private static void Talk(Simulation sim, int updates)
    {
        for (var i = 0; i < updates; i++)
            sim.Social.Update(sim);
    }

    [Fact]
    public void CompatibilityIsFixedSymmetricAndBounded()
    {
        var sim = TestWorld.Create();
        var people = Enumerable.Range(0, 6).Select(i => Person(sim, 2 + i * 2)).ToList();
        var values = new List<float>();
        foreach (var a in people)
            foreach (var b in people.Where(b => b != a))
            {
                var c = MoodRules.Compatibility(sim, a, b);
                Assert.Equal(c, MoodRules.Compatibility(sim, b, a));
                Assert.InRange(c, -MoodRules.MaxCompatibility, MoodRules.MaxCompatibility);
                values.Add(c);
            }
        Assert.True(values.Distinct().Count() > 5, "pairs should differ");
    }

    [Fact]
    public void OpinionAddsUpMemoriesAboutThatPersonOnly()
    {
        var sim = TestWorld.Create();
        var a = Person(sim, 5);
        var b = Person(sim, 7);
        var c = Person(sim, 9);
        var chatted = sim.Defs.Thought("Chatted");
        var baseB = MoodRules.Opinion(sim, a, b);
        MoodSystem.Remember(sim, a, chatted, c); // about someone else: doesn't count toward b
        MoodSystem.Remember(sim, a, chatted, b);
        MoodSystem.Remember(sim, a, chatted, b);
        Assert.Equal(baseB + chatted.Opinion * (1 + chatted.StackFalloff), MoodRules.Opinion(sim, a, b), 3);
        Assert.Equal(MoodRules.Opinion(sim, b, a), MoodRules.Compatibility(sim, a, b)); // b remembers nothing
    }

    [Fact]
    public void NeighboursTalkOnTheirOwnStreamAndStrangersApartDont()
    {
        var sim = TestWorld.Create();
        var a = Person(sim, 5);
        var b = Person(sim, 7);
        var far = Person(sim, 30);
        var mainRand = sim.Rand.Fingerprint;

        Talk(sim, 300);

        Assert.Equal(mainRand, sim.Rand.Fingerprint);
        Assert.NotEmpty(sim.Social.Recent);
        Assert.All(sim.Social.Recent, ev => Assert.DoesNotContain(far, new[] { ev.Initiator, ev.Recipient }));
        Assert.Contains(sim.World.Get<Mood>(a).Memories, m => m.Other == b);
        Assert.DoesNotContain(sim.World.Get<Mood>(far).Memories, m => !m.Other.IsNull);
    }

    [Fact]
    public void AWallBetweenThemStopsTheTalk()
    {
        var sim = TestWorld.Create();
        sim.BuildWallRing(new int3(3, 3, 1), new int3(8, 8, 1));
        Person(sim, 5);       // inside
        Person(sim, 10);      // outside, three cells away
        Talk(sim, 300);
        Assert.Empty(sim.Social.Recent);
    }

    [Fact]
    public void SleepersAndTheDraftedDontTalk()
    {
        var sim = TestWorld.Create();
        var a = Person(sim, 5);
        var b = Person(sim, 7);
        sim.World.Get<Mind>(a).Asleep = true;
        sim.World.Get<Mind>(b).Drafted = true;
        Talk(sim, 300);
        Assert.Empty(sim.Social.Recent);
    }

    [Fact]
    public void ASlightIsLikelierTowardSomeoneDisliked()
    {
        var sim = TestWorld.Create();
        var a = Person(sim, 5);
        var b = Person(sim, 7);
        var slight = sim.Defs.Interaction("Slight");
        var before = SocialSystem.Weight(sim, a, b, slight);
        for (var i = 0; i < 3; i++)
            MoodSystem.Remember(sim, a, sim.Defs.Thought("Slighted"), b);
        Assert.True(SocialSystem.Weight(sim, a, b, slight) > before);
        // Comfort only for someone who's down.
        var comfort = sim.Defs.Interaction("Comfort");
        Assert.Equal(0f, SocialSystem.Weight(sim, b, a, comfort));
        sim.World.Get<Mood>(a).Value = 0.2f;
        Assert.True(SocialSystem.Weight(sim, b, a, comfort) > 0);
    }

    [Fact]
    public void FriendsGrieveHarderAndRivalsDont()
    {
        var sim = TestWorld.Create();
        var friend = Person(sim, 5);
        var rival = Person(sim, 7);
        var dead = Person(sim, 9);
        for (var i = 0; i < 3; i++)
        {
            MoodSystem.Remember(sim, friend, sim.Defs.Thought("DeepTalk"), dead);
            MoodSystem.Remember(sim, rival, sim.Defs.Thought("Slighted"), dead);
        }
        MoodSystem.Remember(sim, friend, sim.Defs.Thoughts.RescuedMe, dead);
        MoodSystem.Remember(sim, rival, sim.Defs.Thoughts.CapturedMe, dead);
        Assert.True(MoodRules.Opinion(sim, friend, dead) >= MoodRules.FriendAt);
        Assert.True(MoodRules.Opinion(sim, rival, dead) <= MoodRules.RivalAt);

        ref var c = ref sim.World.Get<Creature>(dead);
        NeedsSystem.Kill(sim, dead, ref c);

        Assert.Contains(sim.World.Get<Mood>(friend).Memories, m => m.Def == sim.Defs.Thoughts.FriendDied);
        Assert.Contains(sim.World.Get<Mood>(rival).Memories, m => m.Def == sim.Defs.Thoughts.RivalDied);
    }

    [Fact]
    public void TheSocialStreamSurvivesASave()
    {
        var sim = TestWorld.Create();
        Person(sim, 5);
        Person(sim, 7);
        Talk(sim, 50);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(sim.Social.Rand.Fingerprint, loaded.Social.Rand.Fingerprint);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
    }
}
