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

    /// <summary>Dread when one of the town's own dies (Afterlife), and what's lifted when they're called back.</summary>
    public float DreadPerTownDeath { get; set; } = 5;
    public float DreadPerRevival { get; set; } = -3;

    /// <summary>Dread a cleared quest lifts.</summary>
    public float DreadPerQuestCleared { get; set; } = -12;

    /// <summary>When the Director (<c>Director.Director</c>) sends its crises, in order.</summary>
    public List<CrisisBeat> Beats { get; set; } = [];

    /// <summary>Renown and Dread for a crisis the town met (drove the raiders off, put the fire out).</summary>
    public float CrisisMetRenown { get; set; } = 6;
    public float CrisisMetDread { get; set; } = -4;

    /// <summary>Dread for a crisis the town didn't meet.</summary>
    public float CrisisUnmetDread { get; set; } = 10;

    /// <summary>The dragon's instead: driven off, the valley sings of it; not, it shudders.</summary>
    public float DragonMetRenown { get; set; } = 20;
    public float DragonUnmetDread { get; set; } = 20;

    /// <summary>
    /// A round's raids against the old storyteller's (<c>Raids.Points</c>, sized for an armed
    /// colony): the town is mostly tradesfolk, and the fighting is the adventurers'.
    /// </summary>
    public float RaidPointsFactor { get; set; } = 0.4f;

    /// <summary>Dread each game hour while something a crisis broke (the bridge) stays down.</summary>
    public float DreadPerBreachHour { get; set; } = 0.5f;

    /// <summary>Chance a raid of strength 1 or more pulls the bridge down as it comes.</summary>
    public float RaidBreaksBridge { get; set; } = 0.6f;

    /// <summary>Game hours a crisis runs before it's over whether the town met it or not.</summary>
    public float CrisisHours { get; set; } = 6;

    /// <summary>Game hours before a raid that word of it comes in, and the town calls for arms.</summary>
    public float RaidWarningHours { get; set; } = 5;

    /// <summary>Silver the town has to pay for arms over the round (<c>Militia.MilitiaSystem</c>).</summary>
    public int TownPurse { get; set; } = 40;

    /// <summary>Silver the town pays for each new weapon hung on the muster rack.</summary>
    public int ArmsPrice { get; set; } = 6;

    /// <summary>The most weapons a call to arms asks for, however many are unarmed.</summary>
    public int ArmsWanted { get; set; } = 6;

    /// <summary>What the carter leaves at the smithy each morning from the second day: ore to smelt, and fuel.</summary>
    public List<ThingCount> OreDelivery { get; set; } = [];

    internal override void Resolve(DefDatabase db)
    {
        foreach (var t in OreDelivery)
            t.Resolve(db);
    }
}

/// <summary>What sort of trouble the Director sends.</summary>
public enum CrisisKind : byte
{
    Raid,
    Fire,
    Shortage,
    Plague,
    Dragon,
}

/// <summary>
/// One crisis in the season: on <see cref="Day"/> (1 is the day of the deal), at a time between
/// <see cref="FromHour"/> and <see cref="ToHour"/> (sooner the more Dread there is), one of
/// <see cref="Kinds"/> by weight, at <see cref="Strength"/> (more with Dread too).
/// </summary>
public sealed class CrisisBeat
{
    public int Day { get; set; } = 1;
    public float FromHour { get; set; } = 10;
    public float ToHour { get; set; } = 16;
    public float Strength { get; set; } = 1;

    /// <summary>Kind name to weight. Kinds the game can't send yet are passed over.</summary>
    public Dictionary<string, float> Kinds { get; set; } = [];

    /// <summary>The kinds and weights, read.</summary>
    public List<(CrisisKind Kind, float Weight)> Weights()
    {
        var list = new List<(CrisisKind, float)>();
        foreach (var (name, weight) in Kinds.OrderBy(k => k.Key, StringComparer.Ordinal))
            if (Enum.TryParse<CrisisKind>(name, ignoreCase: true, out var kind) && weight > 0)
                list.Add((kind, weight));
        return list;
    }
}
