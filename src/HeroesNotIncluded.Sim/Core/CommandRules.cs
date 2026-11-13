using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Core;

/// <summary>A command performed as a character (walking, working): only that character's player may send it.</summary>
public interface IActorCommand
{
    Entity Actor { get; }
}

/// <summary>A command aimed at one creature (draft, priorities, gear, orders): not at another player's character.</summary>
public interface ISubjectCommand
{
    Entity Subject { get; }
}

/// <summary>A command made on behalf of a player id (creating their character): only that player may send it.</summary>
public interface IPlayerCommand
{
    Guid? Owner { get; }
}

/// <summary>A command only the host may send, as part of running the session (starting the round).</summary>
public interface IHostCommand;

/// <summary>
/// Who may send what in a co-op session. Commands arrive from the network, so the peers check
/// them identically before applying: everyone may give village orders, but you steer only your own
/// character, and only the host may use developer tools.
/// </summary>
public static class CommandRules
{
    /// <summary>Why <paramref name="issuer"/> may not send this command right now, or null if they may.</summary>
    public static string? Refusal(Simulation sim, ISimCommand command, Guid issuer, bool isHost)
    {
        if (sim.Round.Phase == Round.RoundPhase.Over && command is IActorCommand)
            return "the season's over";
        if (command is IDevCommand && !isHost)
            return "only the host can use developer tools";
        if (command is IHostCommand && !isHost)
            return "only the host can do that";
        if (command is IPlayerCommand p && p.Owner != issuer)
            return "that's another player's";
        if (command is IActorCommand a && Players.OwnerOf(sim, a.Actor) != issuer)
            return "that isn't your character";
        if (command is ISubjectCommand s && ControllerOf(sim, s.Subject) is { } owner && owner != issuer)
            return "that belongs to another player";
        return null;
    }

    /// <summary>The player steering a creature, if any.</summary>
    public static Guid? ControllerOf(Simulation sim, Entity e)
    {
        var w = sim.World;
        if (!w.IsAlive(e))
            return null;
        if (Players.OwnerOf(sim, e) is { } owner)
            return owner;
        return null;
    }
}
