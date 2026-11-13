using System.Text.Json.Serialization;

namespace HeroesNotIncluded.Sim.Defs;

/// <summary>
/// Base for all data-driven definitions loaded from data/defs/*.json. Defs reference each
/// other by <see cref="Name"/>; references are resolved to objects in <see cref="Resolve"/>.
/// </summary>
public abstract class Def
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Dense index within its def type, assigned at load. Useful for arrays.</summary>
    [JsonIgnore]
    public int Index { get; internal set; }

    internal virtual void Resolve(DefDatabase db) { }

    public override string ToString() => Name;
}

/// <summary>
/// What fills a map cell: air, or a solid block (dirt, stone, water...). Creatures stand on top of
/// solid blocks, so the movement and fire properties describe the block's top surface.
/// </summary>
public sealed class BlockDef : Def
{
    /// <summary>Top face sprite.</summary>
    public string Sprite { get; set; } = "";

    /// <summary>Side face sprite; the top sprite if empty.</summary>
    public string SpriteSide { get; set; } = "";

    /// <summary>
    /// Presentation only: a top texture used in patches across this block (wildflowers in grass),
    /// where smooth noise rises above <see cref="AccentAbove"/>, frayed at the edges with
    /// <see cref="AccentFringe"/>. The sim never reads these.
    /// </summary>
    public string Accent { get; set; } = "";
    public string AccentFringe { get; set; } = "";
    public float AccentAbove { get; set; } = 0.7f;

    /// <summary>False only for air: the cell can hold creatures, structures and items.</summary>
    public bool Solid { get; set; } = true;

    /// <summary>Movement cost multiplier for walking on top. 0 means impassable.</summary>
    public float MoveCost { get; set; } = 1f;

    public bool IsWater { get; set; }
    public bool CanBuildOn { get; set; } = true;

    /// <summary>Fuel the ground itself offers to fire (dry grass burns, dirt doesn't).</summary>
    public float Flammability { get; set; }

    /// <summary>Block this becomes after its surface burns (grass → dirt).</summary>
    public string? BurnsInto { get; set; }

    [JsonIgnore] public BlockDef? BurnsIntoDef { get; private set; }

    /// <summary>Work-ticks to dig the block out. 0 means it can't be dug.</summary>
    public float DigWork { get; set; }

    /// <summary>Items left behind when the block is dug out.</summary>
    public List<ThingCount> DigYield { get; set; } = [];

    /// <summary>The tool kind digging it takes (rock wants a "Pick"): without one it goes at <c>Crafting.BareHands</c>. Null: any hands.</summary>
    public string? DigTool { get; set; }

    /// <summary>
    /// The lowest tool tier (<see cref="TierDef"/>) that breaks it: below that, with
    /// <see cref="DigTool"/> or bare hands, it can't be dug at all. 0 (soil, rough stone): anyone.
    /// </summary>
    public TierRef DigTier { get; set; }

    /// <summary>Extras digging it sometimes turns up (gold flecks in river sand), rolled like a boulder's ore.</summary>
    public List<BonusYield> DigBonus { get; set; } = [];

    /// <summary>
    /// Ore: hidden until a face of it is opened (<c>Systems.Mining.ShownBlock</c>), passing for the
    /// rock around it, or <see cref="HostRock"/> when there's none to copy.
    /// </summary>
    public bool Ore { get; set; }

    /// <summary>
    /// Presentation only: a tint (RGB 0..1) on rock beside this ore while it's hidden, so a sharp eye
    /// can tell a vein is near (green-stained limestone over copper). The sim never reads it.
    /// </summary>
    public float[]? Stain { get; set; }

    /// <summary>What hidden ore looks like when every neighbor is ore too (default rough stone).</summary>
    public string? HostRock { get; set; }

    [JsonIgnore] public BlockDef? HostRockDef { get; private set; }

    public bool Passable => MoveCost > 0;

    internal override void Resolve(DefDatabase db)
    {
        if (string.IsNullOrEmpty(SpriteSide))
            SpriteSide = Sprite;
        if (BurnsInto != null)
            BurnsIntoDef = db.Block(BurnsInto);
        DigTier = db.ResolveTier(DigTier, $"Block '{Name}'");
        foreach (var b in DigBonus)
            b.ThingDef = db.Thing(b.Def);
        HostRockDef = HostRock != null ? db.Block(HostRock) : Ore && db.TryBlock("Stone", out var rough) ? rough : null;
        foreach (var y in DigYield)
            y.Resolve(db);
    }
}

public enum ThingCategory
{
    Item,
    Building,
    Plant,
    Resource,
}

/// <summary>Kinds of villager work. At equal priority, earlier entries are done first.</summary>
public enum WorkType
{
    Firefight,
    Doctor,
    Warden,
    Refuel,

    /// <summary>Work cooking bills at a fire or hearth (<c>RecipeDef.Skill</c> Cooking).</summary>
    Cook,
    Construct,

    /// <summary>Work crafting bills at workbenches: planks, furniture, gear.</summary>
    Craft,
    Haul,
    Chop,
    Mine,
    Harvest,

    // Food-gathering last: at equal priority, a village gets its walls, fire and wood in first.

    /// <summary>Shoot animals marked for hunting, and butcher carcasses.</summary>
    Hunt,

    /// <summary>Cast from marked spots on the shore.</summary>
    Fish,
}

public sealed class ThingCount
{
    public string Def { get; set; } = "";
    public int Count { get; set; } = 1;

    /// <summary>Optional material the items come in (an oak's wood is oak).</summary>
    public string Material { get; set; } = "";

    [JsonIgnore] public ThingDef ThingDef { get; internal set; } = null!;
    [JsonIgnore] public MaterialDef? MaterialDef { get; internal set; }

    internal void Resolve(DefDatabase db)
    {
        ThingDef = db.Thing(Def);
        MaterialDef = string.IsNullOrEmpty(Material) ? null : db.Material(Material);
        if (MaterialDef != null && !MaterialDef.Fits(ThingDef))
            throw new Exception($"Material '{Material}' is a kind of {MaterialDef.VariantOf.Name}, not {Def}");
    }

    public override string ToString() => $"{Count}x {Def}";
}

/// <summary>
/// A kind of an item (oak, pine or birch wood). Items and the buildings made from them remember
/// their material: it keeps stacks apart, names them ("birch wall") and picks their art. Any
/// material of an item satisfies a cost for that item, so materials never block building.
/// </summary>
public sealed class MaterialDef : Def
{
    /// <summary>The item this is a kind of ("Wood").</summary>
    public string Of { get; set; } = "";

    /// <summary>Tint for art that has no material-specific sprite (RGB, 0..1).</summary>
    public float[] Color { get; set; } = [1, 1, 1];

    // What the material does to things made of it (docs/plans/mining-progression.md). All neutral
    // by default, so a def without them, and things without a material, keep their def's stats.

