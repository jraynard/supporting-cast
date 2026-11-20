using System.Text.Json.Serialization;

namespace HeroesNotIncluded.Sim.Defs;

/// <summary>
/// A role in the village, dealt to a player or held by an AI villager. Called a trade because
/// "job" already means an AI task. A trade decides where its holder works and, while the AI is
/// running them, what they put first, so a town with empty seats still gets its work done.
/// </summary>
public sealed class TradeDef : Def
{
    /// <summary>
    /// The town role (<see cref="TownRole"/>) of the building this trade works from: "Kitchen",
    /// "Tavern". Empty for none. Which building that is depends on the map.
    /// </summary>
    public string Workplace { get; set; } = "";

    /// <summary>
    /// Names the trade went by before (the empath was the "Empath"): looked up by them, it's still
    /// found, so saves, replays, saved characters' progress and the command line from then still work.
    /// </summary>
    public List<string> Formerly { get; set; } = [];

    /// <summary>Any number may hold it, so there's always something left to deal.</summary>
    public bool Unlimited { get; set; }

    /// <summary>
    /// Only ever a player's, and only one who asked for it (the musician): never dealt to an AI villager or to a
    /// seat that didn't want it, and its player comes to town as a newcomer rather than taking a villager's
    /// place. A round nobody asks for it in plays as if it weren't there.
    /// </summary>
    public bool PlayerOnly { get; set; }

    /// <summary>
    /// A trade that comes to town by the road, said with this line as it does (<c>{name}</c> for who): its character starts
    /// a little way down the road a party would come in by and walks into the square (<see cref="Systems.Players.ArriveByRoad"/>).
    /// Null: they're simply there.
    /// </summary>
    public string? ArrivesByRoad { get; set; }

    /// <summary>
    /// Their home is a room at the inn (<see cref="Systems.Homes"/>), on the house: kept for them, never let, while there's
    /// one free; a cottage if not. The musician's: the inn's glad of the music, and they've no cottage of their own.
    /// </summary>
    public bool LodgesAtInn { get; set; }

    /// <summary>
    /// Who the trade suits, shown where a trade is chosen: a new player should be able to find a calm trade with
    /// little to learn first, and an old hand the busy one. Presentation only.
    /// </summary>
    public TradeLevel? Level { get; set; }

    /// <summary>
    /// The old key for <see cref="Level"/> (easygoing, steady, busy), still read so a mod written before it loads:
    /// easygoing reads as beginner, steady as intermediate, busy as experienced. A <c>level</c> wins over it.
    /// </summary>
    [Obsolete("Use Level (\"level\" in a trade def).")]
    public string? Pace
    {
        get => null;
        set => Level ??= value?.ToLowerInvariant() switch
        {
            "easygoing" => TradeLevel.Beginner,
            "steady" => TradeLevel.Intermediate,
            "busy" => TradeLevel.Experienced,
            _ => null,
        };
    }

    /// <summary>Changes from the default work priorities (1 highest, 4 lowest, 0 disabled).</summary>
    public Dictionary<WorkType, int> WorkPriorities { get; set; } = [];

    /// <summary>
    /// Stations (thing def names) whose every recipe the holder knows. A trade is its know-how: the
    /// blacksmith shouldn't have to rediscover iron. Skill still decides which they can make yet.
    /// </summary>
    public List<string> Knows { get; set; } = [];

    /// <summary>The trade's working stations at its workplace (thing def names): what its holder works at.</summary>
    public List<string> Stations { get; set; } = [];

    /// <summary>
    /// What the trade is about and how it earns, in a line or two: the first thing its tutorial says,
    /// because a newcomer told only which key to press doesn't know why they're pressing it.
    /// </summary>
    public string TutorialIntro { get; set; } = "";

    /// <summary>
    /// The trade's tutorial chapters (<c>Scenarios/Lessons</c>), one game loop each: those marked
    /// <see cref="LessonDef.Core"/> are run in order, the rest offered after.
    /// </summary>
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
    /// The holder may dig, mine, chop and forage in a round (the labourer). A trade may have only a share
    /// of it: <see cref="Fells"/> (the carpenter's trees), <see cref="Mines"/> (the miner's rock).
    /// </summary>
    public bool Gathers { get; set; }

    /// <summary>The holder may fell trees in a round, by hand (the carpenter: Carpentry.FellSession), though it gathers nothing else.</summary>
    public bool Fells { get; set; }

    /// <summary>
    /// The holder may dig and mine in a round (the miner, at the hill), though it gathers nothing else:
    /// no crops, no timber. <see cref="Gathers"/> includes this.
    /// </summary>
    public bool Mines { get; set; }

    /// <summary>
    /// Things (def names) the holder brings the town by their own work, which the carter would otherwise bring
    /// (the miner's iron ore): while a player holds the trade, the carter brings only the season's
    /// <see cref="SeasonDef.SuppliedShare"/> of each, so the round leans on them, and a round without them still has it.
    /// </summary>
    public List<string> Supplies { get; set; } = [];

