namespace HeroesNotIncluded.Sim.Bots;

/// <summary>
/// What a bot got up to over a round, counted from its own side (what it started and said), so
/// <c>--season-sweep</c> can show how much of each trade's game the bots actually play, and a
/// change that leaves a game unplayed shows up as a gap rather than only as a lost round.
/// </summary>
public sealed class BotStats
{
    /// <summary>Mini-game sessions started, by session type.</summary>
    public Dictionary<string, int> Sessions { get; } = [];

    /// <summary>Orders taken on, by service.</summary>
    public Dictionary<string, int> Taken { get; } = [];

    /// <summary>Lines said aloud.</summary>
    public int Said { get; set; }

    /// <summary>Slips its temperament made on purpose: a short hammering, a key off the wrong hook.</summary>
    public int Mistakes { get; set; }

    internal static void Count(Dictionary<string, int> into, string key) => into[key] = into.GetValueOrDefault(key) + 1;
}