    /// <summary>Max HP ×: oak walls stand up to more than pine ones.</summary>
    public float HpFactor { get; set; } = 1f;

    /// <summary>Added to a wall's or floor's insulation (kept below 0.99).</summary>
    public float Insulation { get; set; }

    /// <summary>Flammability ×: pine catches sooner than oak.</summary>
    public float Flammability { get; set; } = 1f;

    /// <summary>Building and crafting work ×: granite is slow to dress, sandstone quick.</summary>
    public float WorkFactor { get; set; } = 1f;

    /// <summary>Weight per unit ×, for packs and loads.</summary>
    public float MassFactor { get; set; } = 1f;

    /// <summary>For room beauty later; nothing reads it yet.</summary>
    public float Beauty { get; set; }

    /// <summary>A few words for tooltips ("sturdy, slow to work").</summary>
    public string Character { get; set; } = "";

    [JsonIgnore] public ThingDef VariantOf { get; private set; } = null!;

    /// <summary>Suffix for material-specific sprites: <c>buildings/wall_wood</c> + <c>_birch</c>.</summary>
    [JsonIgnore] public string SpriteSuffix => "_" + Name.ToLowerInvariant();

    internal override void Resolve(DefDatabase db) => VariantOf = db.Thing(Of);

    /// <summary>Can an item of this def come in this material: the item it's a kind of, or one made from it (oak planks).</summary>
    public bool Fits(ThingDef def) => VariantOf == def || (def.MaterialsOfDef != null && def.MaterialsOfDef == VariantOf);

    /// <summary>
    /// "birch wood", "birch wall" (for "wooden wall"), "birch bed"; for stone, the word "stone"
    /// becomes the rock: "granite blocks", "granite wall", "cut granite wall".
    /// </summary>
    public string Describe(string label)
    {
        if (label.StartsWith("wooden ", StringComparison.Ordinal))
            return Label + label[6..];
        if (Of == "Stone" && FindWord(label, "stone") is var at and >= 0)
            return label[..at] + Label + label[(at + 5)..];
        return $"{Label} {label}";
    }

    /// <summary>Where <paramref name="word"/> stands as a whole word in <paramref name="text"/>, or -1.</summary>
    private static int FindWord(string text, string word)
    {
        for (var at = text.IndexOf(word, StringComparison.Ordinal); at >= 0; at = text.IndexOf(word, at + 1, StringComparison.Ordinal))
        {
            var before = at == 0 || text[at - 1] == ' ';
            var after = at + word.Length == text.Length || text[at + word.Length] == ' ';
            if (before && after)
                return at;
        }
        return -1;
    }
}

/// <summary>A butcher spot's edge over cutting up a kill where it fell.</summary>
public sealed class ButcherSpotProps
{
    /// <summary>Butchering work goes this many times as fast.</summary>
    public float Speed { get; set; } = 1.5f;

    /// <summary>Added to the butcher's share of the yield (1 at average skill).</summary>
    public float Yield { get; set; } = 0.1f;
}

/// <summary>A smithy station's part in forging a piece (<c>Work.Smithing</c>).</summary>
public enum SmithyRole
{
    None,
    Anvil,
    Quench,
    Grind,
}

public sealed class BuildingProps
{
    /// <summary>False for structures that only map generation places (natural slopes).</summary>
    public bool Buildable { get; set; } = true;

    public ClimbKind Climb { get; set; }

    /// <summary>Blocks movement and separates rooms.</summary>
    public bool IsWall { get; set; }

    /// <summary>Passable, but separates rooms (and holds heat when closed).</summary>
    public bool IsDoor { get; set; }

    /// <summary>
    /// A door that can be locked (<see cref="Components.Locked"/>; <see cref="Simulation.SetLocked"/>):
    /// shut and impassable while locked, left standing open when not (a cage gate).
    /// </summary>
    public bool Lockable { get; set; }

    /// <summary>
    /// Lives in the map's floor layer, not the structure layer: walkable, and the ceiling of the cell
    /// below. Needs support within <see cref="SupportSpan"/> cells.
    /// </summary>
    public bool IsFloor { get; set; }

    /// <summary>For floors: how many cells sideways they may reach from ground or a wall below.</summary>
    public int SupportSpan { get; set; } = 2;

    /// <summary>
    /// A roof (with <see cref="IsFloor"/>: it lives in the floor layer, on top of the walls): shelters
    /// the cells below like a floor, but isn't walked on or built on. Covers a porch or lean-to off
    /// a wall, out to <see cref="SupportSpan"/> cells; enclosed rooms are roofed without one.
    /// </summary>
    public bool IsRoof { get; set; }

    /// <summary>0 = conducts heat like air, 1 = perfect insulator.</summary>
    public float Insulation { get; set; }

    public List<ThingCount> Cost { get; set; } = [];

    /// <summary>
    /// What taking it apart gives back, if it isn't half its <see cref="Cost"/>: things nobody
    /// builds (the wagon) can still be broken up. A non-empty salvage also makes it deconstructable.
    /// </summary>
    public List<ThingCount> Salvage { get; set; } = [];

    /// <summary>Can be taken apart: anything buildable, or with a <see cref="Salvage"/>.</summary>
    [JsonIgnore] public bool Deconstructable => Buildable || PartOnly || Salvage.Count > 0;

    /// <summary>
    /// Only ever laid as part of a preset building (<see cref="BuildingDef.Parts"/>: a log wall, a
    /// plinth): never on the build bar, and never placed on its own.
    /// </summary>
    public bool PartOnly { get; set; }

    /// <summary>
    /// May stand outside any building while preset buildings are on (<c>Systems.BuildingSystem.PresetMode</c>):
    /// fences, yard workplaces, ladders. Everything else goes up as part of a preset building or
    /// as an extra it allows.
    /// </summary>
    public bool Outdoor { get; set; }

    /// <summary>A wall (with <see cref="IsWall"/>) that fences a yard but never encloses a room: rooms flow past it.</summary>
    public bool Fence { get; set; }

    /// <summary>Low or open enough to see past, though it stops a walker (split rails, not logs).</summary>
    public bool SeeThrough { get; set; }

    /// <summary>Construction work in work-ticks.</summary>
    public float Work { get; set; } = 100;

    public bool IsBed { get; set; }

    /// <summary>Heat pushed into the tile per game hour, in °C·tiles. Requires fuel if a fuel def is set.</summary>
    public float HeatOutput { get; set; }

    /// <summary>Upper bound the heater pushes its own tile towards.</summary>
    public float MaxHeatTemp { get; set; } = 60;

    /// <summary>
    /// While lit, how many °C warmer it feels right beside it (<c>Environment.Warmth</c>: a quarter
    /// less per tile further, gone past three). Felt by creatures only, on top of the air.
    /// </summary>
    public float RadiantWarmth { get; set; }

