using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Replay;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The co-op pause: everyone agrees before time holds, and everyone's ready before it runs again.</summary>
public class PauseVoteTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bea = new("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid Cal = new("00000000-0000-0000-0000-00000000000c");

    /// <summary>A town with Ana hosting and the others joined, as a co-op session would tell it.</summary>
    private static Simulation Session(params Guid[] guests)
    {
        var (sim, _) = TestWorld.Millbrook(7, 2);
        PeerEvents.Joined(sim, Ana, isHost: true);
        foreach (var g in guests)
            PeerEvents.Joined(sim, g, isHost: false);
        return sim;
    }

    [Fact]
    public void ItHoldsOnlyOnceEveryoneAgrees()
    {
        var sim = Session(Bea, Cal);
        sim.ApplyNow(new CallPauseCommand(Bea));
        Assert.Equal(PausePhase.Calling, sim.PauseVote.Phase);
        sim.ApplyNow(new PauseAnswerCommand(Ana, true));
        Assert.False(sim.PauseVote.Held);
        Assert.Equal([Cal], sim.PauseVote.Waiting);
        sim.ApplyNow(new PauseAnswerCommand(Cal, true));
        Assert.True(sim.PauseVote.Held);
        Assert.Empty(sim.PauseVote.Agreed); // now it counts who's ready
    }

    [Fact]
    public void OneNoCarriesOn()
    {
        var sim = Session(Bea);
        sim.ApplyNow(new CallPauseCommand(Ana));
        sim.ApplyNow(new PauseAnswerCommand(Bea, false));
        Assert.Equal(PausePhase.None, sim.PauseVote.Phase);
    }

    [Fact]
    public void AnUnansweredCallLapses()
    {
        var sim = Session(Bea);
        sim.ApplyNow(new CallPauseCommand(Ana));
        for (var i = 0; i < PauseVote.CallTicks + 60; i++)
            sim.Tick();
        Assert.Equal(PausePhase.None, sim.PauseVote.Phase);
    }

    [Fact]
    public void TimeRunsAgainOnceEveryoneIsReady_JoinersIncluded()
    {
        var sim = Session(Bea);
        sim.ApplyNow(new CallPauseCommand(Ana));
        sim.ApplyNow(new PauseAnswerCommand(Bea, true));
        Assert.True(sim.PauseVote.Held);

        sim.ApplyNow(new PauseAnswerCommand(Ana, true));
        PeerEvents.Joined(sim, Cal, isHost: false);
        sim.ApplyNow(new PauseAnswerCommand(Bea, true));
        Assert.True(sim.PauseVote.Held); // Cal came in while it held: not ready yet
        sim.ApplyNow(new PauseAnswerCommand(Bea, false));
        sim.ApplyNow(new PauseAnswerCommand(Cal, true));
        Assert.True(sim.PauseVote.Held); // Bea changed her mind
        sim.ApplyNow(new PauseAnswerCommand(Bea, true));
        Assert.Equal(PausePhase.None, sim.PauseVote.Phase);
    }

    [Fact]
    public void SomeoneGoneCantHoldTheTown()
    {
        var sim = Session(Bea, Cal);
        sim.ApplyNow(new CallPauseCommand(Ana));
        sim.ApplyNow(new PauseAnswerCommand(Bea, true));
        PeerEvents.Away(sim, Cal); // Cal dropped before answering
        Assert.True(sim.PauseVote.Held);

        sim.ApplyNow(new PauseAnswerCommand(Ana, true));
        PeerEvents.Left(sim, Bea);
        Assert.Equal(PausePhase.None, sim.PauseVote.Phase);
    }

    [Fact]
    public void TheCallerLeavingEndsTheCall()
    {
        var sim = Session(Bea, Cal);
        sim.ApplyNow(new CallPauseCommand(Bea));
        PeerEvents.Left(sim, Bea);
        Assert.Equal(PausePhase.None, sim.PauseVote.Phase);
    }

    [Fact]
    public void NoPausingInTheLobbyOrForStrangers()
    {
        var sim = Session(Bea);
        sim.ApplyNow(new CallPauseCommand(Cal)); // not connected
        Assert.Equal(PausePhase.None, sim.PauseVote.Phase);
        sim.Round.OpenLobby();
        sim.ApplyNow(new CallPauseCommand(Ana));
        Assert.Equal(PausePhase.None, sim.PauseVote.Phase);
    }

    [Fact]
    public void AVoteSurvivesASnapshot()
    {
        var sim = Session(Bea, Cal);
        sim.ApplyNow(new CallPauseCommand(Ana));
        sim.ApplyNow(new PauseAnswerCommand(Bea, true));

        var (loaded, _) = SaveGame.Read(SaveGame.Snapshot(sim), sim.Defs);

        Assert.Equal(PausePhase.Calling, loaded.PauseVote.Phase);
        Assert.Equal(Ana, loaded.PauseVote.Caller);
        Assert.Equal(sim.PauseVote.Present, loaded.PauseVote.Present);
        Assert.Equal(sim.PauseVote.Agreed, loaded.PauseVote.Agreed);
        Assert.Equal(sim.FullHash(), loaded.FullHash());
    }

    [Fact]
    public void HostingAgainForgetsWhoWasHere()
    {
        var sim = Session(Bea);
        sim.ApplyNow(new CallPauseCommand(Ana));
        PeerEvents.Joined(sim, Ana, isHost: true);
        Assert.Equal([Ana], sim.PauseVote.Present);
        Assert.Equal(PausePhase.None, sim.PauseVote.Phase);
    }
}
