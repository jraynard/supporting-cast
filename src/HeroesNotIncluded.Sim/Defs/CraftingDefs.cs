using System.Text.Json.Serialization;
using HeroesNotIncluded.Sim.Components;

namespace HeroesNotIncluded.Sim.Defs;

/// <summary>
/// One thing that can be made: ingredients in, products out, after some skilled work. Made at a
/// <see cref="Station"/> (a building with <see cref="WorkbenchProps"/>), or by hand from the pack
/// when it has none. Villagers make recipes through bills on the station; the player picks one
/// at the station and holds the work key. Recipes live in <c>data/defs/workshop.json</c>.
/// </summary>
public sealed class RecipeDef : Def
{
    /// <summary>The building it's made at, or empty for a hand recipe (made anywhere, from the pack).</summary>
    public string Station { get; set; } = "";

    /// <summary>What it uses up. A material on an entry asks for that one; otherwise any kind of the item will do.</summary>
    public List<ThingCount> Ingredients { get; set; } = [];

    /// <summary>
    /// What it makes. Products that come in materials and have none set take the main material of
    /// the ingredients (oak wood makes oak planks), see <see cref="ThingDef.MaterialsOf"/>.
    /// </summary>
    public List<ThingCount> Products { get; set; } = [];

    /// <summary>Work in work-ticks (1 per tick of labor at average skill).</summary>
    public float Work { get; set; } = 300;

    /// <summary>The skill it trains and is sped up by; Cooking recipes are Cook work, the rest Craft.</summary>
    public Skill Skill { get; set; } = Skill.Crafting;

    /// <summary>Below this level of <see cref="Skill"/> nobody will (or can) make it.</summary>
    public int MinSkill { get; set; }

    /// <summary>The station's cell must be at least this hot (°C) while it's worked; 0 = no need (kilns, forges).</summary>
    public float MinTemp { get; set; }

    /// <summary>
    /// Tool kinds it can't be made without (<see cref="ToolProps.Kind"/>): on the crafter, or lying
    /// (or kept in a chest) within <c>Crafting.ToolReach</c> of the station. They also speed it up.
    /// </summary>
    public List<string> Tools { get; set; } = [];

    /// <summary>The tier (<see cref="ToolProps.Tier"/>) each of <see cref="Tools"/> must at least be: finer work wants better tools.</summary>
    public TierRef ToolTier { get; set; }

    /// <summary>Tool kinds that make it go faster when they're to hand, but aren't needed.</summary>
    public List<string> HelpedBy { get; set; } = [];

    /// <summary>How it gets into the recipe book; null means everyone knows it from the start.</summary>
    public RecipeUnlock? Unlock { get; set; }

    /// <summary>
    /// Smelted as a bloom: a player in a round runs the ore down in the fire and beats the slag out
    /// on the anvil (<c>Work.Smithing.BeginBloom</c>), and how well decides how many bars come of it.
    /// </summary>
    public bool Bloom { get; set; }

    [JsonIgnore] public ThingDef? StationDef { get; private set; }

    /// <summary>
    /// Only at a station fitted with this preset-building upgrade (<see cref="BuildingUpgrade"/>,
    /// by name: a stew needs the hearth's cooking pot). Null: any station of its kind.
    /// </summary>
    public string? Upgrade { get; set; }

    /// <summary>The kind of work bills for it are.</summary>
    [JsonIgnore] public WorkType WorkType => Skill == Skill.Cooking ? WorkType.Cook : WorkType.Craft;

    [JsonIgnore] public bool IsHandRecipe => StationDef == null;

    internal override void Resolve(DefDatabase db)
    {
        ToolTier = db.ResolveTier(ToolTier, $"Recipe '{Name}'");
        if (!string.IsNullOrEmpty(Station))
        {
            StationDef = db.Thing(Station);
            if (StationDef.Building?.Workbench == null)
                throw new Exception($"Recipe '{Name}': {Station} isn't a workbench (it needs \"workbench\" in its building props)");
            StationDef.Recipes.Add(this);
            // Its other shapes work it too: a building's hearth cooks what a campfire does.
            foreach (var shape in db.AllThings)
                if (shape.ShapeOfDef == StationDef && shape.Building?.Workbench != null)
                    shape.Recipes.Add(this);
        }
        if (Ingredients.Count == 0 || Products.Count == 0)
            throw new Exception($"Recipe '{Name}' needs ingredients and products");
        foreach (var c in Ingredients.Concat(Products))
        {
            c.Resolve(db);
            if (c.ThingDef.Category != ThingCategory.Item)
                throw new Exception($"Recipe '{Name}': {c.Def} isn't an item");
            if (c.Count <= 0)
                throw new Exception($"Recipe '{Name}': {c.Def} needs a positive count");
        }
        if (Work <= 0)
            throw new Exception($"Recipe '{Name}' needs some work");
        if (Unlock is { } u && u.Level > 0 == !string.IsNullOrEmpty(u.Discover))
            throw new Exception($"Recipe '{Name}': unlock by a skill level or by a discovery, one of them");
    }
}

/// <summary>
/// When a recipe becomes known: at a level of a skill (<see cref="Level"/>, for <see cref="Skill"/>,
/// defaulting to the recipe's own), or on a discovery, a trigger string like <c>handle:IronOre</c>
/// or <c>built:Kiln</c> (see <c>Systems/RecipeBooks.cs</c>).
/// </summary>
public sealed class RecipeUnlock
{
    public Skill? Skill { get; set; }
    public int Level { get; set; }
    public string Discover { get; set; } = "";
}

/// <summary>
/// A building that recipes are made at: a sawhorse, a bench, a fire. Villagers work its bills
/// (<c>Components.Workbench</c>); what's been brought for the current one stays at the station
/// as counts, like a construction site's deliveries, so stopping halfway loses nothing.
/// </summary>
public sealed class WorkbenchProps
{
    /// <summary>Most bills it takes at once.</summary>
    public int Bills { get; set; } = 8;

    /// <summary>In a round, a player makes gear here by hand, as a mini-game (<see cref="Work.HammerSession"/>).</summary>
    public bool HandWork { get; set; }
}
