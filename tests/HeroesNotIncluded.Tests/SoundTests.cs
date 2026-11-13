using Brutal.Numerics;
using HeroesNotIncluded.Audio;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;
using Narsil.Audio;

namespace HeroesNotIncluded.Tests;

/// <summary>The game's sound: the manifest it ships, babble, and what the director plays for what the player perceives.</summary>
public class SoundTests
{
    private static string Manifest => Path.Combine(DevPaths.Sounds!, "sounds.json");

    [Fact]
    public void TheManifestHasEverySoundTheGameNamesAndEveryFileDecodes()
    {
        var report = SoundAudit.Run(Manifest);
        Assert.True(report.Errors.Count == 0, string.Join("\n", report.Errors));
        Assert.True(report.Files > 0);
    }

    [Fact]
    public void EachVoiceIsItsOwnAndAStrangersSaysNothingOfWho()
    {
        var a = new Entity(3, 1);
        Assert.Equal(Babble.VoiceOf(a, 1), Babble.VoiceOf(a, 99)); // steady for a speaker
        var voices = Enumerable.Range(0, 40).Select(i => Babble.VoiceOf(new Entity(i, 1), 0)).ToList();
        Assert.Equal(Babble.Registers, voices.Select(v => v.Register).Distinct().Count()); // deep, middling and high all turn up
        Assert.True(voices.Max(v => v.Loudness) - voices.Min(v => v.Loudness) > 0.3f); // some talk louder than others
        Assert.True(voices.Max(v => v.Pace) - voices.Min(v => v.Pace) > 0.2f);
        // Unrecognised: the voice comes from the line, so two lines from one stranger differ.
        Assert.Contains(Enumerable.Range(0, 20), i => Babble.VoiceOf(null, i) != Babble.VoiceOf(null, 0));
    }

    [Fact]
    public void BabbleFollowsTheWordsVolumeAndClarity()
    {
        HeardLine Line(string text, Volume v = Volume.Say, Clarity c = Clarity.Clear, int who = 2, long id = 7) => new(id, 0, "Bea", text, v, c, new Entity(who, 1));
        var said = Babble.For(Line("Good morning to you."));
        Assert.Equal(5, said.Count); // good, mor-ning, to, you
        Assert.True(said.Zip(said.Skip(1)).All(p => p.Second.Delay > p.First.Delay));
        Assert.All(said, s => Assert.Equal(0f, s.Muffle));
        Assert.Equal(said[0].Variant / (Babble.Onsets * Babble.Vowels), said[1].Variant / (Babble.Onsets * Babble.Vowels)); // one register throughout
        Assert.NotEqual(said[0].Variant, said[1].Variant); // "goo" and "mor" aren't the same sound

        Assert.True(Babble.For(Line("Raiders!", Volume.Shout))[0].Gain > Babble.For(Line("Raiders!"))[0].Gain);
        Assert.All(Babble.For(Line("... the bridge ...", c: Clarity.Muffled)), s => Assert.True(s.Muffle > 0.5f));
        Assert.Empty(Babble.For(Line("*cough*")));
        Assert.Equal(Babble.MaxSyllables, Babble.For(Line(string.Join(' ', Enumerable.Repeat("hello", 40)))).Count);
        var question = Babble.For(Line("Where is the inn?"));
        Assert.True(question[^1].Pitch > question[^2].Pitch * 1.1f); // a question rises at the end

        // The same words from two people (or twice from one) don't echo each other.
        var other = Babble.For(Line("Good morning to you.", who: 31, id: 8));
        Assert.False(said.Zip(other).All(p => p.First.Pitch == p.Second.Pitch && p.First.Delay == p.Second.Delay && p.First.Gain == p.Second.Gain));
    }

    private static (AudioEngine Audio, SoundDirector Director) Rig()
    {
        var audio = new AudioEngine(new NullAudioBackend(48), null, Manifest);
        Assert.True(audio.Reload());
        return (audio, new SoundDirector(audio));
    }

    private static readonly float3 Right = new(1, 0, 0);

