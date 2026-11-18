using HeroesNotIncluded.Sim.Round;

namespace HeroesNotIncluded.Sim.Modding;

/// <summary>
/// A way to play a round (docs/MODDING.md, "A new game mode"): the season (the game's own), the practice
/// yard, or a mod's (a night of raids to see through). The round keeps its mode by name, and asks it what
/// to run, how to begin and when it's over. Registered with <see cref="SimRegistry.Mode"/>; every hook has
/// a default, so a mode says only how it differs from a season.
/// </summary>
public interface IGameMode
{
    /// <summary>Its id, saved with the round ("season", "practice", a mod's "siege").</summary>
    string Name { get; }

    /// <summary>What the title screen calls it ("Siege Night"), and a line saying what it is.</summary>
    string Label { get; }

    string Description { get; }

    /// <summary>
    /// The season's clock runs it: the festival on the last day, the reckoning there, and Dread at its limit
    /// ending the round sooner. Off, none of that happens and the mode ends the round itself (<see cref="Check"/>).
    /// </summary>
    bool Seasonal => true;

    /// <summary>The Director sends the season's crises. Off, it sends none of its own (a mode may still send them, and they play out).</summary>
    bool Crises => true;

    /// <summary>The hour of day it starts at, the clock moved on to it as the trades are dealt (null: as the town wakes).</summary>
    float? StartHour => null;

    /// <summary>The trades are dealt and the round's begun: set out what the mode needs.</summary>
    void Started(Simulation sim) { }

    /// <summary>Where the round's got to, in a line under the clock ("Till dawn: 6 hours"); null for the mode's label. A seasonal mode shows the season's.</summary>
    string? Status(Simulation sim) => null;

    /// <summary>Asked as the round goes: how it ends, now, or null to play on. A town with nobody left alive ends anyway.</summary>
    RoundEnd? Check(Simulation sim) => null;
}

/// <summary>How a mode ends a round: an outcome for the record, and the reveal's headline.</summary>
public readonly record struct RoundEnd(RoundOutcome Outcome, string Headline);

/// <summary>The game's own round: a season, its crises, the festival and the reckoning.</summary>
public sealed class SeasonMode : IGameMode
{
    public const string Id = "season";
    public string Name => Id;
    public string Label => "A season";
    public string Description => "Days of trade and trouble, the festival, and the town's verdict.";
}

/// <summary>The practice yard (<see cref="Scenarios.Practice"/>): no season, no crises, customers when sent for.</summary>
public sealed class PracticeMode : IGameMode
{
    public const string Id = "practice";
    public string Name => Id;
    public string Label => "Practice";
    public string Description => "Your trade's games, in your own time.";
    public bool Seasonal => false;
    public bool Crises => false;
    public void Started(Simulation sim) => sim.Round.Practice = true;
}

/// <summary>The game's own modes, registered as a mod's would be.</summary>
internal sealed class CoreModes : ISimMod
{
    public void Register(SimRegistry registry)
    {
        registry.Mode(new SeasonMode());
        registry.Mode(new PracticeMode());
    }
}

/// <summary>The modes registered (the game's, then each mod's).</summary>
public static class GameModes
{
    public static IReadOnlyList<IGameMode> All => [.. SimContent.ModeList];

    /// <summary>The mode of that name, or the season if there's none (its mod is off): a round always has one.</summary>
    public static IGameMode Of(string name) => All.FirstOrDefault(m => m.Name == name) ?? All.First(m => m.Name == SeasonMode.Id);
}
