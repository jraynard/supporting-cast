using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Emotes (<see cref="Emotes"/>): shown through a command, seen only by whoever can see you, and over when you move on.</summary>
public class EmoteTests
{
    /// <summary>A flat map in a round, in full daylight, with one character at (10, 10).</summary>
    private static (Simulation Sim, Entity Me) Round()
    {
        var sim = Create(60, 60);
        var me = Person(sim, 10, 10, "Me");
        sim.Round.OpenLobby();
        sim.Round.Sit(Guid.Empty, "Me", null, null, null);
        sim.Round.Start(sim);
        sim.RebuildDerivedIfNeeded();
        Array.Fill(sim.Map.Light, 1f);
        return (sim, me);
    }

    private static Entity Person(Simulation sim, int x, int y, string name = "Other") =>
        sim.SpawnCreature(sim.Defs.Creature("Human"), name, C(x, y), ageDays: 25 * 365);

    // The clock is moved by hand: ticking would let the townsfolk's own feet end their emotes.
    private static void Wait(Simulation sim, int ticks) => sim.Clock.Tick += ticks;

    [Fact]
    public void AWaveShowsThenRunsOut()
    {
        var (sim, me) = Round();
        new EmoteCommand(me, Emote.Wave, null).Apply(sim);

        Assert.Equal(Emote.Wave, Emotes.Current(sim, me)!.Value.Kind);
        Wait(sim, Emotes.Duration(Emote.Wave));
        Assert.Null(Emotes.Current(sim, me));
    }

    [Fact]
    public void SteppingAwayEndsIt()
    {
        var (sim, me) = Round();
        Assert.True(Emotes.Show(sim, me, Emote.Cheer, null));

        sim.World.Get<Position>(me).Cell = C(11, 10);
        Assert.Null(Emotes.Current(sim, me));
    }

    [Fact]
    public void NotWhenDownOrWithABladeOut()
    {
        var (sim, me) = Round();
        var down = Person(sim, 12, 10);
        sim.World.Get<Creature>(down).Downed = true;
        Assert.False(Emotes.Show(sim, down, Emote.Wave, null));

        sim.World.Add(me, new FreeMover { Drawn = true });
        Assert.Equal("weapon drawn", Emotes.Problem(sim, me, Emote.Wave));
        Assert.False(Emotes.Show(sim, me, Emote.Wave, null));
    }

    [Fact]
    public void OneAtATimeNotAFlurry()
    {
        var (sim, me) = Round();
        Assert.True(Emotes.Show(sim, me, Emote.Laugh, null));
        Assert.False(Emotes.Show(sim, me, Emote.Weep, null));
        Wait(sim, Emotes.Gap);
        Assert.True(Emotes.Show(sim, me, Emote.Weep, null));
        Assert.Equal(Emote.Weep, Emotes.Current(sim, me)!.Value.Kind);
    }

    [Fact]
    public void APointKeepsWhereItPointsWithinReach()
    {
        var (sim, me) = Round();
        Assert.True(Emotes.Show(sim, me, Emote.Point, C(20, 14)));
        Assert.Equal(C(20, 14), Emotes.Current(sim, me)!.Value.At);

        Wait(sim, Emotes.Gap);
        Assert.True(Emotes.Show(sim, me, Emote.Wave, C(20, 14)));
        Assert.Null(Emotes.Current(sim, me)!.Value.At); // only a point points

        Wait(sim, Emotes.Gap);
        Assert.True(Emotes.Show(sim, me, Emote.Point, new int3(-5, 9999, 1)));
        Assert.Null(Emotes.Current(sim, me)!.Value.At);
    }

    [Fact]
    public void OnlyThoseWhoCanSeeYouSeeIt()
    {
        var (sim, me) = Round();
        var near = Person(sim, 13, 10);
        var far = Person(sim, 55, 55);
        Assert.True(Emotes.Show(sim, near, Emote.Bow, null));
        Assert.True(Emotes.Show(sim, far, Emote.Bow, null));

        var view = Perception.For(sim, me);
        Assert.Equal(Emote.Bow, view.EmoteOf(near)!.Value.Kind);
        Assert.Null(view.EmoteOf(far));
    }

    [Fact]
    public void ItSurvivesASave()
    {
        var (sim, me) = Round();
        Assert.True(Emotes.Show(sim, me, Emote.Point, C(15, 12)));

        var loaded = SaveLoadTests.RoundTrip(sim);

        var em = Emotes.Current(loaded, me)!.Value;
        Assert.Equal(Emote.Point, em.Kind);
        Assert.Equal(C(15, 12), em.At);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
    }

    [Fact]
    public void LongFinishedOnesAreTidiedAway()
    {
        var (sim, me) = Round();
        Assert.True(Emotes.Show(sim, me, Emote.Shrug, null));
        Emotes.Tidy(sim);
        Assert.True(sim.World.Has<Emoting>(me));

        Wait(sim, Emotes.Duration(Emote.Shrug) + Emotes.Gap);
        Emotes.Tidy(sim);
        Assert.False(sim.World.Has<Emoting>(me));
    }

    [Fact]
    public void TheCommandGoesThroughTheWire()
    {
        var (sim, me) = Round();
        var sent = new EmoteCommand(me, Emote.Point, C(12, 12));
        var back = (EmoteCommand)CommandCodec.Decode(CommandCodec.Encode(sent, sim.Defs), sim.Defs);
        Assert.Equal(sent, back);
    }
}
