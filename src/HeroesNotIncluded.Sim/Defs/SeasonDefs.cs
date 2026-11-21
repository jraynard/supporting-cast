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
    public float FestivalHour { get; set; } = 17;

    /// <summary>
    /// The hour of the last day the festival's things start going up in the square: each a site
    /// the builders (the carpenter) put up that afternoon (<c>Round.FestivalSystem</c>). Whatever isn't up by the
    /// bell is put up then. At or after <see cref="FestivalHour"/>, everything appears at the bell.
    /// </summary>
    public float FestivalSetupHour { get; set; } = 11;

    /// <summary>How long the festival lasts, in game hours, before the reckoning ends the round (<see cref="FestivalDef"/>).</summary>
    public float FestivalHours { get; set; } = 5;

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

    /// <summary>How the Director reads the town and answers it (<c>Director.Tension</c>, <c>Director.RuleMind</c>).</summary>
    public PacingDef Pacing { get; set; } = new();

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

    /// <summary>
    /// What an AI villager counts toward a round's raid against a player's character (<c>Raids.Points</c>): the band
    /// grows with the players, who do the fighting, more than with the town.
    /// </summary>
    public float RaidAiShare { get; set; } = 1f;

    /// <summary>Dread each game hour while something a crisis broke (the bridge) stays down.</summary>
    public float DreadPerBreachHour { get; set; } = 0.5f;

    /// <summary>Chance a raid of strength 1 or more pulls the bridge down as it comes.</summary>
    public float RaidBreaksBridge { get; set; } = 0.6f;

    /// <summary>Game hours a crisis runs before it's over whether the town met it or not.</summary>
    public float CrisisHours { get; set; } = 6;

    /// <summary>How a brawl at the inn goes (<c>Hospitality.Brawling.BrawlCrisis</c>).</summary>
    public BrawlDef Brawl { get; set; } = new();

    /// <summary>Game hours before a raid that word of it comes in, and the town calls for arms.</summary>
    public float RaidWarningHours { get; set; } = 5;

    /// <summary>Silver the town has to pay for arms over the round (<c>Militia.MilitiaSystem</c>).</summary>
    public int TownPurse { get; set; } = 40;

    /// <summary>Silver the town pays for each new weapon hung on the muster rack.</summary>
    public int ArmsPrice { get; set; } = 6;

    /// <summary>The most weapons a call to arms asks for, however many are unarmed.</summary>
    public int ArmsWanted { get; set; } = 6;

    /// <summary>
    /// The share of the most of a thing the carter brings a morning (<see cref="SupplyDef.Max"/>) while a player's trade supplies it
    /// (<see cref="TradeDef.Supplies"/>: the miner's ore): enough to get by on, not to keep the forge busy.
    /// </summary>
    public float SuppliedShare { get; set; } = 0.25f;

    /// <summary>
    /// Wood laid in the barn's stockpile at the deal, on top of the settlers' pile: the town's fires
    /// burn about ten score logs a day, and players fetching fuel by hand need some left to fetch.
    /// </summary>
    public int BarnWood { get; set; }

    /// <summary>
    /// Charcoal in the smithy's fuel bin at the deal: a day and a half's firing, so a smith new to the trade has time
    /// to find the kiln before the forge goes cold (nobody brings charcoal).
    /// </summary>
    public int SmithyCharcoal { get; set; } = 30;

    /// <summary>Cloth in the shrine's basket at the deal, to tear into bandages; the carter brings more to order (supplies.json).</summary>
    public int ShrineCloth { get; set; } = 4;

    /// <summary>Planks laid in the barn's stockpile at the deal: for what the town makes (hafts, contraptions, furniture), till the carpenter saws more.</summary>
    public int BarnPlanks { get; set; }

    /// <summary>
    /// More in the smithy's chest at the deal, beside its iron ore and planks: bronze for what's asked for in it, and copper
    /// and tin to alloy more at the kiln (copper bars from the ore, then bronze).
    /// </summary>
    public List<ThingCount> SmithyStock { get; set; } = [];

    /// <summary>What the woodcutter leaves in the barn each morning from the second day: timber, for building and mending.</summary>
    public List<ThingCount> WoodDelivery { get; set; } = [];

    /// <summary>
    /// Firewood laid in the barn's stockpile at the deal: the town's hearths, ovens and kilns burn it
    /// (not timber). With <see cref="FirewoodDelivery"/> it should nearly last the season, so the
    /// carpenter splits logs only to top it up, or in a shortage, when the carter brings none.
    /// </summary>
    public int BarnFirewood { get; set; }

    /// <summary>Firewood the carter leaves in the barn each morning from the second day, but not in a shortage.</summary>
    public List<ThingCount> FirewoodDelivery { get; set; } = [];


    internal override void Resolve(DefDatabase db)
    {
        Pacing.Resolve(Name);
        foreach (var beat in Beats)
        {
            if (beat.FollowsQuest.Length > 0 && !db.AllQuests.Any(q => q.Name == beat.FollowsQuest))
                throw new Exception($"Season {Name}: a beat follows quest '{beat.FollowsQuest}', and there's no such quest");
            if (beat.Replaces.Length > 0 && !Beats.Any(b => b != beat && b.Id == beat.Replaces))
                throw new Exception($"Season {Name}: a beat replaces '{beat.Replaces}', and no other beat has that id");
            if (beat.AfterHours.Length != 2 || beat.AfterHours[1] < beat.AfterHours[0])
                throw new Exception($"Season {Name}: a beat's afterHours is [earliest, latest]");
        }
        foreach (var t in SmithyStock)
            t.Resolve(db);
        foreach (var t in WoodDelivery)
            t.Resolve(db);
        foreach (var t in FirewoodDelivery)
            t.Resolve(db);
    }
}

