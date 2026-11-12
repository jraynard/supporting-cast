using System.Text.Json.Serialization;
using SupportingCast.Sim.Maps;

namespace SupportingCast.Sim.Defs;

/// <summary>
/// A preset building (a dwelling, a longhouse, a smithy): a fixed plan raised in phases
/// (<see cref="BuildPhase"/>: foundation, walls, roof, furnishing) rather than walls dragged
/// cell by cell. It stamps ordinary structures (its <see cref="Parts"/>) into the map, so rooms,
/// heat, paths and fire treat it like anything else built; what it adds is the plan, the order
/// of the phases and where extras go once it's up (<see cref="Slots"/>). See <c>Systems.Buildings</c>.
/// </summary>
public sealed class BuildingDef : Def
{
    /// <summary>Toolbar icon (a sprite name). Presentation only.</summary>
    public string Icon { get; set; } = "";

    /// <summary>
    /// The plan, one string per row from the back (-Y) to the front (+Y), every row as long:
    /// <c>W</c> wall, <c>D</c> door, <c>S</c> stair up to the <see cref="UpperLayout"/>, <c>P</c> post (a wall part standing alone: an open side's
    /// corner), <c>.</c> inside, a space for no part of the plot. Every plot cell gets the
    /// foundation and the roof above it. Turned by the building's facing like a footprint
    /// (<see cref="Footprint.Rotate(int, int, int)"/>) round the plan's middle.
    /// </summary>
    public List<string> Layout { get; set; } = [];

    /// <summary>
    /// A second storey's plan, as <see cref="Layout"/> and as big, standing on it one level up:
    /// <c>W</c>, <c>D</c>, <c>P</c>, <c>.</c> or space. Every cell of it gets the
    /// <see cref="BuildingParts.UpperFloor"/> except above a stair (<c>S</c> in the ground plan,
    /// which must be <c>.</c> here: the stairwell). Empty: one storey. The roof goes over both.
    /// </summary>
    public List<string> UpperLayout { get; set; } = [];

    /// <summary>1, or 2 with an <see cref="UpperLayout"/>.</summary>
    [JsonIgnore] public int Storeys => UpperLayout.Count > 0 ? 2 : 1;

    /// <summary>The structures each phase lays, unless the variant says otherwise.</summary>
    public BuildingParts Parts { get; set; } = new();

    /// <summary>What the Furnish phase puts in, at plan cells.</summary>
    public List<BuildingFurnishing> Furnish { get; set; } = [];

    /// <summary>
    /// Spots for extras once it's finished: this one takes a second bed, these two take bellows
    /// either side of the forge. Each is filled (or not) on its own; nothing else goes inside.
    /// </summary>
    public List<BuildingSlot> Slots { get; set; } = [];

    /// <summary>
    /// Improvements to what it came with, once it's finished: a cooking pot over the hearth. Each
    /// is built as a site of its own, then unlocks the recipes that name it (<see cref="RecipeDef.Upgrade"/>).
    /// </summary>
    public List<BuildingUpgrade> Upgrades { get; set; } = [];

    /// <summary>
    /// Its beds are let, not slept in by whoever's tired (the inn's rooms): only someone given one
    /// (<c>BedOwner</c>, a <c>Lodging</c> order) sleeps there.
    /// </summary>
    public bool LetsBeds { get; set; }

    /// <summary>What it's called until its keeper names it (<c>Shops.ShopName</c>): "The Wild Bear". Empty: just its label.</summary>
    public string DefaultName { get; set; } = "";

    /// <summary>
    /// Where customers line up to be served when it's a shop (<c>Shops.ShopSystem.Line</c>): plan
    /// cells (column, row) on the ground storey, the front of the line first, the one served from
    /// behind the counter. They may run off the plot (out of an open front). Empty: no line, and
    /// customers just wait about the place.
    /// </summary>
    public List<int[]> Line { get; set; } = [];

    /// <summary>A memory its people get from eating inside it (feasting in the longhouse). Null: none.</summary>
    public string? EatThought { get; set; }

    [JsonIgnore] public ThoughtDef? EatThoughtDef { get; private set; }

    /// <summary>Looks to choose from; each may swap parts (thatch for turf). None: one plain variant.</summary>
    public List<BuildingVariant> Variants { get; set; } = [];

    /// <summary>Plan width (across, X before turning) and depth (rows, Y).</summary>
    [JsonIgnore] public int Width { get; private set; }
    [JsonIgnore] public int Depth { get; private set; }