    public float LightRadius { get; set; }

    public FuelProps? Fuel { get; set; }

    /// <summary>Bellows: set beside a forge, a smith working there pumps them (<c>Components.Blown</c>).</summary>
    public BellowsProps? Bellows { get; set; }

    /// <summary>Where a hot piece goes after the forge (<c>Work.Smithing</c>): the anvil, the quench tub or the grindstone.</summary>
    public SmithyRole Smithy { get; set; }

    /// <summary>Where carcasses are carried to be butchered (<c>Systems.ButcherSpots</c>).</summary>
    public ButcherSpotProps? ButcherSpot { get; set; }

    /// <summary>
    /// Presentation only: the height (in cells) of a top that items in its cell rest on (a table,
    /// a chest, a bench), so a dropped stack sits on it rather than on the floor inside it.
    /// </summary>
    public float SurfaceHeight { get; set; }

    /// <summary>Recipes are made here (<see cref="RecipeDef.Station"/>); villagers work its bills.</summary>
    public WorkbenchProps? Workbench { get; set; }

    /// <summary>
    /// Holds this many stacks of items inside it (a wagon, later chests). Stored items are off the
    /// map but keep the container's cell, so they feel its temperature and still rot.
    /// </summary>
    public int StorageStacks { get; set; }

    /// <summary>
    /// An empath meditating within reach of it does better (<c>Healing.MeditateSession</c>): pain
    /// unsettles them less by this share, and they gather more mana by half as much again.
    /// </summary>
    public float Meditation { get; set; }

    /// <summary>A cask drinks are poured from (<c>Work.PourSession</c>).</summary>
    public CaskProps? Cask { get; set; }

    /// <summary>A stew pot, cooked by taste over the fire beside it (<c>Work.Cooking</c>).</summary>
    public StewPotProps? StewPot { get; set; }

    /// <summary>A tub of hot water dirty tankards and bowls are washed in (<c>Work.WashSession</c>).</summary>
    public bool WashTub { get; set; }

    /// <summary>A bar: mixed drinks are made at it (<c>Work.MixSession</c>).</summary>
    public bool Bar { get; set; }

    /// <summary>Somewhere to sit: a customer served a drink or a meal takes it to one (<c>Hospitality.SavourJob</c>).</summary>
    public bool Seat { get; set; }

    /// <summary>
    /// A serving hatch, set in a wall: a counter that holds what's put up on it for collecting
    /// (<c>Shops.Hatch</c>), dividing rooms as a wall does but heard through, as a doorway is.
    /// </summary>
    public bool Hatch { get; set; }

    /// <summary>
    /// Built into a wall and open only at its front (a fireplace): worked, fed and tended from the
    /// cell in front of it, never from the street behind, and a fire in it heats, lights and
    /// warms that cell, the room's side (<see cref="Environment.Warmth.FireCell"/>), not the wall.
    /// </summary>
    public bool SetInWall { get; set; }
}

/// <summary>
/// A drink's properties: how strong it is, and what else it does. Effects add up in a mix, so a
/// house mix does whatever its ingredients do; the same plumbing will carry reagents and poison.
/// </summary>
public sealed class DrinkEffectProps
{
    /// <summary>How much it goes to the head (<c>Hospitality.Tipsy</c>).</summary>
    public float Strength { get; set; }

    /// <summary>Takes the chill off on a cold day.</summary>
    public float Warming { get; set; }

    /// <summary>Wakes a drinker up: rest.</summary>
    public float Rousing { get; set; }

    /// <summary>Dulls the ache of a wound.</summary>
    public float Numbing { get; set; }

    /// <summary>Loosens the tongue: it goes to the head faster, and they talk.</summary>
    public float Loosening { get; set; }
}

/// <summary>A mixed drink's recipe: a serving from the cask, what's added, and the cues to mix it to.</summary>
public sealed class MixProps
{
    /// <summary>The drink it's built on, drawn from a cask behind the bar (a thing def name).</summary>
    public string Base { get; set; } = "Ale";

    /// <summary>What's added, one of each (thing def names).</summary>
    public List<string> Adds { get; set; } = [];

    /// <summary>The cues it's mixed to, F, A and D, in order. A house mix takes its cues from what's added.</summary>
    public string Cues { get; set; } = "";

    /// <summary>The house mix: any combination that isn't a named one.</summary>
    public bool House { get; set; }
}

/// <summary>What a stew pot ladles out, and how far its ingredients stretch.</summary>
public sealed class StewPotProps
{
    /// <summary>The bowl it ladles (a thing def name): its nutrition is one serving's worth.</summary>
    public string Serves { get; set; } = "Stew";

    /// <summary>The clean vessel a bowl is ladled into (a thing def name).</summary>
    public string Into { get; set; } = "Bowl";

    /// <summary>Servings per serving's worth of nutrition put in: a stew goes further than the raw food.</summary>
    public float Yield { get; set; } = 1.5f;

    /// <summary>The most ingredients' nutrition it holds.</summary>
    public float Holds { get; set; } = 6f;
}

/// <summary>What a cask pours and how many it holds when full.</summary>
public sealed class CaskProps
{
    /// <summary>The drink it pours (a thing def name).</summary>
    public string Pours { get; set; } = "";

    public int Servings { get; set; } = 60;

    /// <summary>The keg it's refilled from (a thing def name), knocked in at the tap (<c>Work.TapSession</c>).</summary>
    public string Keg { get; set; } = "Keg";

    /// <summary>The vessel a serving is poured into (a thing def name).</summary>
    public string Into { get; set; } = "Tankard";
}

public sealed class FuelProps
{
    public string Def { get; set; } = "";
    public int Capacity { get; set; } = 10;

    /// <summary>Fuel units consumed per game day while burning.</summary>
    public float BurnPerDay { get; set; } = 10;

    /// <summary>Haulers refuel when below this fraction of capacity.</summary>
    public float RefuelAt { get; set; } = 0.5f;

    /// <summary>
    /// Fired only for work (a kiln, a forge): it burns, heats and glows only while it has a craft
    /// under way or a bill that wants making, and is only refuelled then. A campfire burns regardless.
    /// </summary>
    public bool OnDemand { get; set; }

    /// <summary>
    /// Other things it burns besides <see cref="Def"/> (coal in a forge), each with how much hotter
    /// and longer it burns. The last fuel put in decides (<c>Fueled.Loaded</c>).
    /// </summary>
    public List<FuelOption> Also { get; set; } = [];

    /// <summary>
    /// About the heat (°C) the building holds on its main fuel, standing in the open, as the heat sim
    /// plays it out (tuned by <c>OreTests.FuelReachMatchesWhatTheStationHolds</c>).
    /// Haulers pick fuel by it: the cheapest that reaches what the next craft needs. 0: no need to say.
    /// </summary>
    public float Reach { get; set; }

