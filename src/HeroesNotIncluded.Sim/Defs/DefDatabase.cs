using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace HeroesNotIncluded.Sim.Defs;

/// <summary>
/// All loaded defs. Each JSON file under the defs directory is an object with any of the keys
/// "blocks", "things", "creatures", "needs", "bodies", "materials", "difficulties", "raiderKinds", "wildlife", "seasons", "thoughts", "interactions", "recipes", "tiers", "veins",
/// "dragonInteractions", "trustLevels", "tricks", "outfits", "elements", "buildings", "kits", "trades", "adventurerKinds", "parties", "services", "mendings", "ghostErrands", "quests",
/// each an array of defs (and "basics", the tutorial's first chapter: one lesson, the last loaded). Files are merged in name
/// order, directory by directory (the game's, then each mod's), so later files can add content, replace a def by
/// naming it again, or change only some of its fields with <c>"$patch": "merge"</c> (objects merge, anything else is
/// replaced) or drop it with <c>"$patch": "remove"</c> (docs/MODDING.md).
/// </summary>
public sealed class DefDatabase
{
    private readonly Dictionary<string, BlockDef> _blocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ThingDef> _things = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CreatureDef> _creatures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NeedDef> _needs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BodyDef> _bodies = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MaterialDef> _materials = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DifficultyDef> _difficulties = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RaiderKindDef> _raiderKinds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WildlifeDef> _wildlife = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SeasonDef> _seasons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IncidentDef> _incidents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FestivalDef> _festivals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ThoughtDef> _thoughts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, InteractionDef> _interactions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RecipeDef> _recipes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TierDef> _tiers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, VeinDef> _veins = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OutfitDef> _outfits = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BuildingDef> _buildings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BuildingKitDef> _kits = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TradeDef> _trades = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AdventurerKindDef> _adventurerKinds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PartyDef> _parties = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ServiceDef> _services = new(StringComparer.OrdinalIgnoreCase);
    private readonly ServiceDef?[] _byServiceKind = new ServiceDef?[Enum.GetValues<ServiceKind>().Length];

    private readonly Dictionary<string, MendingDef> _mendings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GhostErrandDef> _ghostErrands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BotTemperDef> _botTempers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BarkDef> _barks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuneDef> _runes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DrawFeelDef> _drawFeels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, QuestDef> _quests = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MapDef> _maps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Hand-made maps (data/defs/maps/), in def order.</summary>
    public List<MapDef> AllMaps { get; } = [];

    /// <summary>Where parties can be sent (data/defs/quests.json), in def order.</summary>
    public List<QuestDef> AllQuests { get; } = [];

    /// <summary>What an empath can cast (data/defs/healing.json), in def order.</summary>
    public List<MendingDef> AllMendings { get; } = [];
    public List<GhostErrandDef> AllGhostErrands { get; } = [];
    public List<BotTemperDef> AllBotTempers { get; } = [];
    public List<BarkDef> AllBarks { get; } = [];

    /// <summary>What mendings are woven from (data/defs/healing.json), in def order: keys 1 to 9.</summary>
    public List<RuneDef> AllRunes { get; } = [];

    /// <summary>How drawing each kind of wound feels (data/defs/healing.json).</summary>
    public List<DrawFeelDef> AllDrawFeels { get; } = [];

    /// <summary>How each kind of order goes (data/defs/orders.json).</summary>
    public List<ServiceDef> AllServices { get; } = [];

    /// <summary>Who adventuring parties are made of (data/defs/adventurers.json).</summary>
    public List<AdventurerKindDef> AllAdventurerKinds { get; } = [];

    /// <summary>Parties that can come to town during a round, in def order.</summary>
    public List<PartyDef> AllParties { get; } = [];

    /// <summary>Trades a round deals out (data/defs/trades.json), in def order.</summary>
    public List<TradeDef> AllTrades { get; } = [];