    /// <summary>The plan symbol at a plan cell (column, row), ' ' outside.</summary>
    public char At(int col, int row) => col >= 0 && row >= 0 && col < Width && row < Depth ? Layout[row][col] : ' ';

    /// <summary>The plan symbol at a plan cell on a storey (0 the ground, 1 the upper), ' ' outside or with no such storey.</summary>
    public char At(int col, int row, int level) => level == 0 ? At(col, row)
        : level == 1 && UpperLayout.Count > 0 && col >= 0 && row >= 0 && col < Width && row < Depth ? UpperLayout[row][col] : ' ';

    /// <summary>A plan cell's offset from the anchor before turning (the plan's middle, as footprints centre).</summary>
    public int2 Local(int col, int row) => new(col + Footprint.Lo(Width), row + Footprint.Lo(Depth));

    public BuildingVariant Variant(int index) => Variants[Math.Clamp(index, 0, Variants.Count - 1)];

    /// <summary>Whether any of its slots takes <paramref name="def"/> (or a shape of it).</summary>
    public bool Accepts(ThingDef def) => Slots.Any(s => s.Accepts(def));

    /// <summary>Whether <paramref name="def"/> turned to <paramref name="facing"/> at a plan cell of a storey stands wholly on <c>.</c> cells (clear of a stairwell).</summary>
    internal bool FitsInside(ThingDef def, int col, int row, int facing, int level, HashSet<(int, int, int)>? taken = null)
    {
        int lx = Footprint.Lo(def.SizeX), ly = Footprint.Lo(def.SizeY);
        for (var y = ly; y < ly + def.SizeY; y++)
        for (var x = lx; x < lx + def.SizeX; x++)
        {
            var d = Footprint.Rotate(x, y, facing);
            if (At(col + d.X, row + d.Y, level) != '.' || taken?.Contains((col + d.X, row + d.Y, level)) == true)
                return false;
            if (level > 0 && At(col + d.X, row + d.Y, level - 1) == 'S')
                return false; // the stairwell
        }
        return true;
    }

    /// <summary>
    /// Whether people can still get about inside once it's furnished, or why not: every open cell
    /// reachable from the doors (or an open side), every piece of furniture and slot in reach of
    /// one, and the whole customer line walkable. Solid furniture is walked round, so a careless
    /// layout boxes in a corner or the bar; slots count as filled with whatever's most in the way.
    /// The stair leads up to the cells round its head on the upper storey.
    /// </summary>
    public string? LayoutProblem()
    {
        for (var level = 0; level < Storeys; level++)
        {
            var blocked = new HashSet<(int, int)>();
            foreach (var f in Furnish.Where(f => f.Level == level && f.ThingDef.Impassable))
                foreach (var (x, y, _) in Covers(f.ThingDef, f.At[0], f.At[1], f.Facing, level))
                    blocked.Add((x, y));
            foreach (var slot in Slots.Where(s => s.Level == level))
                if (slot.ThingDefs.FirstOrDefault(d => d.Impassable) is { } solid)
                    foreach (var (x, y, _) in Covers(solid, slot.At[0], slot.At[1], slot.Facing, level))
                        blocked.Add((x, y));

            bool Open(int x, int y) => At(x, y, level) is '.' or 'D' or 'S' && !blocked.Contains((x, y))
                && !(level > 0 && At(x, y, level - 1) == 'S');
            var start = new List<(int, int)>();
            for (var y = 0; y < Depth; y++)
            for (var x = 0; x < Width; x++)
            {
                if (!Open(x, y))
                    continue;
                var edge = x == 0 || y == 0 || x == Width - 1 || y == Depth - 1;
                var upStair = level > 0 && Enumerable.Range(-1, 3).Any(dy => Enumerable.Range(-1, 3).Any(dx => At(x + dx, y + dy, level - 1) == 'S'));
                if (At(x, y, level) == 'D' || (level == 0 && edge) || upStair)
                    start.Add((x, y));
            }
            var reached = new HashSet<(int, int)>(start);
            var queue = new Queue<(int, int)>(start);
            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var (nx, ny) = (x + dx, y + dy);
                    if ((dx == 0 && dy == 0) || !Open(nx, ny) || reached.Contains((nx, ny)))
                        continue;
                    if (dx != 0 && dy != 0 && (!Open(x + dx, y) || !Open(x, y + dy)))
                        continue; // no squeezing past corners
                    reached.Add((nx, ny));
                    queue.Enqueue((nx, ny));
                }
            }

