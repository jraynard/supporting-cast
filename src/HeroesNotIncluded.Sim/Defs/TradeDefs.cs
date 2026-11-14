using System.Text.Json.Serialization;

namespace HeroesNotIncluded.Sim.Defs;

/// <summary>
/// A role in the village, dealt to a player or held by an AI villager. Called a trade because
/// "job" already means an AI task. A trade decides where its holder works and, while the AI is
/// running them, what they put first, so a town with empty seats still gets its work done.
/// </summary>
public sealed class TradeDef : Def
{
    /// <summary>The building this trade works from, by def name; empty for none.</summary>
    public string Workplace { get; set; } = "";

    /// <summary>Any number may hold it, so there's always something left to deal.</summary>
    public bool Unlimited { get; set; }

    /// <summary>Changes from the default work priorities (1 highest, 4 lowest, 0 disabled).</summary>
    public Dictionary<WorkType, int> WorkPriorities { get; set; } = [];

    /// <summary>
    /// Stations (thing def names) whose every recipe the holder knows. A trade is its know-how: the
    /// blacksmith shouldn't have to rediscover iron. Skill still decides which they can make yet.
    /// </summary>
    public List<string> Knows { get; set; } = [];

    /// <summary>
    /// The trade's working stations at its workplace (thing def names): marked in the world for a
    /// newcomer until they've been to each, so the first thing a new player sees is where to go.
    /// </summary>
    public List<string> Stations { get; set; } = [];

    /// <summary>A few short first-day lines for a player new to the trade, shown one at a time.</summary>
    public List<string> Tips { get; set; } = [];

    /// <summary>The least the holder has in a skill: a smith can smith.</summary>
    public Dictionary<Components.Skill, int> Skills { get; set; } = [];

    /// <summary>
    /// The holder's mana, for a trade with the gift (the Healer's empathy, <c>Healing/</c>); 0 for
    /// none. Having it is what lets them lay on hands and cast.
    /// </summary>
    public float Mana { get; set; }

    /// <summary>Matches parties to quests (<c>Expeditions/</c>, the Innkeeper's): starts the round with a guess at every quest.</summary>
    public bool Brokers { get; set; }

    /// <summary>What the holder starts the round with: the tools of the trade and a little stock.</summary>
    public List<ThingCount> Kit { get; set; } = [];

    /// <summary>
    /// What's kept for the holder at the workplace rather than carried: put in its stash (a
    /// <see cref="BuildingDef.Stash"/>, under the inn's bar) and fetched when trouble comes.
    /// </summary>
    public List<ThingCount> Stash { get; set; } = [];

    /// <summary>
    /// The services at its workplace this trade sees to, when it shares one (the inn: the
    /// innkeeper the bar, rooms and quests, the cook the meals). Empty: everything served there.
    /// It's what its AI takes on, and the prices on the sign its holder sets.
    /// </summary>
    public List<ServiceKind> Serves { get; set; } = [];

    /// <summary>The holder is given a key to every lock fitted at its workplace (the innkeeper's storeroom).</summary>
    public bool HoldsKeys { get; set; }

    /// <summary>
    /// The holder may dig, mine, chop and forage in a round (the labourer, for now). Nobody else
    /// does: that's the Miner's and Forager's work, when they come.
    /// </summary>
    public bool Gathers { get; set; }

    /// <summary>The holder mends what crises break in town (<c>Tinkering.Repairs</c>): the tinker, the bridge.</summary>
    public bool MendsTown { get; set; }

    /// <summary>The holder names the shop (<c>Shops.ShopName</c>): the innkeeper, not the cook.</summary>
    public bool NamesShop { get; set; } = true;

    /// <summary>Does this trade see to this service at its workplace?</summary>
    public bool Serving(ServiceKind kind) => Serves.Count == 0 || Serves.Contains(kind);

    [JsonIgnore]
    public List<ThingDef> KnownStations { get; } = [];

    [JsonIgnore]
    public List<ThingDef> StationDefs { get; } = [];

    [JsonIgnore]
    public BuildingDef? WorkplaceDef { get; private set; }

    internal override void Resolve(DefDatabase db)
    {
        if (Workplace.Length > 0)
            WorkplaceDef = db.Building(Workplace);
        KnownStations.Clear();
        foreach (var station in Knows)
            KnownStations.Add(db.Thing(station));
        StationDefs.Clear();
        foreach (var station in Stations)
            StationDefs.Add(db.Thing(station));
        foreach (var item in Kit)
            item.ThingDef = db.Thing(item.Def);
        foreach (var item in Stash)
            item.ThingDef = db.Thing(item.Def);
        foreach (var (skill, level) in Skills)
            if (level is < 0 or > Components.Skills.Max)
                throw new Exception($"Trade '{Name}': {skill} {level} is out of range");
        foreach (var (work, priority) in WorkPriorities)
            if (priority is < 0 or > 4)
                throw new Exception($"Trade '{Name}': priority {priority} for {work} isn't between 0 and 4");
    }
}
