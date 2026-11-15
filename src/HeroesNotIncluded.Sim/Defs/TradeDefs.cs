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

    /// <summary>The practice yard's guided lessons in the trade (<c>Scenarios/Lessons</c>), one game loop each.</summary>
    public List<LessonDef> Lessons { get; set; } = [];

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
        foreach (var lesson in Lessons)
        {
            foreach (var item in lesson.Supply)
                item.ThingDef = db.Thing(item.Def);
            if (lesson.Steps.Count == 0 || lesson.Steps.Select(s => s.Id).Distinct().Count() != lesson.Steps.Count)
                throw new Exception($"Trade '{Name}': lesson '{lesson.Id}' needs steps, each with its own id");
        }
        foreach (var (skill, level) in Skills)
            if (level is < 0 or > Components.Skills.Max)
                throw new Exception($"Trade '{Name}': {skill} {level} is out of range");
        foreach (var (work, priority) in WorkPriorities)
            if (priority is < 0 or > 4)
                throw new Exception($"Trade '{Name}': priority {priority} for {work} isn't between 0 and 4");
    }
}

/// <summary>Customers the practice yard sends, worn the way a lesson needs them (<c>Scenarios.Practice</c>).</summary>
public enum CustomerWear : byte
{
    /// <summary>As the trade wants them: hurt for a healer, chipped blades for a smith, and so on.</summary>
    Trade,

    /// <summary>Their blades whole but dull: a grindstone's work.</summary>
    Dull,

    /// <summary>Their blades chipped and bent: the forge's.</summary>
    Chipped,
}

/// <summary>
/// One guided lesson in the practice yard: what it sets up (customers, a restocked workplace,
/// materials in the pack) and the words for each step. Which step the player is on is worked out
/// from what they know, in code (<c>Scenarios.Lessons</c>); the words live here so they read in the
/// game's voice and can be changed without a build.
/// </summary>
public sealed class LessonDef
{
    /// <summary>Unique within the trade; with it, the id progress is kept under (<c>Blacksmith/sharpen</c>).</summary>
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>A line on what it teaches, shown before it's begun.</summary>
    public string Blurb { get; set; } = "";

    /// <summary>Customers sent when it begins, if any.</summary>
    public CustomerWear? Customers { get; set; }

    /// <summary>The workplace stocked again when it begins.</summary>
    public bool Restock { get; set; }

    /// <summary>Put in the player's pack when it begins: what the work needs that the workplace doesn't keep.</summary>
    public List<ThingCount> Supply { get; set; } = [];

    /// <summary>Its steps in the usual order; a step can come round again (a blade still chipped goes back to the fire).</summary>
    public List<LessonStepDef> Steps { get; set; } = [];

    /// <summary>The step's index by id, -1 if this lesson doesn't list it.</summary>
    public int IndexOf(string step) => Steps.FindIndex(s => s.Id == step);
}

/// <summary>A step of a lesson: an id the code returns, and what the player is told to do (key glyphs as in tips).</summary>
public sealed class LessonStepDef
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";

    /// <summary>Only comes up when something's gone a way it needn't (the forge gone out): listed only while it's the step at hand.</summary>
    public bool Aside { get; set; }
}
