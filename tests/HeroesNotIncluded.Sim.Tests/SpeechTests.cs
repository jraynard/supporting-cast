using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Speech;
using SupportingCast.Sim.Systems;
using static SupportingCast.Sim.Tests.TestWorld;

namespace SupportingCast.Sim.Tests;

public class SpeechTests
{
    private static Simulation World()
    {
        var sim = Create(70, 70);
        sim.RebuildDerivedIfNeeded();
        return sim;
    }

    private static Entity Person(Simulation sim, int x, int y, string name = "P") =>
        sim.SpawnCreature(sim.Defs.Creature("Human"), name, C(x, y), ageDays: 25 * 365);

    private static ThingDef Door(Simulation sim) => sim.Defs.AllThings.First(t => t.Building is { IsDoor: true, Fence: false, PartOnly: false });

    /// <summary>A closed room over x 20..26, y 20..26, with a door in the east wall at (27, 23) if asked.</summary>
    private static void Room(Simulation sim, bool door = false)
    {
        for (var i = 19; i <= 27; i++)
        {
            sim.SpawnStructure(sim.Thing("WoodWall"), C(i, 19));
            sim.SpawnStructure(sim.Thing("WoodWall"), C(i, 27));
            if (i is > 19 and < 27)
            {
                sim.SpawnStructure(sim.Thing("WoodWall"), C(19, i));
                sim.SpawnStructure(door && i == 23 ? Door(sim) : sim.Thing("WoodWall"), C(27, i));
            }
        }
        sim.RebuildDerivedIfNeeded();
    }

    private static Hearing? HeardBy(Utterance u, Entity e) => u.Listeners.FindIndex(h => h.Listener == e) is >= 0 and var i ? u.Listeners[i] : null;

    [Fact]
    public void AWhisperReachesOnlyTheOneBesideYou()
    {
        var sim = World();
        var a = Person(sim, 10, 10);
        var b = Person(sim, 11, 10);
        var c = Person(sim, 13, 10);

        var u = sim.Speech.Say(sim, a, "the blacksmith did it", Volume.Whisper)!;

        Assert.Equal(Clarity.Clear, HeardBy(u, b)!.Value.Clarity);
        Assert.Null(HeardBy(u, c));
    }

    [Fact]
    public void SpeakingCarriesAcrossTheOpenButNotFar()
    {
        var sim = World();
        var a = Person(sim, 10, 10);
        var near = Person(sim, 18, 10);
        var far = Person(sim, 30, 10);

        var u = sim.Speech.Say(sim, a, "hello", Volume.Say)!;

        Assert.Equal(Clarity.Clear, HeardBy(u, near)!.Value.Clarity);
        Assert.Null(HeardBy(u, far));
    }

    [Fact]
    public void AWallMufflesSpeechAndStopsItFurtherOff()
    {
        var sim = World();
        Room(sim);
        var inside = Person(sim, 25, 23);
        var justOutside = Person(sim, 28, 23);
        var furtherOut = Person(sim, 34, 23);

        var u = sim.Speech.Say(sim, inside, "keep this quiet", Volume.Say)!;

        Assert.Equal(Clarity.Muffled, HeardBy(u, justOutside)!.Value.Clarity);
        Assert.False(HeardBy(u, justOutside)!.Value.Identified);
        Assert.Null(HeardBy(u, furtherOut));
    }

    [Fact]
    public void AWhisperDoesNotGoThroughAWall()
    {
        var sim = World();
        Room(sim);
        var inside = Person(sim, 26, 23);
        var outside = Person(sim, 28, 23);

        var u = sim.Speech.Say(sim, inside, "psst", Volume.Whisper)!;

        Assert.Null(HeardBy(u, outside));
    }

    [Fact]
    public void AShoutCarriesThroughWallsMuffled()
    {
        var sim = World();
        Room(sim);
        var inside = Person(sim, 23, 23);
        var outside = Person(sim, 40, 23);

        var u = sim.Speech.Say(sim, inside, "fire in the clinic", Volume.Shout)!;

        Assert.Equal(Clarity.Muffled, HeardBy(u, outside)!.Value.Clarity);
    }

    [Fact]
    public void SomeoneInADoorwayHearsBothSides()
    {
        var sim = World();
        Room(sim, door: true);
        var inside = Person(sim, 22, 23);
        var outside = Person(sim, 32, 23);
        var doorway = Person(sim, 27, 23);

        var fromInside = sim.Speech.Say(sim, inside, "in here", Volume.Say)!;
        var fromOutside = sim.Speech.Say(sim, outside, "out here", Volume.Say)!;

        Assert.Equal(Clarity.Clear, HeardBy(fromInside, doorway)!.Value.Clarity);
        Assert.Equal(Clarity.Clear, HeardBy(fromOutside, doorway)!.Value.Clarity);
    }

    [Fact]
    public void AVoiceFarOffIsHeardButNotKnown()
    {
        var sim = World();
        var a = Person(sim, 10, 10);
        var far = Person(sim, 30, 10);

        var u = sim.Speech.Say(sim, a, "over here", Volume.Shout)!;

        var h = HeardBy(u, far)!.Value;
        Assert.Equal(Clarity.Clear, h.Clarity);
        Assert.False(h.Identified);
    }