    [JsonIgnore] public ThingDef ThingDef { get; internal set; } = null!;

    /// <summary>Everything it burns, <see cref="Def"/> first.</summary>
    [JsonIgnore] public List<FuelOption> Options { get; } = [];

    /// <summary>How <paramref name="fuel"/> burns here, or null if it doesn't take it.</summary>
    public FuelOption? OptionFor(ThingDef? fuel)
    {
        foreach (var o in Options)
            if (o.ThingDef == fuel)
                return o;
        return null;
    }

    public bool Takes(ThingDef fuel) => OptionFor(fuel) != null;

    internal void Resolve(DefDatabase db)
    {
        ThingDef = db.Thing(Def);
        Options.Clear();
        Options.Add(new FuelOption { Def = Def, ThingDef = ThingDef, Reach = Reach });
        foreach (var o in Also)
        {
            o.ThingDef = db.Thing(o.Def);
            Options.Add(o);
        }
    }
}

/// <summary>A fuel a building takes, and how it burns there compared with its main fuel.</summary>
public sealed class FuelOption
{
    public string Def { get; set; } = "";

    /// <summary>Heat output ×.</summary>
    public float HeatFactor { get; set; } = 1f;

    /// <summary>Added to the building's <c>MaxHeatTemp</c> (°C): coal burns hotter than charcoal.</summary>
    public float MaxTempBonus { get; set; }

    /// <summary>Units burned per day ×: below 1 lasts longer.</summary>
    public float BurnFactor { get; set; } = 1f;

    /// <summary>The heat the station holds on this fuel (see <see cref="FuelProps.Reach"/>).</summary>
    public float Reach { get; set; }

    [JsonIgnore] public ThingDef ThingDef { get; internal set; } = null!;
}

public sealed class HarvestProps
{
    public WorkType WorkType { get; set; }
    public float Work { get; set; } = 100;
    public List<ThingCount> Yield { get; set; } = [];

    /// <summary>Extras that turn up now and then (ore in a broken boulder), each rolled on its own.</summary>
    public List<BonusYield> Bonus { get; set; } = [];

    /// <summary>If false the thing stays and regrows (berry bush).</summary>
    public bool DestroyOnHarvest { get; set; } = true;

    public float RegrowDays { get; set; }
}

/// <summary>How the 3D view draws a thing. <see cref="Auto"/> picks from the category and building props.</summary>
public enum RenderShape
{
    Auto,

    /// <summary>A full cube filling the cell (walls, doors).</summary>
    Block,

    /// <summary>A slope up to the next level (stairs, natural slopes).</summary>
    Ramp,

    /// <summary>A thin slab forming the floor of its cell (floors).</summary>
    Slab,

    /// <summary>Flat on the floor (beds, campfires): the sprite is drawn top-down.</summary>
    Decal,

    /// <summary>An upright sprite facing the camera (plants, rocks, items, creatures).</summary>
    Billboard,
}

/// <summary>
/// One box of a <see cref="ThingDef.Model"/>, in cell units with z up: (0,0,0) is the cell's
/// north-west floor corner, (1,1,1) the opposite top corner (boxes may stand taller). The model's
/// front is its +Y (south) side, which faces the viewer at the default camera; a thing's
/// <see cref="Components.Thing.Facing"/> turns the whole model. Sprites are atlas names; a face
/// shows the part of its sprite matching its size (32 px per cell), so small boxes keep the
/// pixel scale.
/// </summary>
public sealed class ModelBox
{
    public float[] Min { get; set; } = [0, 0, 0];
    public float[] Max { get; set; } = [1, 1, 1];
    public string Top { get; set; } = "";
    public string Side { get; set; } = "";

    /// <summary>The +Y face, if it differs from <see cref="Side"/> (a chest's lock, a bed's foot).</summary>
    public string Front { get; set; } = "";

    /// <summary>Items: shown only once the stack holds at least this many (a log pile growing).</summary>
    public int MinCount { get; set; }
}

/// <summary>
/// How a bridge floor is dressed (<see cref="ThingDef.Bridge"/>): low walls along its open sides,
/// piers down to the bed every few cells, and arched undersides between them. Kept as data so a
/// timber footbridge could be dressed differently; the sim never reads it.
/// </summary>
public sealed class BridgeLook
{
    /// <summary>The parapet's face and the coping stones along its top.</summary>
    public string Parapet { get; set; } = "";
    public string Coping { get; set; } = "";

    /// <summary>The piers and the arch rings between them.</summary>
    public string Pier { get; set; } = "";

    /// <summary>How tall the parapet stands above the deck, in levels.</summary>
    public float Height { get; set; } = 0.45f;

    /// <summary>A pier every so many cells along the span (counted from its first cell).</summary>
    public int PierEvery { get; set; } = 2;
}

public sealed class ThingDef : Def
{
    public ThingCategory Category { get; set; }
    public string Sprite { get; set; } = "";

    /// <summary>A tree (any species): what forests are made of and what clearing orders target.</summary>
    public bool IsTree { get; set; }

    /// <summary>Optional toolbar icon (a sprite name); the game falls back to <see cref="Sprite"/>. Presentation only.</summary>
    public string Icon { get; set; } = "";

    /// <summary>
    /// Presentation only: the shape a person holds it as ("sword", "axe", "pick"...: the
    /// <c>apparel/held_&lt;shape&gt;_&lt;body&gt;</c> sheets), its metal tinted by its tier. Null: not drawn in hand.
    /// </summary>
    public string? Held { get; set; }

    public RenderShape Shape { get; set; }

    /// <summary>Drawn size in cells (billboard height, or decal width).</summary>
    public float RenderSize { get; set; } = 1f;

    /// <summary>
    /// Shorthand for a square <see cref="Size"/>: a 3x3 building is <c>"footprint": 3</c>.
    /// </summary>
    public int Footprint { get; set; } = 1;

    /// <summary>
    /// Cells a structure takes up, [across, deep] (deep runs to the model's front, +Y), turned by
    /// its facing (<see cref="Maps.Footprint"/>): a bed is [1, 2], a workbench [2, 1]. Every covered
    /// cell holds the structure in <c>Map.Structure</c>, so nothing else can be built there; its
    /// <see cref="Thing.Cell"/> stays the anchor, where its user sleeps (a bed's head). Defaults to
    /// <see cref="Footprint"/> square.
    /// </summary>
    public int[]? Size { get; set; }

    /// <summary>Resolved <see cref="Size"/>: cells across and deep before turning.</summary>
    [JsonIgnore] public int SizeX { get; private set; } = 1;
    [JsonIgnore] public int SizeY { get; private set; } = 1;

    /// <summary>
    /// Another def this is a shape of (a long table is a <c>"Table"</c>): the build bar shows them
    /// as one entry with a shape picker, and anything asking for the base (a tower's furnishings)
    /// takes any of them.
    /// </summary>
    public string? ShapeOf { get; set; }