    /// <summary>
    /// What an AI holder makes at its workplace without a bill, to keep it in (<c>Crafting.Kept</c>): the smith's
    /// charcoal, baked at the smithy's kiln while there's little left. A player keeps their own.
    /// </summary>
    public List<TradeKeep> Keeps { get; set; } = [];

    /// <summary>The holder mends what crises break in town (<c>Tinkering.Repairs</c>): the tinker, the bridge.</summary>
    public bool MendsTown { get; set; }

    /// <summary>Puts up what the town builds for an occasion (the festival's stalls and lanterns), as the labourer does, without gathering.</summary>
    public bool Builds { get; set; }

    /// <summary>
    /// What its workplace runs short of that a labourer could fetch (<c>Favours.Wants</c>): held by
    /// the AI, it pins an errand to the notice board when there's less than <see cref="TradeWant.Below"/>
    /// about the place. It's the labourer's living, when no player's posting.
    /// </summary>
    public List<TradeWant> Wants { get; set; } = [];

    /// <summary>The holder names the shop (<c>Shops.ShopName</c>): the innkeeper, not the cook.</summary>
    public bool NamesShop { get; set; } = true;

    /// <summary>Does this trade see to this service at its workplace?</summary>
    public bool Serving(ServiceKind kind) => Serves.Count == 0 || Serves.Contains(kind);

    [JsonIgnore]
    public List<ThingDef> KnownStations { get; } = [];

    [JsonIgnore]
    public List<ThingDef> StationDefs { get; } = [];

    /// <summary>The role of the building it works from (<see cref="Workplace"/>); <see cref="TownRole.None"/> for none.</summary>
    [JsonIgnore]
    public TownRole Role { get; private set; }

    /// <summary>Does it work from a building at all (the labourer doesn't)?</summary>
    [JsonIgnore]
    public bool HasWorkplace => Role != TownRole.None;

    /// <summary>Does it work from a building of this kind (its def has the trade's role)?</summary>
    public bool WorksAt(BuildingDef? def) => def != null && def.Has(Role);

    internal override void Resolve(DefDatabase db)
    {
        Role = TownRoles.Parse(Workplace, $"Trade '{Name}' workplace");
        foreach (var want in Wants)
        {
            want.ThingDef = db.Thing(want.Thing);
            if (want.ThingDef.Category != ThingCategory.Item || want.Below < 1 || want.Count < 1 || want.Price < 0 || want.Hours <= 0)
                throw new Exception($"Trade '{Name}': a want of {want.Thing} needs an item, below and count of 1 or more, a price and hours");
        }
        foreach (var keep in Keeps)
        {
            keep.RecipeDef = db.Recipe(keep.Recipe);
            if (keep.RecipeDef.Station.Length == 0 || keep.RecipeDef.Products.Count == 0 || keep.Below < 1)
                throw new Exception($"Trade '{Name}': a keep of {keep.Recipe} needs a recipe made at a station, with a product, and below of 1 or more");
        }
        KnownStations.Clear();
        foreach (var station in Knows)
            KnownStations.Add(db.Thing(station));
        StationDefs.Clear();
        foreach (var station in Stations)
            StationDefs.Add(db.Thing(station));
        foreach (var thing in Supplies)
            db.Thing(thing); // a name that isn't a thing is a mistake in the def
        foreach (var item in Kit)
            item.ThingDef = db.Thing(item.Def);
        foreach (var item in Stash)
            item.ThingDef = db.Thing(item.Def);
        foreach (var lesson in Lessons)
        {
            if (lesson.Id == Scenarios.Lessons.Tutorial.BasicsId || db.SharedLessons.Any(l => l.Id == lesson.Id))
                throw new Exception($"Trade '{Name}': lesson id '{lesson.Id}' is a shared chapter's");
            lesson.Resolve(db, $"Trade '{Name}'");
        }
        foreach (var (skill, level) in Skills)
            if (level is < 0 or > Components.Skills.Max)
                throw new Exception($"Trade '{Name}': {skill} {level} is out of range");
        foreach (var (work, priority) in WorkPriorities)
            if (priority is < 0 or > 4)
                throw new Exception($"Trade '{Name}': priority {priority} for {work} isn't between 0 and 4");
    }
}

/// <summary>Customers the practice yard sends, wanting what a lesson needs (<c>Scenarios.Practice</c>).</summary>
public enum CustomerKind : byte
{
    /// <summary>As the trade wants them: hurt for a healer, chipped blades for a smith, and so on.</summary>
    Trade,

    /// <summary>Their blades whole but dull: a grindstone's work.</summary>
    Dull,

    /// <summary>Their blades chipped and bent: the forge's.</summary>
    Chipped,

    /// <summary>Thirsty from the road: a drink at the bar straight off.</summary>
    Thirsty,

    /// <summary>Here long enough to want work: one of them asks the innkeeper for a quest.</summary>
    Questing,
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

    /// <summary>
    /// The id by another name: a shared chapter (trades.json's "shared") is a def in a list, which
    /// the layered loader keys by name (so a mod can <c>$patch</c> it).
    /// </summary>
    public string Name { get => Id; set => Id = value; }