            for (var y = 0; y < Depth; y++)
            for (var x = 0; x < Width; x++)
                if (At(x, y, level) == '.' && Open(x, y) && !reached.Contains((x, y)))
                    return $"{Name}: cell [{x}, {y}] on storey {level} can't be reached";
            bool Touched(IEnumerable<(int X, int Y, int)> cells) =>
                cells.Any(c => reached.Contains((c.X, c.Y)) || Enumerable.Range(-1, 3).Any(dy => Enumerable.Range(-1, 3).Any(dx => reached.Contains((c.X + dx, c.Y + dy)))));
            foreach (var f in Furnish.Where(f => f.Level == level))
                if (!Touched(Covers(f.ThingDef, f.At[0], f.At[1], f.Facing, level)))
                    return $"{Name}: nobody can get to the {f.ThingDef.Label} at [{f.At[0]}, {f.At[1]}]";
            foreach (var slot in Slots.Where(s => s.Level == level))
                if (!Touched([(slot.At[0], slot.At[1], level)]))
                    return $"{Name}: nobody can get to the slot at [{slot.At[0]}, {slot.At[1]}]";
            if (level == 0)
                foreach (var cell in Line)
                    if (!reached.Contains((cell[0], cell[1])) && At(cell[0], cell[1]) != ' ')
                        return $"{Name}: the line's cell [{cell[0]}, {cell[1]}] can't be stood in";
        }
        return null;
    }

    /// <summary>The plan cells a piece of furniture covers.</summary>
    internal static IEnumerable<(int, int, int)> Covers(ThingDef def, int col, int row, int facing, int level)
    {
        int lx = Footprint.Lo(def.SizeX), ly = Footprint.Lo(def.SizeY);
        for (var y = ly; y < ly + def.SizeY; y++)
        for (var x = lx; x < lx + def.SizeX; x++)
        {
            var d = Footprint.Rotate(x, y, facing);
            yield return (col + d.X, row + d.Y, level);
        }
    }

    internal override void Resolve(DefDatabase db)
    {
        if (Layout.Count == 0 || Layout[0].Length == 0)
            throw new Exception($"Building '{Name}' has no layout");
        Width = Layout[0].Length;
        Depth = Layout.Count;
        foreach (var row in Layout)
        {
            if (row.Length != Width)
                throw new Exception($"Building '{Name}': every layout row must be {Width} long");
            foreach (var ch in row)
                if (ch is not ('W' or 'D' or 'P' or 'S' or '.' or ' '))
                    throw new Exception($"Building '{Name}': unknown layout symbol '{ch}' (W, D, P, S, . or space)");
        }
        if (Line.Any(c => c.Length != 2))
            throw new Exception($"Building '{Name}': each line cell is [column, row]");
        if (UpperLayout.Count > 0 && UpperLayout.Count != Depth)
            throw new Exception($"Building '{Name}': the upper layout must have {Depth} rows, as the ground's");
        for (var row = 0; row < UpperLayout.Count; row++)
        {
            if (UpperLayout[row].Length != Width)
                throw new Exception($"Building '{Name}': every upper layout row must be {Width} long");
            for (var col = 0; col < Width; col++)
            {
                var ch = UpperLayout[row][col];
                if (ch is not ('W' or 'D' or 'P' or '.' or ' '))
                    throw new Exception($"Building '{Name}': unknown upper layout symbol '{ch}' (W, D, P, . or space)");
                if (ch != ' ' && At(col, row) == ' ')
                    throw new Exception($"Building '{Name}': the upper storey overhangs the ground plan at [{col}, {row}]");
                if (At(col, row) == 'S' && ch != '.')
                    throw new Exception($"Building '{Name}': above the stair at [{col}, {row}] must be open ('.')");
            }
        }
        if (Variants.Count == 0)
            Variants.Add(new BuildingVariant { Name = "Plain", Label = Label });
        foreach (var v in Variants)
        {
            v.KitDef = string.IsNullOrEmpty(v.Kit) ? null : db.Kit(v.Kit);
            v.Resolved = BuildingParts.Merge(v.Parts, Parts);
            v.Resolved.Resolve(db, this);
            if (string.IsNullOrEmpty(v.Label))
                v.Label = v.Name;
        }
        EatThoughtDef = EatThought != null ? db.Thought(EatThought) : null;
        foreach (var u in Upgrades)
        {
            u.OnDef = db.Thing(u.On);
            if (!Furnish.Exists(f => db.Thing(f.Def).IsA(u.OnDef)))
                throw new Exception($"Building '{Name}': the upgrade '{u.Name}' goes on a {u.On}, which it doesn't come with");
            foreach (var c in u.Cost)
                c.Resolve(db);
        }
        // Furniture stands inside, clear of the rest of it; slots stand clear of the furniture.
        var furnished = new HashSet<(int, int, int)>();
        foreach (var f in Furnish)
        {
            if (f.Level < 0 || f.Level >= Storeys)
                throw new Exception($"Building '{Name}': the {f.Def} is on storey {f.Level}, which it doesn't have");
            f.ThingDef = db.Thing(f.Def);
            if (f.ThingDef.Building == null)
                throw new Exception($"Building '{Name}' furnishes '{f.Def}', which isn't a building");
            if (f.At.Length != 2)
                throw new Exception($"Building '{Name}': a furnishing is at [column, row]");
            if (!FitsInside(f.ThingDef, f.At[0], f.At[1], f.Facing, f.Level, furnished))
                throw new Exception($"Building '{Name}': the {f.Def} at [{f.At[0]}, {f.At[1]}] doesn't fit inside");
            furnished.UnionWith(Covers(f.ThingDef, f.At[0], f.At[1], f.Facing, f.Level));
        }
        foreach (var slot in Slots)
        {
            if (slot.At.Length != 2 || slot.Defs.Count == 0)
                throw new Exception($"Building '{Name}': a slot is at [column, row] and takes at least one def");
            slot.ThingDefs.Clear();
            foreach (var name in slot.Defs)
            {
                var d = db.Thing(name);
                if (slot.Level < 0 || slot.Level >= Storeys || d.Building == null || !FitsInside(d, slot.At[0], slot.At[1], slot.Facing, slot.Level, furnished))
                    throw new Exception($"Building '{Name}': a {name} in the slot at [{slot.At[0]}, {slot.At[1]}] doesn't fit inside, clear of the furniture");
                slot.ThingDefs.Add(d);
            }
        }
    }
}

