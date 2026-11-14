using System.Text.Json.Serialization;

namespace HeroesNotIncluded.Sim.Defs;

/// <summary>
/// How the season's last evening runs (data/defs/festival.json, <c>Round.FestivalSystem</c>): an
/// opening while the town gathers, the revels (the feast, the dancing, the show table and the
/// games), then the ceremony that ends the round. The festival is the season's payoff and the town's
/// last chance: what the visitors still in town think is counted at their toast, not before.
/// The last one loaded is in effect.
/// </summary>
public sealed class FestivalDef : Def
{
    /// <summary>Game hours from the festival's start before the revels: the bell, and everyone to the square.</summary>
    public float OpeningHours { get; set; } = 0.5f;

    /// <summary>Game hours at the festival's end given to the ceremony (toasts, honours, the verdict).</summary>
    public float CeremonyHours { get; set; } = 1.25f;

    /// <summary>Game minutes for the ceremony's bell: the crowd gathers round the well.</summary>
    public float BellMinutes { get; set; } = 10;

    /// <summary>Game minutes each party's toast is given.</summary>
    public float ToastMinutes { get; set; } = 6;

    /// <summary>Game minutes each honour is given.</summary>
    public float HonourMinutes { get; set; } = 4;

    /// <summary>Game minutes left at the end for the verdict to be heard before the reveal.</summary>
    public float VerdictMinutes { get; set; } = 8;

    /// <summary>At most this many honours are called.</summary>
    public int MaxHonours { get; set; } = 5;

    /// <summary>What the town gets up to in the revels, by weight (<see cref="FestivalActivity"/> names).</summary>
    public Dictionary<string, float> Activities { get; set; } = [];

    /// <summary>What visitors get up to in the revels: more looking at the show table and the games, by weight.</summary>
    public Dictionary<string, float> VisitorActivities { get; set; } = [];

    /// <summary><see cref="Activities"/> parsed, in the enum's order (so a pick is the same on every peer).</summary>
    [JsonIgnore] public IReadOnlyList<(FestivalActivity Activity, float Weight)> TownWeights { get; private set; } = [];

    /// <summary><see cref="VisitorActivities"/> parsed, in the enum's order.</summary>
    [JsonIgnore] public IReadOnlyList<(FestivalActivity Activity, float Weight)> VisitorWeights { get; private set; } = [];

    /// <summary>
    /// A visitor's satisfaction for each piece on the show table they look over, by its quality
    /// (crude, plain, fine, masterwork): what a town can make is part of what it's known for.
    /// </summary>
    public float[] AdmireSatisfaction { get; set; } = [-0.5f, 0.25f, 1f, 2f];

    /// <summary>How near the show table (cells) a visitor looks the pieces over.</summary>
    public int AdmireReach { get; set; } = 3;

    /// <summary>Satisfaction for a visitor watching someone take a turn at a game, and its most from watching all evening.</summary>
    public float WatchSatisfaction { get; set; } = 0.3f;
    public float WatchCap { get; set; } = 1.5f;

    /// <summary>How near a game (cells) a turn at it is seen.</summary>
    public int WatchReach { get; set; } = 6;

    /// <summary>Chance, each look, that someone at a game who the AI runs has a go.</summary>
    public float TryChance { get; set; } = 0.25f;

    /// <summary>Silver to each game's winner, from the festival's purse.</summary>
    public int PrizeSilver { get; set; } = 8;

    /// <summary>Renown for each game that had a winner: travellers talk of a good night.</summary>
    public float GameRenown { get; set; } = 2;

    /// <summary>The festival's games, each at a thing in the square.</summary>
    public List<FestivalGameDef> Games { get; set; } = [];

    internal override void Resolve(DefDatabase db)
    {
        foreach (var g in Games)
            g.Resolve(db);
        if (AdmireSatisfaction.Length != 4)
            throw new Exception($"Festival '{Name}': admireSatisfaction needs one value per quality (4)");
        TownWeights = Parse(Activities, "activities");
        VisitorWeights = Parse(VisitorActivities, "visitorActivities");
    }

    private List<(FestivalActivity, float)> Parse(Dictionary<string, float> weights, string what)
    {
        var parsed = new List<(FestivalActivity, float)>();
        foreach (var (key, weight) in weights)
        {
            if (!Enum.TryParse<FestivalActivity>(key, ignoreCase: true, out var activity) || activity == FestivalActivity.Gather)
                throw new Exception($"Festival '{Name}': unknown activity '{key}' in {what}");
            if (weight > 0)
                parsed.Add((activity, weight));
        }
        return parsed.OrderBy(p => p.Item1).ToList();
    }

    /// <summary>The game played at a thing of this def, or null.</summary>
    public FestivalGameDef? GameAt(ThingDef def) => Games.Find(g => g.ThingDef == def);
}

/// <summary>A contest at the festival: played at its thing, with a hand game for a player and a roll of the skill for the AI.</summary>
public sealed class FestivalGameDef
{
    /// <summary>Its name in code and saves: "Striker", "Butts".</summary>
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";

    /// <summary>The thing it's played at.</summary>
    public string Def { get; set; } = "";

    /// <summary>What F at it says: "Swing at the striker".</summary>
    public string Verb { get; set; } = "";

    /// <summary>What it rewards in the AI's turns, and what a player's practice lends their hand game.</summary>
    public Components.Skill Skill { get; set; }

    /// <summary>The honour its winner is called up for: "the strongest arm in the valley".</summary>
    public string Honour { get; set; } = "";

    [JsonIgnore] public ThingDef ThingDef { get; private set; } = null!;

    internal void Resolve(DefDatabase db)
    {
        if (string.IsNullOrEmpty(Name))
            throw new Exception("A festival game needs a name");
        ThingDef = db.Thing(Def);
    }
}

/// <summary>What someone does with their evening at the festival (<c>Ai.FestivalJob</c>).</summary>
public enum FestivalActivity : byte
{
    /// <summary>Stand about near the well and talk.</summary>
    Mill,

    /// <summary>Sit at the feast tables.</summary>
    Feast,

    /// <summary>Warm themselves at the bonfire.</summary>
    Bonfire,

    /// <summary>Dance round the bonfire.</summary>
    Dance,

    /// <summary>Watch (and maybe try) a game.</summary>
    Games,

    /// <summary>Look over what's on the show table.</summary>
    Show,

    /// <summary>Gather round the well for the ceremony.</summary>
    Gather,
}