/// <summary>
/// What sort of trouble the Director sends: a name the crisis registry knows
/// (<see cref="Director.CrisisTypes"/>, <see cref="Modding.SimRegistry.Crisis"/>), so a mod can add a
/// kind as the game adds its own. The game's are named here for its code and tests to say.
/// </summary>
public readonly record struct CrisisKind
{
    private readonly string? _name;

    public CrisisKind(string name) => _name = name;

    public string Name => _name ?? "";

    public static readonly CrisisKind Raid = new("Raid");
    public static readonly CrisisKind Fire = new("Fire");
    public static readonly CrisisKind Shortage = new("Shortage");
    public static readonly CrisisKind Plague = new("Plague");
    public static readonly CrisisKind Dragon = new("Dragon");
    public static readonly CrisisKind Brawl = new("Brawl");

    /// <summary>The game's kinds as saves before format 65 numbered them.</summary>
    internal static CrisisKind? FromOld(int n) => n switch
    {
        0 => Raid,
        1 => Fire,
        2 => Shortage,
        3 => Plague,
        4 => Dragon,
        _ => null,
    };

    public override string ToString() => Name;
}

/// <summary>
/// One crisis in the season: on <see cref="Day"/> (1 is the day of the deal), at a time between
/// <see cref="FromHour"/> and <see cref="ToHour"/> (sooner the more Dread there is), one of
/// <see cref="Kinds"/> by weight, at <see cref="Strength"/> (more with Dread too).
/// </summary>
public sealed class CrisisBeat
{
    /// <summary>A name for the beat, for another to <see cref="Replaces"/>; optional.</summary>
    public string Id { get; set; } = "";

    public int Day { get; set; } = 1;
    public float FromHour { get; set; } = 10;
    public float ToHour { get; set; } = 16;
    public float Strength { get; set; } = 1;

    /// <summary>
    /// A quest (<see cref="QuestDef"/> name) whose party this beat follows: it comes only if a party
    /// sets out on it, <see cref="AfterHours"/> after they do, and never once the festival's going up.
    /// <see cref="Day"/> and the hours then don't apply. What happens at the quest stirs the valley.
    /// </summary>
    public string FollowsQuest { get; set; } = "";

    /// <summary>Game hours after the party sets out, earliest and latest (<see cref="FollowsQuest"/>).</summary>
    public float[] AfterHours { get; set; } = [24, 36];

    /// <summary>The <see cref="Id"/> of a beat this one comes in place of, once it's set off by its quest.</summary>
    public string Replaces { get; set; } = "";

    /// <summary>What the town hears when its quest sets it off: a sign, not a warning (optional).</summary>
    public string Omen { get; set; } = "";

    /// <summary>Kind name to weight. Kinds the game can't send yet are passed over.</summary>
    public Dictionary<string, float> Kinds { get; set; } = [];

    /// <summary>The kinds and weights, read.</summary>
    public List<(CrisisKind Kind, float Weight)> Weights()
    {
        var list = new List<(CrisisKind, float)>();
        foreach (var (name, weight) in Kinds.OrderBy(k => k.Key, StringComparer.Ordinal))
            if (Director.CrisisTypes.Find(name) is { } type && weight > 0)
                list.Add((type.Kind, weight));
        return list;
    }
}