/// <summary>The structure defs a building's phases lay (all <see cref="BuildingProps.PartOnly"/>).</summary>
public sealed class BuildingParts
{
    /// <summary>A floor under every plot cell (the plinth). Null: no foundation phase.</summary>
    public string? Foundation { get; set; }
    public string? Wall { get; set; }

    /// <summary>For <c>P</c> cells; the wall when unset.</summary>
    public string? Post { get; set; }
    public string? Door { get; set; }

    /// <summary>A roof over every plot cell, above the top storey. Null: no roof phase.</summary>
    public string? Roof { get; set; }

    /// <summary>For <c>S</c> cells: stairs up to the second storey.</summary>
    public string? Stair { get; set; }

    /// <summary>The floor of the second storey, laid on the walls below.</summary>
    public string? UpperFloor { get; set; }

    [JsonIgnore] public ThingDef? FoundationDef { get; private set; }
    [JsonIgnore] public ThingDef? WallDef { get; private set; }
    [JsonIgnore] public ThingDef? PostDef { get; private set; }
    [JsonIgnore] public ThingDef? DoorDef { get; private set; }
    [JsonIgnore] public ThingDef? RoofDef { get; private set; }
    [JsonIgnore] public ThingDef? StairDef { get; private set; }
    [JsonIgnore] public ThingDef? UpperFloorDef { get; private set; }

    /// <summary>The part for a plan symbol in the Walls phase.</summary>
    public ThingDef? ForSymbol(char ch) => ch switch { 'W' => WallDef, 'D' => DoorDef, 'P' => PostDef, 'S' => StairDef, _ => null };

    internal static BuildingParts Merge(BuildingParts over, BuildingParts base_) => new()
    {
        Foundation = over.Foundation ?? base_.Foundation,
        Wall = over.Wall ?? base_.Wall,
        Post = over.Post ?? base_.Post,
        Door = over.Door ?? base_.Door,
        Roof = over.Roof ?? base_.Roof,
        Stair = over.Stair ?? base_.Stair,
        UpperFloor = over.UpperFloor ?? base_.UpperFloor,
    };

