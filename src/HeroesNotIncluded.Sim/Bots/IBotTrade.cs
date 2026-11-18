using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Bots;

/// <summary>
/// How a bot plays a trade (docs/MODDING.md): one is made for each <see cref="BotPlayer"/> from the
/// factories registered with <see cref="Modding.SimRegistry.Bot"/>, so it may keep its own state. It
/// plays only through the bot's commands and what the bot perceives (<see cref="BotPlayer"/>'s public
/// helpers: <see cref="BotPlayer.Walk"/>, <see cref="BotPlayer.Make"/>, <see cref="BotPlayer.Send"/>...),
/// never by reading hidden state. Each hook's null or false means "not mine": the next trade is asked.
/// </summary>
public interface IBotTrade
{
    /// <summary>The trade's work between orders (its shelf, its stock). True while at it.</summary>
    bool Between(Simulation sim, Entity me, PerceptionView view, long now) => false;

    /// <summary>One step of an order of <paramref name="kind"/> this trade serves; null if it isn't this trade's. False when it's over.</summary>
    bool? Order(Simulation sim, Entity me, PerceptionView view, ServiceKind kind, Entity poster, long now) => null;

    /// <summary>
    /// How to play this frame of a work session (the keys pressed, held and the axis); null if it isn't this
    /// trade's. The second action and axis go on <see cref="BotPlayer.Act"/> and <see cref="BotPlayer.Axis2"/>.
    /// </summary>
    (bool Press, bool Held, int Axis)? Play(WorkSession s) => null;
}

public sealed partial class BotPlayer
{
    private List<IBotTrade>? _tradeBots;

    /// <summary>Every trade's bot, made for this bot (the game's, then each mod's).</summary>
    private IReadOnlyList<IBotTrade> TradeBots => _tradeBots ??= [.. Modding.SimContent.BotFactories.Select(make => make(this))];

    private bool TradesBetween(Simulation sim, Entity me, PerceptionView view, long now)
    {
        foreach (var trade in TradeBots)
            if (trade.Between(sim, me, view, now))
                return true;
        return false;
    }

    private (bool Press, bool Held, int Axis)? PlayTrades(WorkSession s)
    {
        foreach (var trade in TradeBots)
            if (trade.Play(s) is { } play)
                return play;
        return null;
    }
}
