namespace HeroesNotIncluded.Sim.Defs;

/// <summary>
/// How long a round's season runs and what it takes to come through it (data/defs/season.json).
/// A round has a fixed end so a session has a shape: the festival on the last evening, where the
/// town's Renown is reckoned. Dread overflowing ends it sooner. The last one loaded is in effect.
/// </summary>
public sealed class SeasonDef : Def
{
    /// <summary>Silver everyone in town starts the round with: enough for a supper or two, or a favour.</summary>
    public int StartingPurse { get; set; } = 12;

    /// <summary>Days in the season, counting the day of the deal as the first.</summary>
    public int Days { get; set; } = 3;

    /// <summary>The hour of the last day the festival starts.</summary>
    public float FestivalHour { get; set; } = 18;

    /// <summary>How long the festival lasts, in game hours, before the reckoning ends the round.</summary>
    public float FestivalHours { get; set; } = 2;

    /// <summary>Renown the town needs at the festival to come through the season.</summary>
    public float RenownTarget { get; set; } = 60;

    /// <summary>Dread at which the valley gives up on the town, ending the round lost.</summary>
    public float DreadLimit { get; set; } = 100;

    /// <summary>Renown per point of a departing adventurer's satisfaction (clamped to <see cref="SatisfactionRange"/>).</summary>
    public float RenownPerSatisfaction { get; set; } = 1;

    /// <summary>The satisfaction a single adventurer can count for, low and high.</summary>
    public float[] SatisfactionRange { get; set; } = [-5, 10];

    public float RenownPerQuestCleared { get; set; } = 10;
    public float RenownPerAdventurerLost { get; set; } = -4;

    /// <summary>Dread each game hour for each quest nobody has answered.</summary>
    public float DreadPerOpenQuestHour { get; set; } = 0.4f;

    public float DreadPerQuestFailed { get; set; } = 12;
    public float DreadPerAdventurerLost { get; set; } = 3;

    /// <summary>Dread a cleared quest lifts.</summary>
    public float DreadPerQuestCleared { get; set; } = -12;
}
