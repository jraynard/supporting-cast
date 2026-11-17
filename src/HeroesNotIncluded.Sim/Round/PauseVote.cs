using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;

namespace HeroesNotIncluded.Sim.Round;

/// <summary>Where a co-op pause stands.</summary>
public enum PausePhase
{
    None,

    /// <summary>Someone asked to pause; it holds once everyone connected agrees.</summary>
    Calling,

    /// <summary>Time is held; it runs again once everyone connected says they're ready.</summary>
    Held,
}

/// <summary>
/// A co-op pause by agreement: the town never waits for one player, but it may for all of them
/// (someone needs a break). Anyone calls; it holds once every connected player agrees, and lifts
/// once every connected player is ready. Kept in the sim, decided by commands and the session's
/// joins and leaves (<see cref="Replay.PeerEvents"/>), so every peer sees the same vote; the
/// host's game reads <see cref="Held"/> and seals its turns with no steps. A player who drops
/// leaves the tally, so nobody gone can hold the town. Single player pauses game-side and never
/// touches this.
/// </summary>
public sealed class PauseVote : ISimSystem, ISaveState
{
    public string Name => "PauseVote";
    public int Interval => 60;

    /// <summary>How long a call stands unanswered before it lapses, in ticks (ticks still run while it's open).</summary>
    public const long CallTicks = 30 * SimClock.TicksPerSecond;

    private readonly List<Guid> _present = [];
    private readonly List<Guid> _agreed = [];

    public PausePhase Phase { get; private set; }

    /// <summary>Who asked for the pause (while <see cref="PausePhase.Calling"/>).</summary>
    public Guid Caller { get; private set; }

    /// <summary>When the call was made.</summary>
    public long CalledAt { get; private set; }

    /// <summary>The players connected now, in the order they joined.</summary>
    public IReadOnlyList<Guid> Present => _present;

    /// <summary>While calling, who agrees; while held, who is ready.</summary>
    public IReadOnlyList<Guid> Agreed => _agreed;

    /// <summary>Time is held: the host seals turns with no steps.</summary>
    public bool Held => Phase == PausePhase.Held;

    /// <summary>Whether this player has agreed (calling) or is ready (held).</summary>
    public bool HasAgreed(Guid player) => _agreed.Contains(player);

    /// <summary>The connected players who haven't agreed (calling) or aren't ready (held).</summary>
    public IEnumerable<Guid> Waiting => _present.Where(p => !_agreed.Contains(p));

    public void Update(Simulation sim)
    {
        if (Phase == PausePhase.Calling && sim.Clock.Tick - CalledAt >= CallTicks)
        {
            sim.Messages.Add(MessageKind.Info, "The call to pause lapsed.", null);
            Clear();
        }
    }

    /// <summary>A player asks to pause. Refused while a call or a pause already stands, or outside play.</summary>
    public void Call(Simulation sim, Guid player)
    {
        if (Phase != PausePhase.None || !_present.Contains(player) || !Playing(sim))
            return;
        Phase = PausePhase.Calling;
        Caller = player;
        CalledAt = sim.Clock.Tick;
        _agreed.Clear();
        _agreed.Add(player);
        sim.Messages.Add(MessageKind.Info, $"{NameOf(sim, player)} asks to pause.", null);
        Settle(sim);
    }

    /// <summary>
    /// A player answers. While calling: yes agrees, no turns the call down (the caller's no
    /// withdraws it). While held: ready or not.
    /// </summary>
    public void Answer(Simulation sim, Guid player, bool yes)
    {
        if (!_present.Contains(player))
            return;
        switch (Phase)
        {
            case PausePhase.Calling when yes:
                if (!_agreed.Contains(player))
                    _agreed.Add(player);
                Settle(sim);
                break;
            case PausePhase.Calling:
                sim.Messages.Add(MessageKind.Info, player == Caller
                    ? $"{NameOf(sim, player)} carries on: no pause."
                    : $"{NameOf(sim, player)} would rather carry on: no pause.", null);
                Clear();
                break;
            case PausePhase.Held when yes:
                if (!_agreed.Contains(player))
                    _agreed.Add(player);
                Settle(sim);
                break;
            case PausePhase.Held:
                _agreed.Remove(player);
                break;
        }
    }