    public string Title { get; set; } = "";

    /// <summary>A line on what it teaches, shown before it's begun.</summary>
    public string Blurb { get; set; } = "";

    /// <summary>Customers sent when it begins, if any.</summary>
    public CustomerKind? Customers { get; set; }

    /// <summary>The workplace stocked again when it begins.</summary>
    public bool Restock { get; set; }

    /// <summary>The healer's: someone on a shrine pallet with this wound when it begins.</summary>
    public DamageType? Patient { get; set; }

    /// <summary>The healer's: the patient comes in with several wounds, or one festering under a dressing, instead of one plain one (<see cref="Patient"/>).</summary>
    public Scenarios.PatientCase? PatientCase { get; set; }

    /// <summary>The healer's: their mana spent when it begins, to be won back.</summary>
    public bool Spent { get; set; }

    /// <summary>The fight's: a training dummy and a sparring partner set out near the player, whose weapon goes in their pack (<c>Scenarios.Sparring</c>).</summary>
    public bool Sparring { get; set; }

    /// <summary>Put in the player's pack when it begins: what the work needs that the workplace doesn't keep.</summary>
    public List<ThingCount> Supply { get; set; } = [];

    /// <summary>Its steps in the usual order; a step can come round again (a blade still chipped goes back to the fire).</summary>
    public List<LessonStepDef> Steps { get; set; } = [];

    /// <summary>Part of the trade's main tutorial run, in the order listed; the rest are extra chapters.</summary>
    public bool Core { get; set; }

    /// <summary>"Worth knowing": lines shown once it's done, on what the chapter touched but didn't walk through.</summary>
    public List<string> Notes { get; set; } = [];

    /// <summary>The step's index by id, -1 if this lesson doesn't list it.</summary>
    public int IndexOf(string step) => Steps.FindIndex(s => s.Id == step);

    internal void Resolve(DefDatabase db, string owner)
    {
        foreach (var item in Supply)
            item.ThingDef = db.Thing(item.Def);
        if (Steps.Count == 0 || Steps.Select(s => s.Id).Distinct().Count() != Steps.Count)
            throw new Exception($"{owner}: lesson '{Id}' needs steps, each with its own id");
    }
}

/// <summary>A step of a lesson: an id the code returns, and what the player is told to do (key glyphs as in tips).</summary>
public sealed class LessonStepDef
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";

    /// <summary>Only comes up when something's gone a way it needn't (the forge gone out): listed only while it's the step at hand.</summary>
    public bool Aside { get; set; }

    /// <summary>
    /// The on-screen control the step is done with (a game action's name: "Orders", "Pack", "Gift"),
    /// glowed while it's the step at hand. The game reads it; the sim never does.
    /// </summary>
    public string? Ui { get; set; }

    /// <summary>A fuller hint, shown when the player's been on the step a while.</summary>
    public string? More { get; set; }
}

/// <summary>Something a trade makes itself to keep in, as a standing bill would (<see cref="TradeDef.Keeps"/>).</summary>
public sealed class TradeKeep
{
    public string Recipe { get; set; } = "";

    /// <summary>Fewer of its product than this at the workplace, or free about the town, and it's made.</summary>
    public int Below { get; set; } = 10;

    [JsonIgnore] public RecipeDef RecipeDef { get; set; } = null!;
}

/// <summary>Something a trade's workplace wants kept in, and what it'll pay a labourer to fetch it (<see cref="TradeDef.Wants"/>).</summary>
public sealed class TradeWant
{
    public string Thing { get; set; } = "";

    /// <summary>Fewer than this about the workplace and an errand goes up.</summary>
    public int Below { get; set; } = 4;

    /// <summary>How many the errand asks for.</summary>
    public int Count { get; set; } = 8;

    /// <summary>Silver for fetching them.</summary>
    public int Price { get; set; } = 5;

    /// <summary>How long the errand's left up, game hours.</summary>
    public float Hours { get; set; } = 8;

    [JsonIgnore] public ThingDef ThingDef { get; set; } = null!;
}

/// <summary>
/// Who a trade suits (<see cref="TradeDef.Level"/>): how much there is to learn before it goes well, and how much
/// it has going at once. Gathering trades are the calm ones to start with; the inn, the busiest, is for old hands.
/// </summary>
public enum TradeLevel : byte
{
    /// <summary>Calm work at its own pace: no recipes to learn first, nobody kept waiting, nothing lost to a slip.</summary>
    Beginner,

    /// <summary>Know-how to learn (recipes, materials, a craft's steps), and customers now and then.</summary>
    Intermediate,

    /// <summary>Customers all day and several things on the go at once.</summary>
    Experienced,
}

/// <summary>The word for a <see cref="TradeLevel"/>, shown beside a trade's name in the menus.</summary>
public static class TradeLevels
{
    public static string Label(TradeLevel level) => level switch
    {
        TradeLevel.Beginner => "Beginner",
        TradeLevel.Intermediate => "Intermediate",
        _ => "Experienced",
    };
}