    public List<BlockDef> AllBlocks { get; } = [];
    public List<ThingDef> AllThings { get; } = [];
    public List<CreatureDef> AllCreatures { get; } = [];
    public List<NeedDef> AllNeeds { get; } = [];
    public List<BodyDef> AllBodies { get; } = [];
    public List<MaterialDef> AllMaterials { get; } = [];
    public List<DifficultyDef> AllDifficulties { get; } = [];
    public List<RaiderKindDef> AllRaiderKinds { get; } = [];
    public List<WildlifeDef> AllWildlife { get; } = [];
    public List<ThoughtDef> AllThoughts { get; } = [];
    public List<InteractionDef> AllInteractions { get; } = [];
    public List<RecipeDef> AllRecipes { get; } = [];
    public List<TierDef> AllTiers { get; } = [];
    public List<VeinDef> AllVeins { get; } = [];

    /// <summary>Trust levels, lowest first (sorted by <see cref="TrustLevelDef.MinAffinity"/> at load).</summary>
    public List<OutfitDef> AllOutfits { get; } = [];

    /// <summary>Preset buildings (data/defs/buildings.json), in def order: the build bar's order.</summary>
    public List<BuildingDef> AllBuildings { get; } = [];

    /// <summary>How preset buildings are drawn (presentation only).</summary>
    public List<BuildingKitDef> AllKits { get; } = [];

    /// <summary>Every recipe discovery trigger in use ("built:Kiln"), so a check for one that nothing uses is cheap.</summary>
    public HashSet<string> DiscoveryTriggers { get; } = new(StringComparer.Ordinal);

    /// <summary>Items whose handling teaches a recipe, with their trigger ("handle:IronOre").</summary>
    public Dictionary<ThingDef, string> HandleTriggers { get; } = [];

    /// <summary>The memory thoughts code adds by name (null where the defs lack one).</summary>
    public KnownThoughts Thoughts { get; private set; } = new();

    /// <summary>The wildlife settings in effect: the last one loaded (a mod can add or override one).</summary>
    public WildlifeDef Wildlife => AllWildlife.Count > 0 ? AllWildlife[^1] : DefaultWildlife;

    private static readonly WildlifeDef DefaultWildlife = new() { Name = "Default" };

    /// <summary>A round's season (data/defs/season.json): the last one loaded.</summary>
    public List<SeasonDef> AllSeasons { get; } = [];

    /// <summary>The season in effect: the last one loaded, or the built-in default.</summary>
    public SeasonDef Season => AllSeasons.Count > 0 ? AllSeasons[^1] : DefaultSeason;

    private static readonly SeasonDef DefaultSeason = new() { Name = "Default" };

    /// <summary>The Director's small happenings between crises (data/defs/incidents.json).</summary>
    public List<IncidentDef> AllIncidents { get; } = [];

    /// <summary>How the season's festival runs (data/defs/festival.json): the last one loaded.</summary>
    public List<FestivalDef> AllFestivals { get; } = [];

    /// <summary>The festival in effect: the last one loaded, or the built-in default.</summary>
    public FestivalDef Festival => AllFestivals.Count > 0 ? AllFestivals[^1] : DefaultFestival;

    private static readonly FestivalDef DefaultFestival = new() { Name = "Default" };