/// <summary>
/// A brawl at the inn (<c>Hospitality.Brawling.BrawlCrisis</c>): a rowdy crowd (a <see cref="PartyDef.Crowd"/>)
/// comes in loud with drink and starts something. Its heat (0..1) climbs with how many are still at it
/// and boils over into the street at 1; it eases as they're settled.
/// </summary>
public sealed class BrawlDef
{
    /// <summary>Heat a game hour with <see cref="PerBrawlers"/> still fighting (more or fewer in proportion).</summary>
    public float RisePerHour { get; set; } = 0.4f;

    /// <summary>The brawlers <see cref="RisePerHour"/> is for.</summary>
    public float PerBrawlers { get; set; } = 4;

    /// <summary>Heat lost a game hour regardless: tempers tire.</summary>
    public float CoolPerHour { get; set; } = 0.1f;

    /// <summary>Heat lost when the ringleader's settled (and the rest who aren't rowdy lose heart).</summary>
    public float RingleaderCools { get; set; } = 0.3f;

    /// <summary>How deep in their cups the crowd comes in (<c>Tipsy</c>; 0.5 is loud).</summary>
    public float ArriveTipsy { get; set; } = 0.55f;

    /// <summary>A bystander struck by a brawler joins in at this much drink, or if they're rowdy.</summary>
    public float JoinTipsy { get; set; } = 0.5f;

    /// <summary>Crowd members at strength 1 (more or fewer with the beat's strength), at least 3.</summary>
    public int Crowd { get; set; } = 4;

    /// <summary>Heat from which brawlers go for the furniture (<see cref="ThingDef.BreaksInBrawls"/>).</summary>
    public float BreakAt { get; set; } = 0.4f;

    /// <summary>The chance, each turn past <see cref="BreakAt"/>, a brawler takes it out on a table or a stool instead.</summary>
    public float SmashChance { get; set; } = 0.2f;

    /// <summary>Blows a piece takes before it breaks.</summary>
    public int BlowsToBreak { get; set; } = 3;

    /// <summary>The chance, each turn past <see cref="BreakAt"/>, a brawler grabs a stool (<see cref="ThingDef.ThrownInBrawls"/>) and hurls it.</summary>
    public float ThrowChance { get; set; } = 0.15f;

    /// <summary>The chance a brawler's punch, past <see cref="BreakAt"/>, sends them into the table or stool behind them: it takes two blows' worth.</summary>
    public float ShoveChance { get; set; } = 0.35f;

    /// <summary>How much drink a bucket of well water over a brawler takes off (<c>Tipsy</c>): under clumsy after it, they're out of it.</summary>
    public float DouseSobers { get; set; } = 0.35f;

    /// <summary>Heat a game hour a tune calms (or a sour one riles) for every <see cref="PerBrawlers"/> who hear it.</summary>
    public float MusicPerHour { get; set; } = 0.4f;

    /// <summary>The chance, each look, a brawler who hears a reel they could dance to dances instead.</summary>
    public float DanceChance { get; set; } = 0.35f;

    /// <summary>Silver "Drinks on the house!" costs the keeper for each brawler who takes them up on it.</summary>
    public int DrinksCost { get; set; } = 2;

    /// <summary>The chance a brawl boiling over knocks a lamp over: the inn's alight too (a fire crisis of its own).</summary>
    public float FireOnBoil { get; set; } = 0.35f;

    /// <summary>What a crowd that boiled over, or was never settled, thinks of the town as it goes.</summary>
    public float SourSatisfaction { get; set; } = -3f;
}

/// <summary>
/// How the Director paces the season (<c>Director.Tension</c>, <c>Director.RuleMind</c>). It reads
/// the town as it goes (hurt, burning, ahead of or behind where a town on course would be) and
/// eases off a town that's struggling and pushes one that's coasting, like a good game master,
/// rather than piling on whoever's already losing.
/// </summary>
public sealed class PacingDef
{
    /// <summary>
    /// The share of the Renown target a town on course has when the festival starts. The rest comes
    /// at the festival's end, from every adventurer still in town (<c>Reckoning.FinalCount</c>).
    /// </summary>
    public float RenownAtFestival { get; set; } = 0.6f;

    /// <summary>A beat's strength times this at most, eased off (first) and pushed (second).</summary>
    public float[] StrengthRange { get; set; } = [0.75f, 1.35f];

    /// <summary>Harm (0..1) under which the town counts as calm: a beat waits in its window for that.</summary>
    public float CalmHarm { get; set; } = 0.35f;

    /// <summary>Quiet game hours (calm, nothing under way) before an incident may come.</summary>
    public float LullHours { get; set; } = 3;

    /// <summary>The fewest game hours from one incident to the next.</summary>
    public float IncidentGapHours { get; set; } = 4;