    [JsonIgnore] public ThingDef? ShapeOfDef { get; private set; }

    /// <summary>This def, or a shape of it (<see cref="ShapeOf"/>).</summary>
    public bool IsA(ThingDef other) => this == other || ShapeOfDef == other;

    /// <summary>Glows in the dark (moonpetal): drawn unlit and emissive, so it shows at night and blooms.</summary>
    public bool Glow { get; set; }

    /// <summary>
    /// A 3D look made of textured boxes (beds, tables, log piles), replacing the flat sprite. Pure
    /// presentation: the sim never reads it. See <see cref="ModelBox"/>.
    /// </summary>
    public List<ModelBox>? Model { get; set; }

    /// <summary>
    /// A floor that spans water (the stone bridge): the game dresses it with parapets, piers and
    /// arches worked out from the run of it. Pure presentation, like <see cref="Model"/>.
    /// </summary>
    public BridgeLook? Bridge { get; set; }

    /// <summary>
    /// Where a modelled fire's flames stand, in model units (default the cell's middle, on the
    /// floor), and how big: a fireplace's burn in its opening. Presentation only.
    /// </summary>
    public float[]? FireAt { get; set; }
    public float FireSize { get; set; } = 1f;

    /// <summary>Where a lit fire's smoke leaves it, in model units (a chimney's top). Presentation only.</summary>
    public float[]? SmokeAt { get; set; }

    /// <summary>
    /// A model set in a wall (a serving hatch, a fireplace): while the walls beside it are cut
    /// down to show the room, boxes starting this high or higher are left out. Presentation only.
    /// </summary>
    public float? ModelCutAt { get; set; }

    public float MaxHp { get; set; } = 100;
    public float Flammability { get; set; }

    /// <summary>What's left standing in its place when fire destroys this (a tree → a dead tree).</summary>
    public string? BurnsInto { get; set; }

    [JsonIgnore] public ThingDef? BurnsIntoDef { get; private set; }

    /// <summary>Extra movement cost when walking over this (0 = none).</summary>
    public float PathCost { get; set; }

    /// <summary>
    /// Furniture you walk round, not over (a table, a barrel, a workbench): nobody passes and
    /// nothing is put down in its cells. Unlike a wall it doesn't divide rooms.
    /// </summary>
    public bool Solid { get; set; }

    /// <summary>A solid piece a player can climb up onto (<see cref="Components.Perched"/>): a table or a counter.</summary>
    public bool Climbable { get; set; }

    public int StackLimit { get; set; } = 1;

    /// <summary>Weight in kg per unit; required for items, since packs carry by weight (<see cref="ContainerProps"/>).</summary>
    public float Mass { get; set; }

    /// <summary>Too awkward for a pack: carried in the hands only (a pack itself).</summary>
    public bool Bulky { get; set; }

    /// <summary>Food value per unit, as a fraction of a full food need.</summary>
    public float Nutrition { get; set; }

    public bool IsMeat { get; set; }

    /// <summary>A cooked meal: people reach for it first, and it's a pleasure rather than raw meat.</summary>
    public bool Cooked { get; set; }

    /// <summary>Something to drink, served to whoever ordered one (<c>Shops.ServiceKind.Drink</c>).</summary>
    public bool Drink { get; set; }

    /// <summary>What's left once a customer has drunk or eaten it (a dirty tankard, a dirty bowl): a thing def name.</summary>
    public string? Leaves { get; set; }

    /// <summary>A dirty vessel: what it is again once it's washed at a wash tub (<c>Work.WashSession</c>).</summary>
    public string? WashesInto { get; set; }

    /// <summary>A clean vessel a drink is poured or a bowl ladled into (<c>Work.Vessels</c>).</summary>
    public bool Vessel { get; set; }

    /// <summary>What it does to whoever drinks it, or what it brings to a mix (<c>Hospitality.Drinking</c>).</summary>
    public DrinkEffectProps? DrinkEffect { get; set; }

    /// <summary>A mixed drink: what goes into it and the cues it's mixed to (<c>Work.MixSession</c>).</summary>
    public MixProps? Mix { get; set; }

    /// <summary>A bar ingredient: the cue (F, A or D) it's added to a mix on.</summary>
    public string? MixCue { get; set; }

    /// <summary>Days until it rots away at 10–30°C (cold slows it, freezing stops it); 0 never rots.</summary>
    public float RotDays { get; set; }

    public BuildingProps? Building { get; set; }

    /// <summary>
    /// A preset building's phase as one site (made at load, <see cref="BuildingVariant.PhaseDefs"/>):
    /// several villagers deliver to it and build it at once, and finished, it lays every part.
    /// </summary>
    [JsonIgnore] public BuildingDef? PhaseOf { get; internal set; }

    [JsonIgnore] public BuildPhase Phase { get; internal set; }

    /// <summary>A preset building's upgrade as its site (made at load, <see cref="BuildingUpgrade.SiteDef"/>).</summary>
    [JsonIgnore] public BuildingUpgrade? UpgradeOf { get; internal set; }

    public HarvestProps? Harvest { get; set; }

    /// <summary>Items that can be wielded: how they attack.</summary>
    public AttackDef? Weapon { get; set; }

    /// <summary>Items that can be worn: where, and what they protect.</summary>
    public ApparelProps? Apparel { get; set; }

    /// <summary>A tool: what kind (recipes and some work ask for one), and how much faster it makes that work.</summary>
    public ToolProps? Tool { get; set; }

    /// <summary>
    /// The rung of the ladder gear is made at (<see cref="TierDef"/>): a bronze sword, an iron helm.
    /// Tools say it in <see cref="ToolProps.Tier"/>; this is for weapons and armor. 0: not ranked.
    /// </summary>
    public TierRef Tier { get; set; }

    /// <summary>
    /// What it's worth as treasure (coins, precious bars, jewelry), per unit: what dragons love to
    /// sleep on, what the storyteller counts as the village's wealth. 0: not treasure.
    /// </summary>
    public float HoardValue { get; set; }

    /// <summary>The tier this gear stands at: its tool tier, else <see cref="Tier"/>.</summary>
    [JsonIgnore] public int GearTier => Math.Max(Tool?.Tier ?? 0, Tier);

    /// <summary>Items that hold other items by weight: packs, worn on the back.</summary>
    public ContainerProps? Container { get; set; }

    /// <summary>A charm: holds a mending woven into it, to be torn later or carried as a ward (<c>Healing.Charms</c>).</summary>
    public bool Charm { get; set; }

    /// <summary>A tinker's contraption for the road (<c>Tinkering.Contraptions</c>): what it does on a quest.</summary>
    public ContraptionProps? Contraption { get; set; }