    internal void Resolve(DefDatabase db, BuildingDef owner)
    {
        ThingDef? Part(string? name, string role, Func<ThingDef, bool> fits)
        {
            if (name == null)
                return null;
            var d = db.Thing(name);
            if (d.Building is not { PartOnly: true } || !fits(d))
                throw new Exception($"Building '{owner.Name}': its {role} '{name}' must be a partOnly {role}");
            return d;
        }
        FoundationDef = Part(Foundation, "foundation", d => d.IsFloor && !d.IsRoof);
        WallDef = Part(Wall, "wall", d => d.Building!.IsWall);
        PostDef = Part(Post, "post", d => d.Building!.IsWall) ?? WallDef;
        DoorDef = Part(Door, "door", d => d.Building!.IsDoor);
        RoofDef = Part(Roof, "roof", d => d.IsRoof);
        StairDef = Part(Stair, "stair", d => d.Building!.Climb == ClimbKind.Ramp);
        UpperFloorDef = Part(UpperFloor, "upper floor", d => d.IsFloor && !d.IsRoof);
        var plan = string.Concat(owner.Layout) + string.Concat(owner.UpperLayout);
        if ((plan.Contains('W') && WallDef == null) || (plan.Contains('D') && DoorDef == null) || (plan.Contains('P') && PostDef == null))
            throw new Exception($"Building '{owner.Name}': its plan has walls, doors or posts without a part for them");
        if (plan.Contains('S') && StairDef == null)
            throw new Exception($"Building '{owner.Name}': its plan has stairs without a stair part");
        if (owner.UpperLayout.Count > 0 && UpperFloorDef == null)
            throw new Exception($"Building '{owner.Name}': it has an upper storey without an upper floor part");
    }
}

/// <summary>A look a building comes in; <see cref="Kit"/> is the art set the game draws it with.</summary>
public sealed class BuildingVariant
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";

    /// <summary>Presentation only: the art kit (<see cref="BuildingKitDef"/>) it's drawn with; none: the parts' own looks.</summary>
    public string Kit { get; set; } = "";

    [JsonIgnore] public BuildingKitDef? KitDef { get; internal set; }

    /// <summary>Parts this variant uses instead of the building's.</summary>
    public BuildingParts Parts { get; set; } = new();

    [JsonIgnore] public BuildingParts Resolved { get; internal set; } = new();

    /// <summary>
    /// The site each raised phase is built as (Foundation, Walls, the upper storey's, Roof; null where a phase lays
    /// nothing): a def made at load from the parts, costing and taking the work of them all
    /// (<see cref="DefDatabase"/>), covering the whole plot so builders gather round it.
    /// </summary>
    [JsonIgnore] public ThingDef?[] PhaseDefs { get; } = new ThingDef?[(int)BuildPhase.Furnish];

    public ThingDef? PhaseDef(BuildPhase phase) => phase < BuildPhase.Furnish ? PhaseDefs[(int)phase] : null;
}

/// <summary>A piece of furniture the Furnish phase puts in.</summary>
public sealed class BuildingFurnishing
{
    public string Def { get; set; } = "";

    /// <summary>The plan cell [column, row] it's anchored at.</summary>
    public int[] At { get; set; } = [0, 0];

    /// <summary>Its facing within the plan (added to the building's).</summary>
    public int Facing { get; set; }

    /// <summary>The storey it stands on: 0 the ground, 1 the upper.</summary>
    public int Level { get; set; }

    [JsonIgnore] public ThingDef ThingDef { get; internal set; } = null!;
}

/// <summary>
/// A spot in a finished building for one extra, of any of <see cref="Defs"/> (a bed or a straw
/// pallet), at a plan cell and facing: where a second bed goes, where the bellows stand.
/// </summary>
public sealed class BuildingSlot
{
    public List<string> Defs { get; set; } = [];

    /// <summary>The plan cell [column, row] the extra is anchored at.</summary>
    public int[] At { get; set; } = [0, 0];

    /// <summary>Its facing within the plan (added to the building's).</summary>
    public int Facing { get; set; }

    /// <summary>The storey it's on: 0 the ground, 1 the upper.</summary>
    public int Level { get; set; }

    [JsonIgnore] public List<ThingDef> ThingDefs { get; } = [];

    /// <summary>Takes <paramref name="def"/> (or a shape of it).</summary>
    public bool Accepts(ThingDef def) => ThingDefs.Any(def.IsA);

    /// <summary>"bed or straw pallet".</summary>
    [JsonIgnore] public string Label => string.Join(" or ", ThingDefs.Select(d => d.Label));
}

/// <summary>The phases a preset building goes up in, in order; phases with nothing to lay are skipped.</summary>
public enum BuildPhase : byte
{
    Foundation,
    Walls,