    /// <summary>No incident this many game hours before a beat is due.</summary>
    public float BeatClearHours { get; set; } = 3;

    /// <summary>Push (-1..1) under which a town gets no incidents: it has enough on.</summary>
    public float IncidentPushFloor { get; set; } = -0.25f;

    /// <summary>A crisis kind's weight once the season's already had one.</summary>
    public float SeenWeight { get; set; } = 0.4f;

    /// <summary>A crisis kind's (or incident's) weight when a player holds the trade that answers it: trouble is theirs to meet.</summary>
    public float PlayerWeight { get; set; } = 1.5f;

    /// <summary>A crisis kind's weight when nobody able holds the trade that answers it: trouble nobody can meet isn't a story.</summary>
    public float NobodyWeight { get; set; } = 0.25f;

    /// <summary>Crisis kind name to the town role whose trade answers it ("Plague": "Shrine").</summary>
    public Dictionary<string, string> Answers { get; set; } = [];

    /// <summary><see cref="Answers"/>, read.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Dictionary<CrisisKind, TownRole> AnsweredBy { get; } = [];

    internal void Resolve(string season)
    {
        if (StrengthRange.Length != 2 || StrengthRange[0] > 1 || StrengthRange[1] < 1)
            throw new Exception($"Season {season}: pacing's strengthRange is [at most 1, at least 1]");
        AnsweredBy.Clear();
        foreach (var (kind, role) in Answers)
            if (Director.CrisisTypes.Find(kind) is { } type) // a kind whose mod is off answers nothing
                AnsweredBy[type.Kind] = TownRoles.Parse(role, $"Season {season} pacing");
    }
}

/// <summary>What an incident does (<c>Director.Incidents</c>).</summary>
public enum IncidentKind : byte
{
    /// <summary>One adventurer in town comes down with a fever (<c>Healing.FeverSystem</c>): smaller than the plague, and no crisis.</summary>
    Fever,

    /// <summary>A few rats get into the kitchen's larder (<c>Hospitality.RatSystem</c>).</summary>
    Rats,

    /// <summary>
    /// An adventurer in town sends a commission up to the notice board (<c>Shops.Commissions</c>), paid
    /// from their own purse: one of its wants that a shop a player keeps makes.
    /// </summary>
    Commission,
}

/// <summary>
/// A small happening between the season's crises (data/defs/incidents.json): no Renown or Dread
/// of its own, just something for the town to deal with in a lull. The Director calls one when the
/// town's been quiet a while (<see cref="PacingDef.LullHours"/>), by weight among those that could
/// happen now, more likely when a player holds the trade it's for.
/// </summary>
public sealed class IncidentDef : Def
{
    public IncidentKind Kind { get; set; }
    public float Weight { get; set; } = 1;

    /// <summary>The first day of the season (1 is the deal's) it can come.</summary>
    public int MinDay { get; set; } = 1;

    /// <summary>The most times it comes in a season.</summary>
    public int MaxPerRound { get; set; } = 1;

    /// <summary>The town role whose trade it's for ("Kitchen"); empty for anyone's.</summary>
    public string For { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public TownRole ForRole { get; private set; }

    /// <summary>How many there are (rats).</summary>
    public int Count { get; set; } = 1;

    /// <summary>A commission's wants, one picked: what's wanted, how many, and for how much.</summary>
    public List<IncidentWant> Wants { get; set; } = [];

    /// <summary>What the one it starts with says aloud (the fevered, the commission's poster); one picked.</summary>
    public List<string> Says { get; set; } = [];

    /// <summary>The headline, for those it's for (or everyone if it's nobody's): {name} is who it's about.</summary>
    public string Message { get; set; } = "";

    internal override void Resolve(DefDatabase db)
    {
        ForRole = TownRoles.Parse(For, $"Incident {Name}");
        foreach (var want in Wants)
            want.Resolve(db, Name);
        if (Kind == IncidentKind.Commission && Wants.Count == 0)
            throw new Exception($"Incident {Name}: a commission needs wants");
    }
}

/// <summary>Something a commission incident asks to be made.</summary>
public sealed class IncidentWant
{
    public string Thing { get; set; } = "";
    public int Count { get; set; } = 1;
    public int Price { get; set; } = 1;
    public float Hours { get; set; } = 8;

    [System.Text.Json.Serialization.JsonIgnore]
    public ThingDef ThingDef { get; private set; } = null!;

    internal void Resolve(DefDatabase db, string incident) =>
        ThingDef = db.TryThing(Thing, out var def) ? def : throw new Exception($"Incident {incident}: no thing '{Thing}'");
}
