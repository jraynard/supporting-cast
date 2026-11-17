using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;

namespace HeroesNotIncluded.Sim.Replay;

/// <summary>
/// Plays a <see cref="ReplayFile"/> back in a sim of its own: loads where it started, then ticks it
/// on, doing each recorded command and co-op event at the tick it happened. Going back means
/// starting again from the beginning (<see cref="Seek"/>); a whole season replays in a few seconds.
/// Where a recorded state hash doesn't match, it notes the tick (<see cref="DriftedAt"/>) rather
/// than showing a game that didn't happen as if it had.
/// </summary>
public sealed class ReplayPlayer
{
    private readonly DefDatabase _defs;
    private int _next;      // next event to do
    private int _nextCheck; // next state hash to compare

    public ReplayPlayer(ReplayFile file, DefDatabase defs)
    {
        File = file;
        _defs = defs;
        Sim = Restart();
    }

    public ReplayFile File { get; }

    /// <summary>The replay's own sim; a new instance after every <see cref="Seek"/> backwards.</summary>
    public Simulation Sim { get; private set; }

    public long Tick => Sim.Clock.Tick;

    public bool Ended => Tick >= File.EndTick;

    /// <summary>The first tick whose state didn't match the recording; null while it all has.</summary>
    public long? DriftedAt { get; private set; }

    private Simulation Restart()
    {
        var (sim, _) = SaveGame.Read(File.Start, _defs);
        sim.StrictJobs = false; // as the game played it: a command that throws fails alone
        Sim = sim;
        _next = 0;
        _nextCheck = 0;
        DoDue();
        return sim;
    }

    /// <summary>Play one tick (and what was done just after it). False at the end.</summary>
    public bool Step()
    {
        if (Ended)
            return false;
        Sim.Tick();
        Check();
        DoDue();
        return true;
    }

    /// <summary>
    /// Go to <paramref name="tick"/> (clamped to the recording): forward by playing on, backward by
    /// starting over. <paramref name="cancel"/> stops it part way, where it's got to.
    /// </summary>
    public void Seek(long tick, CancellationToken cancel = default)
    {
        tick = Math.Clamp(tick, File.StartTick, File.EndTick);
        if (tick < Tick)
            Restart();
        while (Tick < tick && !cancel.IsCancellationRequested)
            Step();
    }

    private void DoDue()
    {
        var events = File.Events;
        while (_next < events.Length && events[_next].Tick <= Tick)
        {
            var e = events[_next++];
            if (e.Kind == ReplayEventKind.Command)
            {
                ISimCommand command;
                try
                {
                    command = CommandCodec.Decode(e.Command, _defs);
                }
                catch (InvalidDataException)
                {
                    DriftedAt ??= Tick; // can't be: the build matched. Say so rather than guess.
                    continue;
                }
                Sim.ApplyNow(command);
            }
            else
                PeerEvents.Replay(Sim, e.Kind, e.Player, e.IsHost);
        }
    }

    private void Check()
    {
        var checks = File.Checks;
        while (_nextCheck < checks.Length && checks[_nextCheck].Tick < Tick)
            _nextCheck++;
        if (_nextCheck < checks.Length && checks[_nextCheck].Tick == Tick)
        {
            if (DriftedAt == null && Sim.StateHash(withMap: false) != checks[_nextCheck].Hash)
                DriftedAt = Tick;
            _nextCheck++;
        }
    }
}