    /// <summary>A second storey's floor, on the walls below.</summary>
    UpperFloor,

    /// <summary>A second storey's walls, on that floor.</summary>
    UpperWalls,
    Roof,
    Furnish,
    Done,
}

/// <summary>
/// Presentation only (the sim never reads it): how a preset building's parts are drawn, so a
/// longhouse reads as logs and turf rather than generic blocks. Wall cells are pieced together
/// from their plan neighbours: a <see cref="Span"/> for a straight run, else a <see cref="Corner"/>
/// post with an <see cref="Arm"/> towards each neighbour; models are made for a wall running
/// along X and turned as needed. The roof is generated over the whole plot (<see cref="Roof"/>).
/// </summary>
public sealed class BuildingKitDef : Def
{
    /// <summary>A wall cell in a straight run, the whole cell along X.</summary>
    public List<ModelBox> Span { get; set; } = [];

    /// <summary>Half a wall, from the cell's middle to its +X edge: one per neighbour at a corner or an end.</summary>
    public List<ModelBox> Arm { get; set; } = [];

    /// <summary>Where walls meet or end: the post they're joined at.</summary>
    public List<ModelBox> Corner { get; set; } = [];

    /// <summary>A <c>P</c> cell: a post standing on its own (an open side's corner).</summary>
    public List<ModelBox> Post { get; set; } = [];

    /// <summary>The frame round a door in a wall running along X (the door itself swings, drawn apart).</summary>
    public List<ModelBox> Door { get; set; } = [];

    /// <summary>The floor laid over the foundation (a sprite), instead of the foundation part's own.</summary>
    public string Floor { get; set; } = "";

    public RoofKit? Roof { get; set; }
}

/// <summary>
/// A pitched roof over a preset building's plot: two slopes meeting at a ridge along its long
/// side, resting on the walls, overhanging them as eaves and past the gable ends, with boarded
/// gables and barge boards that cross above the ridge.
/// </summary>
public sealed class RoofKit
{
    public string Sprite { get; set; } = "";

    /// <summary>Rise per cell of run (world levels): 0.35 looks about 35 degrees.</summary>
    public float Pitch { get; set; } = 0.35f;

    /// <summary>How far the slopes reach past the side walls' middles, and the roof past the gables.</summary>
    public float Eave { get; set; } = 0.4f;
    public float Overhang { get; set; } = 0.25f;

    /// <summary>How thick the roof looks at its edges.</summary>
    public float Thickness { get; set; } = 0.14f;

    /// <summary>The gable's boards: a sprite alpha-cut to a triangle, its high side on the right.</summary>
    public string Gable { get; set; } = "";

    /// <summary>The gable's boards below the triangle (a wide gable's lower courses).</summary>
    public string GableFill { get; set; } = "";

    /// <summary>The barge boards along the gables' slopes, and how far past the ridge they cross.</summary>
    public string Trim { get; set; } = "";
    public float Horns { get; set; } = 0.3f;

    /// <summary>What hangs along the eaves (a turf lip), if not <see cref="Trim"/>, and how far below the roof it hangs.</summary>
    public string EaveTrim { get; set; } = "";
    public float Lip { get; set; } = 0.08f;

    /// <summary>The beam along the ridge (empty for none).</summary>
    public string Ridge { get; set; } = "";
}

/// <summary>
/// An improvement to one of a finished building's furnishings (<see cref="On"/>: a cooking pot on
/// the hearth): built as a site of its own (<see cref="SiteDef"/>, made at load), then recorded on
/// the building (<c>PresetBuilding.Upgrades</c>), drawn on the furnishing (<see cref="Model"/>), and
/// unlocking the recipes that name it.
/// </summary>
public sealed class BuildingUpgrade
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>The furnishing it's fitted to.</summary>
    public string On { get; set; } = "";
    public List<ThingCount> Cost { get; set; } = [];
    public float Work { get; set; } = 100;

    /// <summary>Presentation only: boxes drawn on the furnishing once it's fitted (in its cell's space).</summary>
    public List<ModelBox> Model { get; set; } = [];

    [JsonIgnore] public ThingDef OnDef { get; internal set; } = null!;

    /// <summary>The site it's built as: costs and takes the work above, at the furnishing's cell.</summary>
    [JsonIgnore] public ThingDef SiteDef { get; internal set; } = null!;

    [JsonIgnore] public BuildingDef Building { get; internal set; } = null!;
}