    [Fact]
    public void TheSleepingAndTheDownedHearNothing()
    {
        var sim = World();
        var a = Person(sim, 10, 10);
        var asleep = Person(sim, 11, 10);
        var downed = Person(sim, 10, 11);
        sim.World.Get<Mind>(asleep).Asleep = true;
        sim.World.Get<Creature>(downed).Downed = true;

        var u = sim.Speech.Say(sim, a, "wake up", Volume.Say)!;

        Assert.Null(HeardBy(u, asleep));
        Assert.Null(HeardBy(u, downed));
    }

    [Fact]
    public void MuffledWordsAreDroppedAndDifferByListener()
    {
        var text = string.Join(' ', Enumerable.Repeat("word", 40));
        var u = new Utterance { Id = 3, Text = text };
        var one = SpeechSystem.TextFor(u, new Hearing(new Entity(1, 0), Clarity.Muffled, false));
        var two = SpeechSystem.TextFor(u, new Hearing(new Entity(2, 0), Clarity.Muffled, false));

        Assert.Contains("...", one);
        Assert.Contains("word", one);
        Assert.NotEqual(one, two);
        Assert.Equal(text, SpeechSystem.TextFor(u, new Hearing(new Entity(1, 0), Clarity.Clear, true)));
    }

    [Fact]
    public void TextIsCleanedAndCapped()
    {
        var sim = World();
        var a = Person(sim, 10, 10);
        var u = sim.Speech.Say(sim, a, "  hi\n\u0007there" + new string('x', 1000), Volume.Say)!;

        Assert.StartsWith("hithere", u.Text);
        Assert.Equal(SpeechSystem.MaxLength, u.Text.Length);
        Assert.Null(sim.Speech.Say(sim, a, "   ", Volume.Say));
    }

    [Fact]
    public void ASpeakerIsRateLimited()
    {
        var sim = World();
        var a = Person(sim, 10, 10);
        for (var i = 0; i < SpeechSystem.RateLimit; i++)
            Assert.NotNull(sim.Speech.Say(sim, a, "spam", Volume.Say));
        Assert.Null(sim.Speech.Say(sim, a, "spam", Volume.Say));

        sim.TickMany(SpeechSystem.RateWindowTicks + 1);
        Assert.NotNull(sim.Speech.Say(sim, a, "again", Volume.Say));
    }

    [Theory]
    [InlineData("hello", Volume.Say, "hello")]
    [InlineData("/w psst", Volume.Whisper, "psst")]
    [InlineData("/S HEY", Volume.Shout, "HEY")]
    [InlineData("/wave", Volume.Say, "/wave")]
    public void ChatBoxPrefixesSetTheVolume(string input, Volume volume, string text) =>
        Assert.Equal((volume, text), SpeechSystem.Parse(input));

    [Fact]
    public void WhatWasSaidSurvivesASave()
    {
        var sim = World();
        var a = Person(sim, 10, 10);
        var b = Person(sim, 11, 10);
        sim.Speech.Say(sim, a, "remember this", Volume.Say);

        var (loaded, _) = SaveGame.Read(SaveGame.Snapshot(sim), sim.Defs);

        var u = Assert.Single(loaded.Speech.Kept);
        Assert.Equal("remember this", u.Text);
        Assert.NotNull(HeardBy(u, b));
        Assert.Equal(sim.FullHash(), loaded.FullHash());
    }

    [Fact]
    public void AViewIsToldOnlyWhatItsCharacterHeard()
    {
        var sim = Create(70, 70);
        var me = Person(sim, 10, 10, "Me");
        sim.Round.OpenLobby();
        sim.Round.Sit(Guid.Empty, "Me", null, null, null);
        sim.Round.Start(sim);
        sim.RebuildDerivedIfNeeded();
        var friend = Person(sim, 12, 10, "Friend");
        var stranger = Person(sim, 60, 60, "Stranger");
        var bystander = Person(sim, 61, 60, "Bystander");

        sim.Speech.Say(sim, friend, "for you", Volume.Say);
        sim.Speech.Say(sim, stranger, "not for you", Volume.Say);

        var heard = Perception.For(sim, me).Heard().ToList();
        var line = Assert.Single(heard);
        Assert.Equal("for you", line.Text);
        Assert.Equal("Friend", line.Who);
        Assert.Equal(friend, line.Speaker);
        Assert.Equal(2, Perception.For(sim, Entity.Null).Heard().Count());
        Assert.Empty(Perception.Blind(sim).Heard());
        _ = bystander;
    }

    [Fact]
    public void AnUnrecognisedVoiceHasNoName()
    {
        var sim = Create(70, 70);
        var me = Person(sim, 10, 10, "Me");
        sim.Round.OpenLobby();
        sim.Round.Sit(Guid.Empty, "Me", null, null, null);
        sim.Round.Start(sim);
        sim.RebuildDerivedIfNeeded();
        var far = Person(sim, 30, 10, "Far");

        sim.Speech.Say(sim, far, "anyone there", Volume.Shout);

        var line = Assert.Single(Perception.For(sim, me).Heard());
        Assert.Equal("Someone", line.Who);
        Assert.Null(line.Speaker);
    }
}