    /// <summary>
    /// The tutorial's first chapter, the same for every trade (trades.json's "basics"): moving,
    /// the pack, the orders and the controls card. The last one loaded; one step if none is.
    /// </summary>
    public LessonDef Basics { get; private set; } = new() { Id = "basics", Title = "The basics", Steps = [new() { Id = "move", Text = "Walk with ~WASD~." }] };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed class DefFile
    {
        public List<BlockDef> Blocks { get; set; } = [];
        public List<ThingDef> Things { get; set; } = [];
        public List<CreatureDef> Creatures { get; set; } = [];
        public List<NeedDef> Needs { get; set; } = [];
        public List<BodyDef> Bodies { get; set; } = [];
        public List<MaterialDef> Materials { get; set; } = [];
        public List<DifficultyDef> Difficulties { get; set; } = [];
        public List<RaiderKindDef> RaiderKinds { get; set; } = [];
        public List<WildlifeDef> Wildlife { get; set; } = [];
        public List<SeasonDef> Seasons { get; set; } = [];
        public List<IncidentDef> Incidents { get; set; } = [];
        public List<FestivalDef> Festivals { get; set; } = [];
        public List<ThoughtDef> Thoughts { get; set; } = [];
        public List<InteractionDef> Interactions { get; set; } = [];
        public List<RecipeDef> Recipes { get; set; } = [];
        public List<TierDef> Tiers { get; set; } = [];
        public List<VeinDef> Veins { get; set; } = [];
        public List<OutfitDef> Outfits { get; set; } = [];
        public List<BuildingDef> Buildings { get; set; } = [];
        public List<BuildingKitDef> Kits { get; set; } = [];
        public List<TradeDef> Trades { get; set; } = [];
        public List<AdventurerKindDef> AdventurerKinds { get; set; } = [];
        public List<PartyDef> Parties { get; set; } = [];
        public List<ServiceDef> Services { get; set; } = [];
        public List<MendingDef> Mendings { get; set; } = [];
        public List<GhostErrandDef> GhostErrands { get; set; } = [];
        public List<BotTemperDef> BotTempers { get; set; } = [];
        public List<BarkDef> Barks { get; set; } = [];
        public List<RuneDef> Runes { get; set; } = [];
        public List<DrawFeelDef> DrawFeels { get; set; } = [];
        public List<QuestDef> Quests { get; set; } = [];
        public List<MapDef> Maps { get; set; } = [];
        public LessonDef? Basics { get; set; }
    }

    public static DefDatabase LoadDirectory(string directory) => LoadDirectories([directory]);

