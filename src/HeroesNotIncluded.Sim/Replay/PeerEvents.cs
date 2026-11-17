using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Replay;

/// <summary>
/// What a co-op session does to the sim outside commands (a player joining, stepping away or
/// leaving, and the barrier before a snapshot), in one place, so the session and a replay of it do
/// exactly the same thing. Each is recorded (<see cref="Simulation.Recorder"/>) as it happens.
/// </summary>
public static class PeerEvents
{
    public static void Joined(Simulation sim, Guid player, bool isHost)
    {
        sim.Recorder?.Note(sim, ReplayEventKind.Joined, player, isHost);
        if (isHost)
            // Hosting a save: anyone who was playing when it was saved is away until they rejoin.
            Players.ReleaseAllBut(sim, player);
        Players.Join(sim, player, adoptLooseAvatar: isHost);
    }

    public static void Away(Simulation sim, Guid player)
    {
        sim.Recorder?.Note(sim, ReplayEventKind.Away, player, false);
        Players.Away(sim, player);
    }

    public static void Left(Simulation sim, Guid player)
    {
        sim.Recorder?.Note(sim, ReplayEventKind.Left, player, false);
        sim.Round.Stand(player);
        Players.Leave(sim, player);
    }

    /// <summary>Every job stopped, so the state can be snapshot exactly (a joiner, a resync).</summary>
    public static void Barrier(Simulation sim)
    {
        sim.Recorder?.Note(sim, ReplayEventKind.Barrier, Guid.Empty, false);
        AiSystem.InterruptAll(sim);
    }

    /// <summary>Do a recorded event again, in a replay.</summary>
    internal static void Replay(Simulation sim, ReplayEventKind kind, Guid player, bool isHost)
    {
        switch (kind)
        {
            case ReplayEventKind.Joined: Joined(sim, player, isHost); break;
            case ReplayEventKind.Away: Away(sim, player); break;
            case ReplayEventKind.Left: Left(sim, player); break;
            case ReplayEventKind.Barrier: Barrier(sim); break;
        }
    }
}
