using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Saving;

namespace HeroesNotIncluded.Sim.Replay;

public enum ReplayEventKind : byte
{
    Command,
    Joined,
    Away,
    Left,
    Barrier,
}

/// <summary>Something done to the sim between ticks: a command (encoded), or a co-op event (<see cref="PeerEvents"/>).</summary>
public sealed record ReplayEvent(long Tick, ReplayEventKind Kind, Guid Player, bool IsHost, byte[]? Command);

/// <summary>A point worth jumping to on the timeline: a crisis, a death.</summary>
public readonly record struct ReplayMarker(long Tick, string Label);

/// <summary>
/// Records a game as it's played, so it can be watched again (<see cref="ReplayPlayer"/>). The sim
/// is deterministic, so a replay is where it started plus everything done to it since: every
/// command applied and every co-op event, each with its tick. It starts from a snapshot, and saves
/// drop running jobs, so it must begin where none are running (<see cref="Begin"/>: before a new
/// sim's first tick, or straight after a load or a co-op snapshot). A state hash every
/// <see cref="CheckEvery"/> ticks lets playback notice if it ever goes its own way.
/// <para>
/// It only reads the sim: it isn't saved, isn't hashed, and changes nothing. Anything that changes
/// the sim without a command (a dev button) must <see cref="Spoil"/> it.
/// </para>
/// </summary>
public sealed class ReplayRecorder
{
    /// <summary>Ticks between state hashes (ten seconds of play).</summary>
    public const int CheckEvery = 600;

    /// <summary>More events than this and recording gives up (a runaway, not a game).</summary>
    public const int MaxEvents = 2_000_000;

    private ReplayRecorder(byte[] start, long startTick)
    {
        Start = start;
        StartTick = EndTick = startTick;
    }

    /// <summary>The sim as recording began (<see cref="SaveGame.Snapshot"/>).</summary>
    public byte[] Start { get; }

    public long StartTick { get; }

    /// <summary>The last tick played.</summary>
    public long EndTick { get; private set; }

    public List<ReplayEvent> Events { get; } = [];
    public List<(long Tick, ulong Hash)> Checks { get; } = [];
    public List<ReplayMarker> Markers { get; } = [];

    /// <summary>Why this can't be replayed any more (something changed the sim outside commands); null while it can.</summary>
    public string? Spoiled { get; private set; }

    /// <summary>
    /// Start recording <paramref name="sim"/> as it is now, and attach to it. Call only where no job
    /// is running (see the class summary). Null, with why, if the sim can't be snapshot (a fight on).
    /// </summary>
    public static ReplayRecorder? Begin(Simulation sim, out string? why)
    {
        try
        {
            var recorder = new ReplayRecorder(SaveGame.Snapshot(sim), sim.Clock.Tick);
            sim.Recorder = recorder;
            why = null;
            return recorder;
        }
        catch (SaveException e)
        {
            why = e.Message;
            return null;
        }
    }

    /// <summary>Stop: something changed the sim behind the recorder's back, so playback would go wrong.</summary>
    public void Spoil(string why) => Spoiled ??= why;

    public void Mark(long tick, string label)
    {
        if (Markers.Count == 0 || Markers[^1] != new ReplayMarker(tick, label))
            Markers.Add(new ReplayMarker(tick, label));
    }

    internal void Applied(Simulation sim, ISimCommand command)
    {
        if (Spoiled != null)
            return;
        byte[] bytes;
        try
        {
            bytes = CommandCodec.Encode(command, sim.Defs);
        }
        catch (Exception e) when (e is InvalidOperationException or SaveException or ArgumentException)
        {
            Spoil($"a {command.GetType().Name} couldn't be recorded: {e.Message}");
            return;
        }
        Add(new ReplayEvent(sim.Clock.Tick, ReplayEventKind.Command, Guid.Empty, false, bytes));
    }

    internal void Note(Simulation sim, ReplayEventKind kind, Guid player, bool isHost)
    {
        if (Spoiled == null)
            Add(new ReplayEvent(sim.Clock.Tick, kind, player, isHost, null));
    }

    private void Add(ReplayEvent e)
    {
        if (Events.Count >= MaxEvents)
        {
            Spoil("too long to keep");
            return;
        }
        Events.Add(e);
    }

    internal void Ticked(Simulation sim)
    {
        if (Spoiled != null)
            return;
        EndTick = sim.Clock.Tick;
        if (EndTick % CheckEvery == 0)
            Checks.Add((EndTick, sim.StateHash(withMap: false)));
    }

    /// <summary>Everything recorded so far, as a replay to keep or watch.</summary>
    public ReplayFile ToFile(string buildTag, IReadOnlyDictionary<string, string> meta) =>
        new(buildTag, new Dictionary<string, string>(meta), StartTick, EndTick, Start, [.. Events], [.. Checks], [.. Markers]);
}
