using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportingCast.Sim.Defs;

/// <summary>
/// All loaded defs. Each JSON file under the defs directory is an object with any of the keys
/// "blocks", "things", "creatures", "needs", "bodies", "materials", "difficulties", "raiderKinds", "wildlife", "thoughts", "interactions", "traits", "recipes", "tiers", "veins",
/// "dragonInteractions", "trustLevels", "tricks", "outfits", "elements", "buildings", "kits",
/// each an array of defs. Files are merged in name
/// order so later files (mods) can add content.
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
    private readonly Dictionary<string, ThoughtDef> _thoughts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, InteractionDef> _interactions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TraitDef> _traits = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RecipeDef> _recipes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DialogueDef> _dialogues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TowerDef> _towers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TierDef> _tiers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ElementDef> _elements = new(StringComparer.OrdinalIgnoreCase);
    private ElementDef[] _byElement = [];
    private readonly Dictionary<string, VeinDef> _veins = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DragonInteractionDef> _dragonInteractions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TrustLevelDef> _trustLevels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TrickDef> _tricks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OutfitDef> _outfits = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BuildingDef> _buildings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BuildingKitDef> _kits = new(StringComparer.OrdinalIgnoreCase);

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
    public List<TraitDef> AllTraits { get; } = [];
    public List<RecipeDef> AllRecipes { get; } = [];
    public List<DialogueDef> AllDialogues { get; } = [];
    public List<TowerDef> AllTowers { get; } = [];
    public List<TierDef> AllTiers { get; } = [];
    public List<VeinDef> AllVeins { get; } = [];
    public List<DragonInteractionDef> AllDragonInteractions { get; } = [];
    public List<ElementDef> AllElements { get; } = [];

    /// <summary>Trust levels, lowest first (sorted by <see cref="TrustLevelDef.MinAffinity"/> at load).</summary>
    public List<TrustLevelDef> TrustLevels { get; private set; } = [];
    public List<TrustLevelDef> AllTrustLevels { get; } = [];
    public List<TrickDef> AllTricks { get; } = [];
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
        public List<ThoughtDef> Thoughts { get; set; } = [];
        public List<InteractionDef> Interactions { get; set; } = [];
        public List<TraitDef> Traits { get; set; } = [];
        public List<RecipeDef> Recipes { get; set; } = [];
        public List<DialogueDef> Dialogues { get; set; } = [];
        public List<TowerDef> Towers { get; set; } = [];
        public List<TierDef> Tiers { get; set; } = [];
        public List<VeinDef> Veins { get; set; } = [];
        public List<DragonInteractionDef> DragonInteractions { get; set; } = [];
        public List<TrustLevelDef> TrustLevels { get; set; } = [];
        public List<TrickDef> Tricks { get; set; } = [];
        public List<OutfitDef> Outfits { get; set; } = [];
        public List<ElementDef> Elements { get; set; } = [];
        public List<BuildingDef> Buildings { get; set; } = [];
        public List<BuildingKitDef> Kits { get; set; } = [];
    }

    public static DefDatabase LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Defs directory not found: {directory}");

        var db = new DefDatabase();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories).Order())
            db.AddJson(File.ReadAllText(file), file);
        db.ResolveAll();
        return db;
    }

    /// <summary>Build from JSON strings directly (tests).</summary>
    public static DefDatabase FromJson(params string[] json)
    {
        var db = new DefDatabase();
        for (var i = 0; i < json.Length; i++)
            db.AddJson(json[i], $"<json {i}>");
        db.ResolveAll();
        return db;
    }

    private void AddJson(string json, string source)
    {
        DefFile file;
        try
        {
            file = JsonSerializer.Deserialize<DefFile>(json, JsonOptions) ?? new DefFile();
        }
        catch (JsonException e)
        {
            throw new Exception($"Failed to parse defs in {source}: {e.Message}", e);
        }

        AddAll(file.Blocks, _blocks, AllBlocks, source);
        AddAll(file.Things, _things, AllThings, source);
        AddAll(file.Creatures, _creatures, AllCreatures, source);
        AddAll(file.Needs, _needs, AllNeeds, source);
        AddAll(file.Bodies, _bodies, AllBodies, source);
        AddAll(file.Materials, _materials, AllMaterials, source);
        AddAll(file.Difficulties, _difficulties, AllDifficulties, source);
        AddAll(file.RaiderKinds, _raiderKinds, AllRaiderKinds, source);
        AddAll(file.Wildlife, _wildlife, AllWildlife, source);
        AddAll(file.Thoughts, _thoughts, AllThoughts, source);
        AddAll(file.Interactions, _interactions, AllInteractions, source);
        AddAll(file.Traits, _traits, AllTraits, source);
        AddAll(file.Recipes, _recipes, AllRecipes, source);
        AddAll(file.Dialogues, _dialogues, AllDialogues, source);
        AddAll(file.Towers, _towers, AllTowers, source);
        AddAll(file.Tiers, _tiers, AllTiers, source);
        AddAll(file.Veins, _veins, AllVeins, source);
        AddAll(file.DragonInteractions, _dragonInteractions, AllDragonInteractions, source);
        AddAll(file.TrustLevels, _trustLevels, AllTrustLevels, source);
        AddAll(file.Tricks, _tricks, AllTricks, source);
        AddAll(file.Outfits, _outfits, AllOutfits, source);
        AddAll(file.Elements, _elements, AllElements, source);
        AddAll(file.Buildings, _buildings, AllBuildings, source);
        AddAll(file.Kits, _kits, AllKits, source);
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
        foreach (var d in AllTraits) d.Resolve(this);
        foreach (var d in AllRecipes) d.Resolve(this); // after things: stations collect their recipes
        foreach (var d in AllDialogues) d.Resolve(this);
        foreach (var d in AllTowers) d.Resolve(this);
        foreach (var d in AllDragonInteractions) d.Resolve(this);
        foreach (var d in AllOutfits) d.Resolve(this);
        foreach (var d in AllElements) d.Resolve(this);
        foreach (var d in AllBuildings) d.Resolve(this); // after things and thoughts: their parts, furniture, feasts
        GeneratePhaseDefs();
        // Every element a dragon can have needs its def (one each).
        var elements = Enum.GetValues<Components.DragonElement>();
        _byElement = new ElementDef[elements.Length];
        foreach (var d in AllElements)
            _byElement[(int)d.Element] = d;
        if (AllElements.Count > 0 && Array.FindIndex(_byElement, d => d == null) is var missing and >= 0)
            throw new Exception($"No element def for {elements[missing]}");
        TrustLevels = AllTrustLevels.OrderBy(t => t.MinAffinity).ToList();
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
            var plot = plan.Count(ch => ch != ' ');
            for (var phase = BuildPhase.Foundation; phase < BuildPhase.Furnish; phase++)
            {
                List<(ThingDef Part, int Count)> laid = phase switch
                {
                    BuildPhase.Foundation when parts.FoundationDef is { } f => [(f, plot)],
                    BuildPhase.Roof when parts.RoofDef is { } r => [(r, plot)],
                    BuildPhase.Walls => "WDP".Select(ch => (Part: parts.ForSymbol(ch)!, Count: plan.Count(c => c == ch)))
                        .Where(x => x.Count > 0).GroupBy(x => x.Part).Select(g => (g.Key, g.Sum(x => x.Count))).ToList(),
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
                var name = phase.ToString().ToLowerInvariant();
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
    public DragonInteractionDef DragonInteraction(string name) => Lookup(_dragonInteractions, name);

    /// <summary>What a dragon of <paramref name="element"/> breathes (data/defs/dragons.json "elements").</summary>
    public ElementDef Element(Components.DragonElement element) =>
        (uint)element < (uint)_byElement.Length && _byElement[(int)element] is { } d ? d : throw new KeyNotFoundException($"No element def for {element}");
    public bool TryDragonInteraction(string name, out DragonInteractionDef def) => _dragonInteractions.TryGetValue(name, out def!);
    public TrickDef Trick(string name) => Lookup(_tricks, name);

    public BlockDef Block(string name) => Lookup(_blocks, name);
    public ThingDef Thing(string name) => Lookup(_things, name);
    public CreatureDef Creature(string name) => Lookup(_creatures, name);
    public NeedDef Need(string name) => Lookup(_needs, name);
    public BodyDef Body(string name) => Lookup(_bodies, name);
    public MaterialDef Material(string name) => Lookup(_materials, name);
    public DifficultyDef Difficulty(string name) => Lookup(_difficulties, name);
    public RaiderKindDef RaiderKind(string name) => Lookup(_raiderKinds, name);
    public ThoughtDef Thought(string name) => Lookup(_thoughts, name);
    public InteractionDef Interaction(string name) => Lookup(_interactions, name);
    public TraitDef Trait(string name) => Lookup(_traits, name);
    public RecipeDef Recipe(string name) => Lookup(_recipes, name);
    public DialogueDef Dialogue(string name) => Lookup(_dialogues, name);
    public TowerDef Tower(string name) => Lookup(_towers, name);
    public OutfitDef Outfit(string name) => Lookup(_outfits, name);
    public BuildingDef Building(string name) => Lookup(_buildings, name);
    public BuildingKitDef Kit(string name) => Lookup(_kits, name);
    public bool TryMaterial(string name, out MaterialDef def) => _materials.TryGetValue(name, out def!);
    public bool TryTrait(string name, out TraitDef def) => _traits.TryGetValue(name, out def!);

    public bool TryThing(string name, out ThingDef def) => _things.TryGetValue(name, out def!);

    public bool TryBlock(string name, out BlockDef def) => _blocks.TryGetValue(name, out def!);

    private static T Lookup<T>(Dictionary<string, T> map, string name)
        where T : Def =>
        map.TryGetValue(name, out var def)
            ? def
            : throw new Exception($"Unknown {typeof(T).Name} '{name}'");
}
