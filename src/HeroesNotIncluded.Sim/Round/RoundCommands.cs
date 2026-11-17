using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Round;

/// <summary>A player sits down in the lobby as the character they made.</summary>
public sealed record JoinLobbyCommand(Guid Player, string Name, Appearance? Look, IReadOnlyList<string> Traits, IReadOnlyList<string>? Clothes = null)
    : ISimCommand, IPlayerCommand
{
    public Guid? Owner => Player;

    public void Apply(Simulation sim) => sim.Round.Sit(Player, Name, Look, Traits, Clothes);
}

/// <summary>A player says which trades they want, best first, and which they refuse.</summary>
public sealed record SetTradePreferencesCommand(Guid Player, IReadOnlyList<TradeDef> Wanted, IReadOnlyList<TradeDef> Never)
    : ISimCommand, IPlayerCommand
{
    public Guid? Owner => Player;

    public void Apply(Simulation sim) => sim.Round.SetPreferences(Player, Wanted, Never);
}

/// <summary>A player is ready, or no longer is.</summary>
public sealed record SetReadyCommand(Guid Player, bool Ready) : ISimCommand, IPlayerCommand
{
    public Guid? Owner => Player;

    public void Apply(Simulation sim) => sim.Round.SetReady(Player, Ready);
}

/// <summary>A player asks everyone to pause (co-op): it holds once all agree (<see cref="PauseVote"/>).</summary>
public sealed record CallPauseCommand(Guid Player) : ISimCommand, IPlayerCommand
{
    public Guid? Owner => Player;

    public void Apply(Simulation sim) => sim.PauseVote.Call(sim, Player);
}

/// <summary>A player answers a call to pause (agree, or carry on), or, while it holds, is ready or not.</summary>
public sealed record PauseAnswerCommand(Guid Player, bool Yes) : ISimCommand, IPlayerCommand
{
    public Guid? Owner => Player;

    public void Apply(Simulation sim) => sim.PauseVote.Answer(sim, Player, Yes);
}

/// <summary>The host deals the trades and starts the round.</summary>
public sealed record StartRoundCommand : ISimCommand, IHostCommand
{
    public void Apply(Simulation sim) => sim.Round.Start(sim);
}

/// <summary>The host chooses the kind of round in the lobby: friendly fire, and (from M4) traitors.</summary>
public sealed record SetRoundOptionsCommand(bool FriendlyFire, bool Traitors) : ISimCommand, IHostCommand
{
    public void Apply(Simulation sim) => sim.Round.SetOptions(new RoundOptions(FriendlyFire, Traitors));
}

/// <summary>A player arriving mid-round takes over an AI villager (not one whose character died: they're a ghost).</summary>
public sealed record TakeTradeCommand(Guid Player, IReadOnlyList<TradeDef> Wanted, IReadOnlyList<TradeDef> Never)
    : ISimCommand, IPlayerCommand
{
    public Guid? Owner => Player;

    public void Apply(Simulation sim) => sim.Round.TakeTrade(sim, Player, Wanted, Never);
}
