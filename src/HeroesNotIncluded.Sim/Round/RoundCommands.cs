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

/// <summary>The host deals the trades and starts the round.</summary>
public sealed record StartRoundCommand : ISimCommand, IHostCommand
{
    public void Apply(Simulation sim) => sim.Round.Start(sim);
}

/// <summary>A player arriving mid-round takes over an AI villager (not one whose character died: they're a ghost).</summary>
public sealed record TakeTradeCommand(Guid Player, IReadOnlyList<TradeDef> Wanted, IReadOnlyList<TradeDef> Never)
    : ISimCommand, IPlayerCommand
{
    public Guid? Owner => Player;

    public void Apply(Simulation sim) => sim.Round.TakeTrade(sim, Player, Wanted, Never);
}