    [Fact]
    public void AFightInSightIsHeardWhereItIs()
    {
        var (sim, center) = CoopSessionTests.Millbrook(11, 2);
        var (audio, director) = Rig();
        var view = Perception.For(sim, Entity.Null); // no round: sees everything
        var listen = new float3(center.X, center.Y, center.Z);
        director.Update(sim, view, Entity.Null, listen, Right, 2f, 0.016f);

        sim.RecentShots.Add(new ShotEvent(sim.Clock.Tick + 1, listen + new float3(1, 0, 0), listen + new float3(2, 0, 0), Ranged: false, Hit: true));
        director.Update(sim, view, Entity.Null, listen, Right, 2f, 0.016f);
        audio.Update(0.2f); // the clash comes a beat after the swing
        Assert.Contains(audio.Recent, s => s.Id == Sounds.Swing);
        Assert.Contains(audio.Recent, s => s.Id == Sounds.Clash && s.At is { } at && at.X > listen.X);
        Assert.Equal(Moods.Danger, director.Mood);

        // Seen once: the same shot doesn't play again.
        var count = audio.Recent.Count(s => s.Id == Sounds.Clash);
        director.Update(sim, view, Entity.Null, listen, Right, 2f, 0.016f);
        audio.Update(0.2f);
        Assert.Equal(count, audio.Recent.Count(s => s.Id == Sounds.Clash));
    }

    [Fact]
    public void OnlyLinesYourCharacterHeardAreBabbled()
    {
        var (sim, center) = CoopSessionTests.Millbrook(12, 6);
        var people = sim.Creatures().Where(e => SpeechSystem.CanAct(sim, e)).ToList();
        var speaker = people[0];
        var at = sim.World.Get<Position>(speaker).Cell;
        var far = people.FirstOrDefault(e => e != speaker && SpeechSystem.Hear(sim, at, sim.World.Get<Position>(e).Cell, Volume.Say) == null);
        Assert.False(far.IsNull, "the test needs a villager out of earshot");

        foreach (var (listener, expect) in new[] { (far, false), (speaker, true) })
        {
            var (audio, director) = Rig();
            var view = new PerceptionView(sim, listener); // as in a round: Perception.For sees everything without one
            var listen = new float3(center.X, center.Y, center.Z);
            director.Update(sim, view, listener, listen, Right, 2f, 0.016f);
            Assert.NotNull(sim.Speech.Say(sim, speaker, $"Fine weather for it, {(expect ? "near" : "far")}.", Volume.Say));
            director.Update(sim, view, listener, listen, Right, 2f, 0.016f);
            audio.Update(2f);
            Assert.Equal(expect, audio.Recent.Any(s => s.Id == Sounds.Voice));
        }
    }

    [Fact]
    public void TheMusicFollowsTheDayAndADangerBell()
    {
        var (sim, center) = CoopSessionTests.Millbrook(13, 1);
        var (audio, director) = Rig();
        var view = Perception.For(sim, Entity.Null);
        var listen = new float3(center.X, center.Y, center.Z);
        director.Update(sim, view, Entity.Null, listen, Right, 2f, 0.016f);
        Assert.Equal(sim.Clock.Daylight < 0.3f ? Moods.Night : Moods.Day, director.Mood);

        sim.Messages.Add(MessageKind.Danger, "Raiders on the north road!");
        director.Update(sim, view, Entity.Null, listen, Right, 2f, 0.016f);
        Assert.Equal(Moods.Danger, director.Mood);
        Assert.Contains(audio.Recent, s => s.Id == Sounds.UiBell);

        director.Update(sim, view, Entity.Null, listen, Right, 2f, SoundDirector.DangerHoldSeconds + 1f);
        Assert.NotEqual(Moods.Danger, director.Mood);
    }

    [Fact]
    public void ACrowdBabblesAFewAtATime()
    {
        var (sim, center) = CoopSessionTests.Millbrook(14, 8);
        var (audio, director) = Rig();
        var view = Perception.For(sim, Entity.Null); // hears everyone
        var listen = new float3(center.X, center.Y, center.Z);
        director.Update(sim, view, Entity.Null, listen, Right, 2f, 0.016f);
        var speakers = sim.Creatures().Where(e => SpeechSystem.CanAct(sim, e)).Take(6).ToList();
        Assert.True(speakers.Count >= 5);
        foreach (var e in speakers)
            Assert.NotNull(sim.Speech.Say(sim, e, "Lovely weather we're having today.", Volume.Say));
        director.Update(sim, view, Entity.Null, listen, Right, 2f, 0.016f);
        var lines = 0;
        for (var k = 0; k < 40; k++)
        {
            audio.Update(0.05f);
            lines = Math.Max(lines, audio.Recent.Where(r => r.Id == Sounds.Voice).Select(r => r.At).Distinct().Count());
        }
        Assert.InRange(lines, 1, SoundDirector.MaxTalking);
    }
}