    /// <summary>A curio brought back from a quest (<c>Tinkering.Curios</c>): what such a thing can be worth.</summary>
    public CurioProps? Curio { get; set; }

    /// <summary>
    /// Comes in the materials of another item, being made from it: planks are oak, pine or birch
    /// like the wood they were sawn from (a recipe's product takes its ingredients' material).
    /// </summary>
    public string? MaterialsOf { get; set; }

    [JsonIgnore] public ThingDef? MaterialsOfDef { get; private set; }

    /// <summary>Recipes made at this building, in def order (filled in when recipes resolve).</summary>
    [JsonIgnore] public List<RecipeDef> Recipes { get; } = [];

    [JsonIgnore] public bool IsFood => Nutrition > 0;

    /// <summary>Food this kind of creature eats: dragon food for dragons, meat for meat-eaters, the rest for plant-eaters.</summary>
    public bool EdibleBy(CreatureDef eater) =>
        IsFood && (IsMeat ? eater.EatsMeat : eater.EatsPlants);
    [JsonIgnore] public bool BlocksMovement => Building?.IsWall == true || Category == ThingCategory.Resource;

    /// <summary>Nobody walks through it: a wall or boulder, or solid furniture.</summary>
    [JsonIgnore] public bool Impassable => BlocksMovement || Solid;
    [JsonIgnore] public bool IsFloor => Building?.IsFloor == true;
    [JsonIgnore] public bool IsRoof => Building?.IsRoof == true;

    internal override void Resolve(DefDatabase db)
    {
        if (Tool != null)
            Tool.Tier = db.ResolveTier(Tool.Tier, $"Thing '{Name}'");
        Tier = db.ResolveTier(Tier, $"Thing '{Name}'");
        if (Shape == RenderShape.Auto)
            Shape = Building switch
            {
                { IsFloor: true } => RenderShape.Slab,
                { IsWall: true } or { IsDoor: true } => RenderShape.Block,
                { Climb: ClimbKind.Ramp } => RenderShape.Ramp,
                not null => RenderShape.Decal,
                _ => RenderShape.Billboard,
            };
        if (Building is { IsRoof: true, IsFloor: false })
            throw new Exception($"{Name}: a roof goes in the floor layer (isFloor: true)");
        if (Building != null)
        {
            foreach (var c in Building.Cost)
                c.Resolve(db);
            foreach (var c in Building.Salvage)
                c.Resolve(db);
            Building.Fuel?.Resolve(db);
        }
        if (Harvest != null)
        {
            foreach (var y in Harvest.Yield)
                y.Resolve(db);
            foreach (var b in Harvest.Bonus)
                b.ThingDef = db.Thing(b.Def);
        }
        if (BurnsInto != null)
            BurnsIntoDef = db.Thing(BurnsInto);
        if (MaterialsOf != null)
            MaterialsOfDef = db.Thing(MaterialsOf);
        if (Size != null)
        {
            if (Size.Length != 2 || Size[0] < 1 || Size[1] < 1)
                throw new Exception($"{Name}: a size is [across, deep], each at least 1");
            (SizeX, SizeY) = (Size[0], Size[1]);
        }
        else if (Footprint < 1)
            throw new Exception($"{Name}: a footprint is at least one cell across");
        else
            SizeX = SizeY = Footprint;
        ShapeOfDef = ShapeOf != null ? db.Thing(ShapeOf) : null;
        if (ShapeOfDef?.ShapeOf != null)
            throw new Exception($"{Name}: a shape of '{ShapeOf}', which is itself a shape of something else");
        Recipes.Clear();
        if (Weapon != null)
            NameAttack(Weapon, Name, Label);
        if (Category == ThingCategory.Item && Mass <= 0)
            throw new Exception($"{Name} is an item without a mass (kg per unit)");
        if (Container != null && (Category != ThingCategory.Item || Container.Capacity <= 0))
            throw new Exception($"{Name}: only items can be containers, and they need a capacity");
        if (Container != null)
            Bulky = true; // packs never go in packs
        if (Apparel != null)
            foreach (var part in Apparel.Covers)
                if (!db.AllBodies.Any(b => b.PartIndex(part) >= 0))
                    throw new Exception($"{Name} covers '{part}', which no body has");
    }

    internal static void NameAttack(AttackDef a, string name, string label)
    {
        if (string.IsNullOrEmpty(a.Name))
            a.Name = name;
        if (string.IsNullOrEmpty(a.Label))
            a.Label = string.IsNullOrEmpty(label) ? a.Name : label;
    }
}

/// <summary>
/// A tool's kind ("Hammer", "Saw", "Axe", "Pick", "Rod"), its speed and its tier. Work it helps goes
/// <see cref="Speed"/> times as fast; gathering (chopping, mining, digging rock, fishing) without
/// one goes at <c>Crafting.BareHands</c>. Recipes name kinds they need (<c>RecipeDef.Tools</c>, of at
/// least <c>RecipeDef.ToolTier</c>) or go faster with (<c>RecipeDef.HelpedBy</c>).
/// </summary>
/// <summary>What a contraption does for its bearer on a quest (<c>Tinkering.Contraptions</c>).</summary>
public enum ContraptionUse
{
    /// <summary>A tapping rod: feels out traps and pits ahead, so fewer wounds.</summary>
    Traps,

    /// <summary>A grapnel and winch: a fall that would kill becomes a bad wound. Spent when it does.</summary>
    Falls,

    /// <summary>A clockwork lantern: stronger in dark places (<see cref="QuestDef.Dark"/>).</summary>
    Dark,

    /// <summary>A spring snare: stronger against beasts (<see cref="QuestDef.Beasts"/>).</summary>
    Beasts,

    /// <summary>A signal rocket: on a quest gone wrong, some who'd have died are found and brought home. Spent when fired.</summary>
    Rescue,
}

/// <summary>A tinker's contraption: what it does, and how many parts are fitted to make it.</summary>
public sealed class ContraptionProps
{
    public ContraptionUse Use { get; set; }

    /// <summary>Parts filed and seated when it's made by hand (<c>Tinkering.FitSession</c>), 2 to 5.</summary>
    public int Parts { get; set; } = 3;
}

/// <summary>Something odd brought back from a quest, and what it might fetch (<c>Tinkering.Curios</c>).</summary>
public sealed class CurioProps
{
    /// <summary>What a genuine one fetches from a collector, in silver.</summary>
    public int WorthMin { get; set; } = 20;
    public int WorthMax { get; set; } = 60;
}

public sealed class ToolProps
{
    public string Kind { get; set; } = "";
    public float Speed { get; set; } = 1.25f;

    /// <summary>
    /// Its rung on the ladder (<see cref="TierDef"/>, <c>tiers</c> in <c>data/defs/mining.json</c>):
    /// 1 stone (by hand), 2 copper, 3 bronze, 4 iron, 5 steel, 6 dragonforged. Written as a number or a name.
    /// </summary>
    public TierRef Tier { get; set; } = 1;
}