    /// <summary>A player connected (or reconnected). Joining a held pause, they aren't ready yet.</summary>
    public void Joined(Guid player, bool isHost)
    {
        if (isHost)
        {
            // Hosting (a save, or anew): whoever was here when it was saved isn't, yet.
            _present.Clear();
            Clear();
        }
        if (!_present.Contains(player))
            _present.Add(player);
    }

    /// <summary>A player dropped or left: out of the tally, which may now be complete.</summary>
    public void Gone(Simulation sim, Guid player)
    {
        _present.Remove(player);
        _agreed.Remove(player);
        if (Phase == PausePhase.Calling && player == Caller)
            Clear(); // nobody left to want it
        else
            Settle(sim);
    }

    private void Settle(Simulation sim)
    {
        if (Phase == PausePhase.None)
            return;
        if (_present.Count == 0)
        {
            Clear();
            return;
        }
        if (_present.Any(p => !_agreed.Contains(p)))
            return;
        if (Phase == PausePhase.Calling)
        {
            Phase = PausePhase.Held;
            _agreed.Clear();
            sim.Messages.Add(MessageKind.Info, "Everyone agrees: time holds.", null);
        }
        else
        {
            Clear();
            sim.Messages.Add(MessageKind.Info, "Everyone's ready: time runs again.", null);
        }
    }

    private void Clear()
    {
        Phase = PausePhase.None;
        Caller = Guid.Empty;
        CalledAt = 0;
        _agreed.Clear();
    }

    /// <summary>A pause means something only while the town is running: not in the lobby or after the reveal.</summary>
    private static bool Playing(Simulation sim) => sim.Round.Phase is not (RoundPhase.Lobby or RoundPhase.Over);

    /// <summary>What everyone calls a player: their character's name, living or not, else their seat's.</summary>
    public static string NameOf(Simulation sim, Guid player)
    {
        var w = sim.World;
        var me = Systems.Players.CharacterOf(sim, player);
        if (!me.IsNull)
            return w.Get<Creature>(me).Name;
        if (Afterlife.Ghosts.Of(sim, player) is { IsNull: false } ghost
            && w.Get<Afterlife.Ghost>(ghost).Body is var body && w.IsAlive(body) && w.Has<Creature>(body))
            return w.Get<Creature>(body).Name;
        return sim.Round.SeatOf(player)?.Name is { Length: > 0 } seat ? seat : "Someone";
    }

    public void Save(SaveWriter w, Simulation sim)
    {
        w.Int((int)Phase);
        w.String(Caller.ToString("N"));
        w.Long(CalledAt);
        WriteIds(w, _present);
        WriteIds(w, _agreed);
    }

    public void Load(SaveReader r, Simulation sim)
    {
        Phase = (PausePhase)Math.Clamp(r.Int(), 0, (int)PausePhase.Held);
        Caller = Guid.TryParse(r.String(), out var caller) ? caller : Guid.Empty;
        CalledAt = r.Long();
        ReadIds(r, _present);
        ReadIds(r, _agreed);
    }

    private static void WriteIds(SaveWriter w, List<Guid> ids)
    {
        w.Int(ids.Count);
        foreach (var id in ids)
            w.String(id.ToString("N"));
    }

    private static void ReadIds(SaveReader r, List<Guid> ids)
    {
        ids.Clear();
        var n = r.Count(RoundSystem.MaxSeats * 4);
        for (var i = 0; i < n; i++)
            if (Guid.TryParse(r.String(), out var id) && !ids.Contains(id))
                ids.Add(id);
    }
}