    /// <summary>
    /// Every <c>*.json</c> under each directory, in order: the first is the game's own (it must exist), the
    /// rest are mods' and are layered over it.
    /// </summary>
    public static DefDatabase LoadDirectories(IReadOnlyList<string> directories)
    {
        if (directories.Count == 0 || !Directory.Exists(directories[0]))
            throw new DirectoryNotFoundException($"Defs directory not found: {(directories.Count == 0 ? "(none)" : directories[0])}");

        var layers = new DefLayers();
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException($"Defs directory not found: {directory}");
            foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories).Order())
                layers.Add(File.ReadAllText(file), file);
        }
        return layers.Build();
    }

    /// <summary>Build from JSON strings directly (tests).</summary>
    public static DefDatabase FromJson(params string[] json)
    {
        var layers = new DefLayers();
        for (var i = 0; i < json.Length; i++)
            layers.Add(json[i], $"<json {i}>");
        return layers.Build();
    }

    /// <summary>
    /// Files as raw JSON, def by def, so a later one can merge into or remove an earlier def before
    /// anything is typed. A def keeps the place it first had, so indices stay put when one is replaced.
    /// </summary>
    private sealed class DefLayers
    {
        private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };
        private static readonly JsonDocumentOptions DocOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        private sealed class Kind
        {
            public required string Key;
            public readonly List<string> Order = [];
            public readonly Dictionary<string, JsonObject> ByName = new(StringComparer.OrdinalIgnoreCase);
        }

        private readonly List<Kind> _kinds = [];
        private readonly Dictionary<string, Kind> _byKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, JsonNode?> _singles = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string json, string source)
        {
            JsonObject root;
            try
            {
                root = JsonNode.Parse(json, NodeOptions, DocOptions) as JsonObject ?? throw new JsonException("expected an object at the top");
            }
            catch (JsonException e)
            {
                throw new Exception($"Failed to parse defs in {source}: {e.Message}", e);
            }
            foreach (var (key, value) in root.ToList())
            {
                if (value is not JsonArray array)
                {
                    _singles[key] = value?.DeepClone(); // "basics": the last one loaded
                    continue;
                }
                if (!_byKey.TryGetValue(key, out var kind))
                {
                    kind = new Kind { Key = key };
                    _byKey[key] = kind;
                    _kinds.Add(kind);
                }
                foreach (var item in array)
                    AddDef(kind, item as JsonObject ?? throw new Exception($"Failed to parse defs in {source}: \"{key}\" should hold objects"), source);
            }
        }

        private static void AddDef(Kind kind, JsonObject def, string source)
        {
            def = (JsonObject)def.DeepClone();
            var name = Text(def, "name");
            if (string.IsNullOrWhiteSpace(name))
                throw new Exception($"A def in \"{kind.Key}\" without a name in {source}");
            var patch = Text(def, "$patch");
            Remove(def, "$patch");
            switch (patch)
            {
                case null:
                    if (!kind.ByName.ContainsKey(name))
                        kind.Order.Add(name);
                    kind.ByName[name] = def;
                    break;
                case "merge":
                    if (!kind.ByName.TryGetValue(name, out var into))
                        throw new Exception($"{source} merges into \"{name}\" in \"{kind.Key}\", which no earlier file defines");
                    Merge(into, def);
                    break;
                case "remove":
                    if (!kind.ByName.Remove(name))
                        throw new Exception($"{source} removes \"{name}\" from \"{kind.Key}\", which no earlier file defines");
                    kind.Order.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                    break;
                default:
                    throw new Exception($"{source}: \"$patch\" on \"{name}\" should be \"merge\" or \"remove\", not \"{patch}\"");
            }
        }

        /// <summary>Objects merge field by field (names ignoring case); anything else, arrays included, is replaced.</summary>
        private static void Merge(JsonObject into, JsonObject from)
        {
            foreach (var (key, value) in from.ToList())
            {
                var existing = into.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
                if (existing.Key != null && existing.Value is JsonObject a && value is JsonObject b)
                {
                    Merge(a, b);
                    continue;
                }
                if (existing.Key != null)
                    into.Remove(existing.Key);
                into[key] = value?.DeepClone();
            }
        }

        private static string? Text(JsonObject o, string key)
        {
            foreach (var (k, v) in o)
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                    return v is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : null;
            return null;
        }

        private static void Remove(JsonObject o, string key)
        {
            foreach (var k in o.Select(p => p.Key).Where(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)).ToList())
                o.Remove(k);
        }

        public DefDatabase Build()
        {
            var merged = new JsonObject(NodeOptions);
            foreach (var kind in _kinds)
                merged[kind.Key] = new JsonArray([.. kind.Order.Select(n => (JsonNode)kind.ByName[n])]);
            foreach (var (key, value) in _singles)
                merged[key] = value;
            DefFile file;
            try
            {
                file = merged.Deserialize<DefFile>(JsonOptions) ?? new DefFile();
            }
            catch (JsonException e)
            {
                throw new Exception($"Failed to read defs: {e.Message}", e);
            }
            var db = new DefDatabase();
            // Kinds the game doesn't know are a mod's own ("instruments"): kept, merged and patched like the
            // game's, and read typed through ModDefs.
            var known = typeof(DefFile).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var kind in _kinds.Where(k => !known.Contains(k.Key)))
                db._modKinds[kind.Key] = [.. kind.Order.Select(n => kind.ByName[n])];
            db.AddFile(file, "defs");
            db.ResolveAll();
            return db;
        }
    }

    private void AddFile(DefFile file, string source)
    {
        AddAll(file.Blocks, _blocks, AllBlocks, source);
        AddAll(file.Things, _things, AllThings, source);
        AddAll(file.Creatures, _creatures, AllCreatures, source);
        AddAll(file.Needs, _needs, AllNeeds, source);
        AddAll(file.Bodies, _bodies, AllBodies, source);
        AddAll(file.Materials, _materials, AllMaterials, source);
        AddAll(file.Difficulties, _difficulties, AllDifficulties, source);
        AddAll(file.RaiderKinds, _raiderKinds, AllRaiderKinds, source);
        AddAll(file.Wildlife, _wildlife, AllWildlife, source);
        AddAll(file.Seasons, _seasons, AllSeasons, source);
        AddAll(file.Incidents, _incidents, AllIncidents, source);
        AddAll(file.Festivals, _festivals, AllFestivals, source);
        AddAll(file.Thoughts, _thoughts, AllThoughts, source);
        AddAll(file.Interactions, _interactions, AllInteractions, source);
        AddAll(file.Recipes, _recipes, AllRecipes, source);
        AddAll(file.Tiers, _tiers, AllTiers, source);
        AddAll(file.Veins, _veins, AllVeins, source);
        AddAll(file.Outfits, _outfits, AllOutfits, source);
        AddAll(file.Buildings, _buildings, AllBuildings, source);
        AddAll(file.Kits, _kits, AllKits, source);
        AddAll(file.Trades, _trades, AllTrades, source);
        AddAll(file.AdventurerKinds, _adventurerKinds, AllAdventurerKinds, source);
        AddAll(file.Parties, _parties, AllParties, source);
        AddAll(file.Services, _services, AllServices, source);
        AddAll(file.Mendings, _mendings, AllMendings, source);
        AddAll(file.GhostErrands, _ghostErrands, AllGhostErrands, source);
        AddAll(file.BotTempers, _botTempers, AllBotTempers, source);
        AddAll(file.Barks, _barks, AllBarks, source);
        AddAll(file.Runes, _runes, AllRunes, source);
        AddAll(file.DrawFeels, _drawFeels, AllDrawFeels, source);
        AddAll(file.Quests, _quests, AllQuests, source);
        AddAll(file.Maps, _maps, AllMaps, source);
        if (file.Basics != null)
            Basics = file.Basics;
    }

    private static void AddAll<T>(List<T> defs, Dictionary<string, T> map, List<T> all, string source)
        where T : Def
    {
        foreach (var def in defs)
        {
            if (string.IsNullOrWhiteSpace(def.Name))
                throw new Exception($"{typeof(T).Name} without a name in {source}");
            if (string.IsNullOrEmpty(def.Label))
                def.Label = def.Name;
            if (map.TryGetValue(def.Name, out var existing))
            {
                // Later files override earlier ones in place, keeping the index stable.
                def.Index = existing.Index;
                all[def.Index] = def;
            }
            else
            {
                def.Index = all.Count;
                all.Add(def);
            }
            map[def.Name] = def;
        }
    }

    private void ResolveAll()
    {
        // Tiers first: blocks, tools and recipes name them.
        foreach (var d in AllTiers) d.Resolve(this);
        foreach (var d in AllMaterials) d.Resolve(this);
        foreach (var d in AllBlocks) d.Resolve(this);
        foreach (var d in AllVeins) d.Resolve(this);
        foreach (var d in AllNeeds) d.Resolve(this);
        foreach (var d in AllBodies) d.Resolve(this);
        foreach (var d in AllThings) d.Resolve(this);
        foreach (var d in AllCreatures) d.Resolve(this);
        foreach (var d in AllWildlife) d.Resolve(this);
        foreach (var d in AllRaiderKinds) d.Resolve(this);
        foreach (var d in AllThoughts) d.Resolve(this);
        foreach (var d in AllInteractions) d.Resolve(this);
        foreach (var d in AllRecipes) d.Resolve(this); // after things: stations collect their recipes
        foreach (var d in AllOutfits) d.Resolve(this);
        foreach (var d in AllKits) d.Resolve(this);
        foreach (var d in AllBuildings) d.Resolve(this); // after things and thoughts: their parts, furniture, feasts
        foreach (var d in AllTrades) d.Resolve(this); // after buildings: their workplaces
        Basics.Resolve(this, "Basics");
        foreach (var d in AllAdventurerKinds) d.Resolve(this);
        foreach (var d in AllParties) d.Resolve(this); // after adventurer kinds and buildings
        if (AllRunes.Count > 9)
            throw new Exception("At most 9 runes: they're keyed 1 to 9");
        foreach (var d in AllMendings) d.Resolve(this);
        foreach (var d in AllGhostErrands) d.Resolve(this);
        foreach (var d in AllBotTempers) d.Resolve(this);
        foreach (var d in AllBarks) d.Resolve(this);
        Healing.Weaving.Validate(this);
        foreach (var d in AllDrawFeels) d.Resolve(this);
        foreach (var d in AllQuests) d.Resolve(this);
        foreach (var d in AllMaps) d.Resolve(this); // after blocks, things, creatures and buildings
        foreach (var d in AllSeasons) d.Resolve(this);
        foreach (var d in AllIncidents) d.Resolve(this);
        foreach (var d in AllFestivals) d.Resolve(this);
        foreach (var d in AllServices)
        {
            d.Resolve(this);
            _byServiceKind[(int)d.Kind] = d;
        }
        GeneratePhaseDefs();
        // Every element a dragon can have needs its def (one each).
        Thoughts = KnownThoughts.Resolve(_thoughts);
        foreach (var r in AllRecipes)
        {
            if (r.Unlock is not { Discover.Length: > 0 } u)
                continue;
            var colon = u.Discover.IndexOf(':');
            var kind = colon < 0 ? u.Discover : u.Discover[..colon];
            var what = colon < 0 ? "" : u.Discover[(colon + 1)..];
            if (kind is not ("handle" or "built"))
                throw new Exception($"Recipe '{r.Name}': unknown discovery '{u.Discover}' (handle:<item> or built:<building>)");
            var def = Thing(what);
            DiscoveryTriggers.Add(u.Discover);
            if (kind == "handle")
                HandleTriggers[def] = u.Discover;
        }
    }

    /// <summary>
    /// The sites preset buildings' phases are built as (<see cref="BuildingVariant.PhaseDefs"/>):
    /// one def per building, look and raised phase, costing and taking the work of all the parts
    /// it lays. Named "Dwelling_Turf_Walls" (saves find them by name) and added after every loaded
    /// thing, so they never move a loaded def's index.
    /// </summary>
    private void GeneratePhaseDefs()
    {
        foreach (var b in AllBuildings)
        foreach (var v in b.Variants)
        {
            var parts = v.Resolved;
            var plan = string.Concat(b.Layout);
            var upper = string.Concat(b.UpperLayout);
            var plot = plan.Count(ch => ch != ' ');
            for (var phase = BuildPhase.Foundation; phase < BuildPhase.Furnish; phase++)
            {
                List<(ThingDef Part, int Count)> laid = phase switch
                {
                    BuildPhase.Foundation when parts.FoundationDef is { } f => [(f, plot)],
                    BuildPhase.Roof when parts.RoofDef is { } r => [(r, plot)],
                    BuildPhase.Walls => Parts(plan, "WDPOS"),
                    BuildPhase.UpperFloor when parts.UpperFloorDef is { } f && upper.Length > 0 => [(f, upper.Count(ch => ch != ' ') - plan.Count(ch => ch == 'S'))],
                    BuildPhase.UpperWalls => Parts(upper, "WDPO"),
                    _ => [],
                };
                v.PhaseDefs[(int)phase] = null;
                if (laid.Count == 0)
                    continue;
                var cost = new List<ThingCount>();
                foreach (var (part, n) in laid)
                foreach (var c in part.Building!.Cost)
                {
                    var material = c.MaterialDef?.Name ?? "";
                    if (cost.Find(x => x.Def == c.ThingDef.Name && x.Material == material) is { } same)
                        same.Count += c.Count * n;
                    else
                        cost.Add(new ThingCount { Def = c.ThingDef.Name, Count = c.Count * n, Material = material });
                }
                var name = phase switch { BuildPhase.UpperFloor => "upper floor", BuildPhase.UpperWalls => "upper storey", _ => phase.ToString().ToLowerInvariant() };
                var def = new ThingDef
                {
                    Name = $"{b.Name}_{v.Name}_{phase}",
                    Label = $"{b.Label} {name}",
                    Description = $"The {b.Label}'s {name}, raised all at once: everyone building brings materials and works on it together.",
                    Category = ThingCategory.Building,
                    Sprite = laid[0].Part.Sprite,
                    Size = [b.Width, b.Depth],
                    Building = new BuildingProps { PartOnly = true, Work = laid.Sum(x => x.Part.Building!.Work * x.Count), Cost = cost },
                    PhaseOf = b,
                    Phase = phase,
                };
                AddAll([def], _things, AllThings, $"building '{b.Name}'");
                def.Resolve(this);
                v.PhaseDefs[(int)phase] = def;
            }

            List<(ThingDef, int)> Parts(string cells, string symbols) => symbols
                .Select(ch => (Part: parts.ForSymbol(ch)!, Count: cells.Count(c => c == ch)))
                .Where(x => x.Count > 0).GroupBy(x => x.Part).Select(g => (g.Key, g.Sum(x => x.Count))).ToList();
        }
        // Each upgrade's site: at the furnishing's cell, one cell, made of what the upgrade costs.
        foreach (var b in AllBuildings)
        foreach (var u in b.Upgrades)
        {
            var site = new ThingDef
            {
                Name = $"{b.Name}_Upgrade_{u.Name}",
                Label = string.IsNullOrEmpty(u.Label) ? u.Name : u.Label,
                Description = u.Description,
                Category = ThingCategory.Building,
                Sprite = u.OnDef.Sprite,
                Building = new BuildingProps
                {
                    PartOnly = true, Work = u.Work,
                    Cost = u.Cost.Select(c => new ThingCount { Def = c.Def, Count = c.Count, Material = c.Material }).ToList(),
                },
                UpgradeOf = u,
            };
            AddAll([site], _things, AllThings, $"building '{b.Name}'");
            site.Resolve(this);
            u.SiteDef = site;
            u.Building = b;
        }
        // A recipe that needs an upgrade belongs only to stations it can be fitted to: a campfire
        // shares the hearth's recipes, but never gets the pot a stew needs.
        var fitsOn = AllBuildings.SelectMany(b => b.Upgrades).ToLookup(u => u.Name, u => u.OnDef);
        foreach (var t in AllThings)
            t.Recipes.RemoveAll(r => r.Upgrade != null && !fitsOn[r.Upgrade].Contains(t));
    }

    /// <summary>A tier as written in a def, as its number: a name is looked up among the <see cref="TierDef"/>s.</summary>
    public TierRef ResolveTier(TierRef tier, string owner)
    {
        if (tier.Name == null)
            return tier;
        if (!_tiers.TryGetValue(tier.Name, out var def))
            throw new Exception($"{owner}: unknown tier '{tier.Name}'");
        return new TierRef { Value = def.Tier };
    }

    /// <summary>The tier def numbered <paramref name="tier"/>, or null if none is.</summary>
    public TierDef? TierAt(int tier)
    {
        foreach (var t in AllTiers)
            if (t.Tier == tier)
                return t;
        return null;
    }

    /// <summary>"bronze" for tier 3; "tier 3" when no def names it.</summary>
    public string TierLabel(int tier) => TierAt(tier)?.Label ?? $"tier {tier}";

    /// <summary>"a bronze pick", "an iron hammer": a tool of at least this tier.</summary>
    public string ToolOfTier(int tier, string kind)
    {
        var what = tier >= 1 ? $"{TierLabel(tier)} {kind.ToLowerInvariant()}" : kind.ToLowerInvariant();
        return ("aeiou".Contains(char.ToLowerInvariant(what[0])) ? "an " : "a ") + what;
    }

    public TierDef Tier(string name) => Lookup(_tiers, name);

    public BlockDef Block(string name) => Lookup(_blocks, name);
    public ThingDef Thing(string name) => Lookup(_things, name);
    public CreatureDef Creature(string name) => Lookup(_creatures, name);
    public NeedDef Need(string name) => Lookup(_needs, name);
    public BodyDef Body(string name) => Lookup(_bodies, name);
    public MaterialDef Material(string name) => Lookup(_materials, name);
    public DifficultyDef Difficulty(string name) => Lookup(_difficulties, name);
    public RaiderKindDef RaiderKind(string name) => Lookup(_raiderKinds, name);
    public AdventurerKindDef AdventurerKind(string name) => Lookup(_adventurerKinds, name);
    public PartyDef Party(string name) => Lookup(_parties, name);
    public ServiceDef Service(string name) => Lookup(_services, name);
    public MendingDef Mending(string name) => Lookup(_mendings, name);
    public RuneDef Rune(string name) => Lookup(_runes, name);

    /// <summary>What's said when this happens; null if nothing's written for it.</summary>
    public BarkDef? Bark(string name) => _barks.GetValueOrDefault(name);

    /// <summary>How drawing a wound of this kind feels; plain jolts for a kind with no def.</summary>
    public DrawFeelDef? DrawFeel(DamageType type) => _drawFeels.GetValueOrDefault(type.ToString());
    public QuestDef Quest(string name) => Lookup(_quests, name);
    public MapDef Map(string name) => Lookup(_maps, name);

    /// <summary>The def for a kind of service; every kind needs one.</summary>
    /// <summary>The service's def, or null when none is loaded (a trade's mod turned off).</summary>
    public ServiceDef? ServiceOrNull(ServiceKind kind) => _byServiceKind[(int)kind];

    public ServiceDef Service(ServiceKind kind) =>
        _byServiceKind[(int)kind] ?? throw new KeyNotFoundException($"No service def for {kind}");
    public ThoughtDef Thought(string name) => Lookup(_thoughts, name);
    public InteractionDef Interaction(string name) => Lookup(_interactions, name);
    public RecipeDef Recipe(string name) => Lookup(_recipes, name);
    public OutfitDef Outfit(string name) => Lookup(_outfits, name);
    public BuildingDef Building(string name) => Lookup(_buildings, name);

    public bool HasBuilding(string name) => _buildings.ContainsKey(name);

    private readonly Dictionary<string, List<JsonObject>> _modKinds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string, Type), object> _modDefs = [];

    /// <summary>
    /// A mod's own kind of def (a top-level key the game doesn't know, "instruments"), read as
    /// <typeparamref name="T"/>, in load order, once. Defs implementing <see cref="IModDef"/> resolve their
    /// references after all of the kind are read. Empty when no file has the kind (its mod is off).
    /// </summary>
    public IReadOnlyList<T> ModDefs<T>(string kind) where T : Def
    {
        if (_modDefs.TryGetValue((kind, typeof(T)), out var cached))
            return (List<T>)cached;
        var list = new List<T>();
        foreach (var raw in _modKinds.GetValueOrDefault(kind) ?? [])
        {
            T def;
            try
            {
                def = raw.Deserialize<T>(JsonOptions) ?? throw new JsonException("empty");
            }
            catch (JsonException e)
            {
                throw new Exception($"Failed to read a def in \"{kind}\": {e.Message}", e);
            }
            if (string.IsNullOrEmpty(def.Label))
                def.Label = def.Name;
            def.Index = list.Count;
            list.Add(def);
        }
        _modDefs[(kind, typeof(T))] = list;
        foreach (var def in list.OfType<IModDef>())
            def.Resolve(this);
        return list;
    }

    /// <summary>A mod's def of its own kind by name (see <see cref="ModDefs{T}"/>); null if there's none.</summary>
    public T? ModDef<T>(string kind, string name) where T : Def =>
        ModDefs<T>(kind).FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
    public BuildingKitDef Kit(string name) => Lookup(_kits, name);
    public TradeDef Trade(string name) =>
        TryTrade(name, out var def) ? def : throw new Exception($"Unknown TradeDef '{name}'");

    /// <summary>A trade by its name, or by one it was once called (<see cref="TradeDef.Formerly"/>), so old saves, replays and rosters still find it.</summary>
    public bool TryTrade(string name, out TradeDef def)
    {
        if (_trades.TryGetValue(name, out def!))
            return true;
        def = AllTrades.FirstOrDefault(t => t.Formerly.Contains(name, StringComparer.OrdinalIgnoreCase))!;
        return def != null;
    }
    public bool TryMaterial(string name, out MaterialDef def) => _materials.TryGetValue(name, out def!);

    public bool TryThing(string name, out ThingDef def) => _things.TryGetValue(name, out def!);

    public bool TryCreature(string name, out CreatureDef def) => _creatures.TryGetValue(name, out def!);

    public bool TryBlock(string name, out BlockDef def) => _blocks.TryGetValue(name, out def!);

    private static T Lookup<T>(Dictionary<string, T> map, string name)
        where T : Def =>
        map.TryGetValue(name, out var def)
            ? def
            : throw new Exception($"Unknown {typeof(T).Name} '{name}'");
}