/// <summary>Where apparel is worn; one item per slot.</summary>
public enum ApparelSlot
{
    Head,
    Body,

    /// <summary>Where a pack is worn (<see cref="ThingDef.Container"/>).</summary>
    Back,

    /// <summary>Jewelry: a silver ring, a gold torc. No armor, but fine to wear (and a dragon's delight).</summary>
    Neck,

    // Clothes (outfits.json): little armor, but they're what a person looks like. Under armor:
    // a byrnie goes over the tunic, a cloak over both.

    /// <summary>A tunic, kaftan, apron dress or vest: the garment on the torso, under any armor.</summary>
    Tunic,

    /// <summary>Trousers or a skirt.</summary>
    Legs,

    Feet,

    /// <summary>Worn over everything: a cloak or mantle.</summary>
    Cloak,
}

/// <summary>
/// A set of clothes: what villagers, raiders and story people are dressed in
/// (<c>Systems.Clothing.Dress</c>), and the character creator's presets. A piece may name one
/// material (its dye) or a list to pick from; villagers roll the pick from their own stream.
/// </summary>
public sealed class OutfitDef : Def
{
    public List<OutfitPiece> Pieces { get; set; } = [];

    /// <summary>Villagers and settlers may be dressed in it.</summary>
    public bool Villager { get; set; }

    /// <summary>Only for this body ("Female"), or anyone when empty: an apron dress goes on women.</summary>
    public string Body { get; set; } = "";

    /// <summary>Presentation only: the look the creator's preset starts from (hair style, beard, eye style, stance names).</summary>
    public string? Hair { get; set; }
    public string? Beard { get; set; }
    public string? Eyes { get; set; }
    public string? Stance { get; set; }

    /// <summary>Presentation only: 0xRRGGBB hair and eye colours for the preset, as hex strings ("2E3C72").</summary>
    public string? HairColor { get; set; }
    public string? EyeColor { get; set; }

    /// <summary>Presentation only: the preset's 3D model (<see cref="Components.Appearance.Model"/>), drawn from its own sheets.</summary>
    public string? Model { get; set; }

    /// <summary>Shown as a creator preset.</summary>
    public bool Preset { get; set; }

    internal override void Resolve(DefDatabase db)
    {
        if (Model != null && Components.Appearance.ValidModel(Model) == null)
            throw new Exception($"Outfit {Name}: model \"{Model}\" must be lowercase letters, digits and underscores");
        foreach (var piece in Pieces)
        {
            piece.ThingDef = db.Thing(piece.Def);
            if (piece.ThingDef.Apparel == null)
                throw new Exception($"Outfit {Name}: {piece.Def} isn't apparel");
            if (!Systems.Clothing.IsClothes(piece.ThingDef))
                throw new Exception($"Outfit {Name}: {piece.Def} is armor, not clothes (mark it \"garment\" if it's both)");
            piece.MaterialDefs.Clear();
            foreach (var m in piece.Material != null ? [piece.Material, .. piece.Dyes] : piece.Dyes)
            {
                var mat = db.Material(m);
                if (!mat.Fits(piece.ThingDef))
                    throw new Exception($"Outfit {Name}: {piece.Def} can't be made of {m}");
                piece.MaterialDefs.Add(mat);
            }
        }
    }
}

public sealed class OutfitPiece
{
    public string Def { get; set; } = "";

    /// <summary>Its dye, for presets and story people; villagers pick from <see cref="Dyes"/> too when given.</summary>
    public string? Material { get; set; }

    /// <summary>Dyes a villager's piece may come in.</summary>
    public List<string> Dyes { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore] public ThingDef ThingDef { get; internal set; } = null!;

    /// <summary><see cref="Material"/> first (if any), then <see cref="Dyes"/>.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public List<MaterialDef> MaterialDefs { get; } = [];
}

public sealed class ContainerProps
{
    /// <summary>Most it holds, in kg (its own weight not counted).</summary>
    public float Capacity { get; set; }
}

/// <summary>
/// Armor worn on the body. It only protects the parts it covers (by body part name), each kind of
/// damage separately: a helm turns swords better than clubs, and a jerkin barely slows fire.
/// </summary>
public sealed class ApparelProps
{
    public ApparelSlot Slot { get; set; }
    public List<string> Covers { get; set; } = [];

    /// <summary>Armor against cuts, stabs and bites.</summary>
    public float Sharp { get; set; }
    public float Blunt { get; set; }
    public float Heat { get; set; }

    /// <summary>
    /// Presentation only: the layer sheet drawn over a person wearing it ("apparel/iron_helm"; the
    /// game looks for <c>_body_hair</c> and <c>_body</c> variants first). None means it isn't drawn.
    /// </summary>
    public string? Worn { get; set; }

    /// <summary>
    /// Heavy armor slows its wearer in a fight: each cell walked costs this much more (0.15 = 15%
    /// more time units; <c>Combat.TimeUnits.WalkCost</c>).
    /// </summary>
    public float Encumbrance { get; set; }

    /// <summary>Presentation only: a full helm drawn instead of the hair.</summary>
    public bool HidesHair { get; set; }

    /// <summary>
    /// Clothes, though worn in an armor slot: a leather vest in the Body slot, over a tunic. Outfits
    /// and the character creator may put it on (<c>Systems.Clothing.IsClothes</c>).
    /// </summary>
    public bool Garment { get; set; }

    public float Against(DamageType type) => type switch
    {
        DamageType.Blunt => Blunt,
        DamageType.Burn or DamageType.Frost => Heat, // what keeps out heat keeps out the cold
        DamageType.Shock => Heat * 0.5f,
        DamageType.Venom or DamageType.Fever => 0f,   // poison and sickness get in anywhere
        _ => Sharp,
    };

    public bool CoversPart(string part) => Covers.Contains(part, StringComparer.OrdinalIgnoreCase);
}

public enum Race
{
    Human,
    Dragon,
    Animal,
}

/// <summary>
/// The temperatures a creature is comfortable in and survives (<see cref="CreatureDef.Tolerance"/>),
/// which a life stage may change: compare them with what it feels (<c>Environment.Warmth</c>).
/// </summary>
public readonly record struct TempTolerance(float ComfortMin, float ComfortMax, float SafeMin, float SafeMax);

