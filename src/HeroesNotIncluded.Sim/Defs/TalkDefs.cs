namespace HeroesNotIncluded.Sim.Defs;

/// <summary>
/// A bot's temperament (<c>data/defs/bots.json</c>): how careful, hasty, greedy, brave and talkative
/// a stand-in player is. A bot draws one by its seed and wobbles it a little, so a solo table isn't
/// five of the same hand: the careless one serves a pot it didn't sniff, the nervy one runs from
/// the dragon while the rest throw water. Never shown: it's only for how the round plays.
/// </summary>
public sealed class BotTemperDef : Def
{
    /// <summary>0..1: how rarely it slips at a game (a short hammering, a key off the wrong hook).</summary>
    public float Care { get; set; } = 0.6f;

    /// <summary>0..1: how quickly it moves on, skipping the chores between orders.</summary>
    public float Haste { get; set; } = 0.4f;

    /// <summary>0..1: how dear it prices, and how slow to come down.</summary>
    public float Greed { get; set; } = 0.4f;

    /// <summary>0..1: how readily it stands up to fire, raiders and the dragon rather than running.</summary>
    public float Nerve { get; set; } = 0.5f;

    /// <summary>0..1: how much it says.</summary>
    public float Chatter { get; set; } = 0.5f;

    /// <summary>How often it's drawn, against the rest.</summary>
    public float Weight { get; set; } = 1f;

    internal override void Resolve(DefDatabase db)
    {
        foreach (var v in (float[])[Care, Haste, Greed, Nerve, Chatter])
            if (v is < 0f or > 1f)
                throw new Exception($"Bot temper '{Name}': care, haste, greed, nerve and chatter are 0 to 1");
        if (Weight <= 0f)
            throw new Exception($"Bot temper '{Name}': weight must be positive");
    }
}

/// <summary>
/// The things people say when something happens (<c>data/defs/barks.json</c>), one def per event:
/// a fire seen, a price called too dear, a guest walking in on another. Each line may be kept for
/// some speakers (<see cref="BarkLine.When"/>: their kind, trade, temper, how drunk); the most
/// fitting lines are the likeliest (<see cref="Speech.Barks"/>). Words only: what a line changes
/// is up to the code that says it.
/// </summary>
public sealed class BarkDef : Def
{
    public List<BarkLine> Lines { get; set; } = [];

    internal override void Resolve(DefDatabase db)
    {
        if (Lines.Count == 0)
            throw new Exception($"Bark '{Name}' has no lines");
        foreach (var l in Lines)
        {
            if (string.IsNullOrWhiteSpace(l.Text) || l.Weight <= 0f)
                throw new Exception($"Bark '{Name}': every line needs words and a positive weight");
            l.Tags = [.. l.When.Select(t => t.ToLowerInvariant())];
        }
    }
}

/// <summary>One way of saying it. <c>{name}</c>, <c>{who}</c>, <c>{thing}</c>, <c>{place}</c> and <c>{n}</c> are filled in when it's said.</summary>
public sealed class BarkLine
{
    public string Text { get; set; } = "";

    /// <summary>Said only by a speaker with all of these tags (lower case): "adventurer", "bot", "smith", "rowdy", "loud"...</summary>
    public List<string> When { get; set; } = [];

    public float Weight { get; set; } = 1f;

    [System.Text.Json.Serialization.JsonIgnore]
    internal string[] Tags { get; set; } = [];
}