public sealed class LifeStage
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public float MinAgeDays { get; set; }
    public string Sprite { get; set; } = "";

    /// <summary>Draw scale in tiles.</summary>
    public float Size { get; set; } = 1f;

    public float SpeedFactor { get; set; } = 1f;

    /// <summary>How many times as much food this stage eats: each unit fills that much less of the Food bar.</summary>
    public float HungerFactor { get; set; } = 1f;

    /// <summary>Body heat pushed into the occupied tile per game hour (dragons run hot).</summary>
    public float BodyHeat { get; set; }

    /// <summary>Overrides the creature's <see cref="CreatureDef.ComfortTempMin"/> at this stage (a grown dragon shrugs off a cold night).</summary>
    public float? ComfortTempMin { get; set; }

    /// <summary>Overrides the creature's <see cref="CreatureDef.SafeTempMin"/> at this stage.</summary>
    public float? SafeTempMin { get; set; }

    /// <summary>Can take off and fly over obstacles (see <c>Ai.Navigation</c>).</summary>
    public bool CanFly { get; set; }

    /// <summary>Speed multiplier while airborne.</summary>
    public float FlySpeedFactor { get; set; } = 2f;

    /// <summary>Levels a flight cruises above the highest thing on its route.</summary>
    public int FlyAltitude { get; set; } = 2;

    /// <summary>Fire breath reach in cells; 0 means this stage can't breathe fire on command.</summary>
    public float BreathRange { get; set; }

    /// <summary>Full width of the breath cone in degrees.</summary>
    public float BreathCone { get; set; } = 20f;

    /// <summary>Rays per puff of breath (more = denser flames).</summary>
    public int BreathRays { get; set; } = 10;

    /// <summary>Natural attacks at this stage, on top of the creature's own (a dragon's bite).</summary>
    public List<AttackDef> Attacks { get; set; } = [];

    /// <summary>Natural armor (dragon scales), 0..1: chance to turn a blow that doesn't penetrate it.</summary>
    public float Armor { get; set; }

    /// <summary>Stacks hauled in one trip (a grown dragon gathers several and carries them together).</summary>
    public int CarryStacks { get; set; } = 1;

    /// <summary>Work types this stage can do, replacing the creature's list; null keeps the creature's.</summary>
    public List<WorkType>? Work { get; set; }

    [JsonIgnore] internal bool[]? Allowed { get; set; }
}

public sealed class NeedRate
{
    public string Need { get; set; } = "";

    /// <summary>Fraction of the need lost per game day.</summary>
    public float DecayPerDay { get; set; } = 1f;

    [JsonIgnore] public NeedDef NeedDef { get; internal set; } = null!;
}

public sealed class CreatureDef : Def
{
    public Race Race { get; set; }

    /// <summary>Tiles per game second at speed factor 1.</summary>
    public float MoveSpeed { get; set; } = 4f;

    /// <summary>Time units per turn in turn-based combat (<c>Combat.TimeUnits</c>): walking and actions are paid from them.</summary>
    public float TimeUnits { get; set; } = 60f;

    public float MaxHp { get; set; } = 100;
    public float ComfortTempMin { get; set; } = 10;
    public float ComfortTempMax { get; set; } = 30;

    /// <summary>Below/above these the creature takes damage over time.</summary>
    public float SafeTempMin { get; set; } = -5;

    public float SafeTempMax { get; set; } = 45;

    public bool EatsMeat { get; set; } = true;
    public bool EatsPlants { get; set; } = true;

    /// <summary>
    /// How likely spoiled food is to make it ill, as a factor on <c>Systems.FoodPoisoning.Chance</c>
    /// (dragons eat carrion and rarely mind).
    /// </summary>
    public float FoodPoisonFactor { get; set; } = 1f;

    public List<NeedRate> Needs { get; set; } = [];
    public List<LifeStage> LifeStages { get; set; } = [];

    /// <summary>Natural attacks (fists); life stages can add their own (bites, claws).</summary>
    public List<AttackDef> Attacks { get; set; } = [];

    /// <summary>Body def name. Without one, injuries can't be tracked and damage drains <c>Creature.Hp</c>.</summary>
    public string? Body { get; set; }

    [JsonIgnore] public BodyDef? BodyDef { get; private set; }

    /// <summary>Default sprite if there are no life stages.</summary>
    public string Sprite { get; set; } = "";

    /// <summary>Set for wild animals (<see cref="Race.Animal"/>).</summary>
    public AnimalProps? Animal { get; set; }

    /// <summary>Work types this creature can be given (see <c>WorkGivers</c>); null means all of them.</summary>
    public List<WorkType>? Work { get; set; }

    private bool[] _allowed = [];

    /// <summary>
    /// Whether a creature of this kind, at <paramref name="stage"/>, can do a type of work.
    /// </summary>
    public bool CanDo(WorkType type, LifeStage stage) => (stage.Allowed ?? _allowed)[(int)type];

    /// <summary>The temperatures it's comfortable in and survives at <paramref name="stage"/>.</summary>
    public TempTolerance Tolerance(LifeStage stage) =>
        new(stage.ComfortTempMin ?? ComfortTempMin, ComfortTempMax, stage.SafeTempMin ?? SafeTempMin, SafeTempMax);

    public LifeStage StageForAge(float ageDays)
    {
        LifeStage? result = null;
        foreach (var s in LifeStages)
            if (ageDays >= s.MinAgeDays)
                result = s;
        return result ?? LifeStages.FirstOrDefault() ?? new LifeStage { Sprite = Sprite };
    }

    internal override void Resolve(DefDatabase db)
    {
        foreach (var n in Needs)
            n.NeedDef = db.Need(n.Need);
        if (Body != null)
            BodyDef = db.Body(Body);
        foreach (var a in Attacks.Concat(LifeStages.SelectMany(s => s.Attacks)))
            ThingDef.NameAttack(a, a.Verb, a.Verb);
        LifeStages.Sort((a, b) => a.MinAgeDays.CompareTo(b.MinAgeDays));
        if ((Race == Race.Animal) != (Animal != null))
            throw new Exception($"Creature '{Name}': animals (and only animals) need \"animal\" props");
        if (Animal is { } props && (props.HerdMin < 1 || props.HerdMax < props.HerdMin || props.StayHours.Length != 2))
            throw new Exception($"Creature '{Name}': bad herd size or stayHours");
        if (Animal is { Plural: "" } named)
            named.Plural = Label;
        _allowed = WorkMask(Work);
        foreach (var s in LifeStages)
            s.Allowed = s.Work == null ? null : WorkMask(s.Work);
    }

    private static bool[] WorkMask(List<WorkType>? types)
    {
        var mask = new bool[Enum.GetValues<WorkType>().Length];
        if (types == null)
            Array.Fill(mask, true);
        else
            foreach (var t in types)
                mask[(int)t] = true;
        return mask;
    }
}

public sealed class NeedDef : Def
{
    /// <summary>Below this a creature will drop work to satisfy the need.</summary>
    public float UrgentBelow { get; set; } = 0.25f;

    /// <summary>When idle, satisfy if below this.</summary>
    public float SeekBelow { get; set; } = 0.6f;

    /// <summary>At zero, health damage per game day (starvation etc.).</summary>
    public float DamagePerDayAtZero { get; set; }
}
