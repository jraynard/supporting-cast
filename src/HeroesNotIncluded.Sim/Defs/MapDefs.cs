using System.Text.Json.Serialization;

namespace HeroesNotIncluded.Sim.Defs;

/// <summary>
/// A hand-made map (<c>Maps.MapBuilder</c> stamps it into a simulation) instead of a generated
/// valley, so a round's village can be laid out like a drawn one: where the square, the lanes, the
/// river and every house go. Three text grids, one string per row from the north (-Y), every row
/// <see cref="Width"/> long, read through the def's own legends:
/// <list type="bullet">
/// <item><see cref="Ground"/> (<c>ground</c>): the top block of each column.</item>
/// <item><see cref="Levels"/> (<c>levels</c>): a digit per column, the level people stand on there, counted up
/// from <see cref="BaseZ"/>. Water stands lower than its banks, so rivers sit in their beds.</item>
/// <item><see cref="Features"/> (<c>features</c>): a structure on the ground (a tree, a fence, the well). A legend
/// entry <c>"Name@L"</c> stands at level <c>L</c> rather than on the ground: a bridge over a river. A floor
/// and what stands on it are joined with <c>+</c>: <c>"Boardwalk@2+DippingPost@2"</c>, a post on a deck.</item>
/// </list>
/// Buildings are listed with exact anchors; a building that doesn't fit is a load-time mistake in
/// the map, not something to search around.
/// </summary>
public sealed class MapDef : Def
{
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Z levels: ground to dig into below, headroom for storeys and roofs above.</summary>
    public int Depth { get; set; } = Maps.MapGenerator.RecommendedDepth;

    /// <summary>The z a height digit of 0 stands on.</summary>
    public int BaseZ { get; set; } = 2;

    /// <summary>Ground chars to block def names.</summary>
    public Dictionary<string, string> GroundLegend { get; set; } = [];

    /// <summary>Feature chars to thing def names, optionally <c>"Name@L"</c> for a fixed level, or <c>"Name!"</c> for a door or gate locked (a pen's). '.' is always nothing.</summary>
    public Dictionary<string, string> FeatureLegend { get; set; } = [];

    [JsonPropertyName("ground")] public List<string> GroundRows { get; set; } = [];
    [JsonPropertyName("levels")] public List<string> HeightRows { get; set; } = [];
    [JsonPropertyName("features")] public List<string> FeatureRows { get; set; } = [];

    public List<MapBuilding> Buildings { get; set; } = [];

    /// <summary>Workplaces the map has a plot for whose building isn't loaded (a trade's mod turned off).</summary>
    [JsonIgnore] public List<string> Absent { get; private set; } = [];

    /// <summary>Where the villagers land and the village's middle (the hero walks in near here): x, y.</summary>
    public int[] Center { get; set; } = [];

    /// <summary>The starting stockpile zone: x, y, width, height on the center's level.</summary>
    public int[] Stockpile { get; set; } = [];

    /// <summary>Animals kept in the village from the start (pigs in their pen).</summary>
    public List<MapLivestock> Livestock { get; set; } = [];

    /// <summary>
    /// What's put up in the square for the festival (<c>Round.FestivalSystem</c>): lanterns, the
    /// bonfire, the feast tables, the show table and the games. Not there until the festival begins.
    /// </summary>
    public List<MapProp> Festival { get; set; } = [];

    /// <summary>
    /// The veins (<see cref="VeinDef"/> names) the seed may scatter underground here; empty, all of them.
    /// A town in a round leaves out what would upset its trade (silver struck and minted into coin).
    /// </summary>
    public List<string> Veins { get; set; } = [];

    /// <summary>The <see cref="Veins"/> resolved, in the defs' order; every vein when the map names none.</summary>
    [JsonIgnore] public IReadOnlyList<VeinDef> VeinDefs { get; private set; } = [];

    /// <summary>
    /// Ore bodies the map is sure to have (a mine's iron and coal), on top of the veins the seed scatters,
    /// which keep clear of the village and may miss a hill altogether. Stamped after the veins.
    /// </summary>
    public List<MapSeam> Seams { get; set; } = [];

    /// <summary>Passages carved through the rock (a mine's adit into its hill), dug out before anything is placed.</summary>
    public List<MapTunnel> Tunnels { get; set; } = [];

    /// <summary>Top block per column (y * Width + x).</summary>
    [JsonIgnore] public BlockDef[] Ground { get; private set; } = [];

    /// <summary>Level per column, from <see cref="BaseZ"/>.</summary>
    [JsonIgnore] public int[] Levels { get; private set; } = [];

    /// <summary>Feature per column, and the fixed level it stands at (-1: on the ground).</summary>
    [JsonIgnore] public MapFeature?[] Features { get; private set; } = [];

    /// <summary>The surface z people stand on in a column.</summary>
    public int StandZ(int x, int y) => BaseZ + Levels[y * Width + x];

    private DefDatabase? _db;

    internal override void Resolve(DefDatabase db)
    {
        _db = db;
        string Where(string grid, int row) => $"Map '{Name}': {grid} row {row}";
        if (Width <= 0 || Height <= 0)
            throw new Exception($"Map '{Name}': width and height must be positive");
        Check(GroundRows, "ground");
        Check(HeightRows, "levels");
        Check(FeatureRows, "features");

        var ground = new Dictionary<char, BlockDef>();
        foreach (var (k, v) in GroundLegend)
            ground[Key(k, "ground legend")] = db.Block(v);
        var features = new Dictionary<char, MapFeature>();
        foreach (var (k, value) in FeatureLegend)
        {
            var parts = value.Split('+');
            if (parts.Length > 2)
                throw new Exception($"Map '{Name}': feature '{value}' stacks more than a floor and one thing on it");
            var (def, level, locked) = Part(parts[0]);
            ThingDef? on = null;
            if (parts.Length == 2)
            {
                var (top, topLevel, _) = Part(parts[1]);
                if (!def.IsFloor || top.IsFloor || topLevel != level)
                    throw new Exception($"Map '{Name}': '{value}' must be a floor and a thing standing on it, at the same level");
                on = top;
            }
            features[Key(k, "feature legend")] = new MapFeature(def, level, locked, on);
        }

        Ground = new BlockDef[Width * Height];
        Levels = new int[Width * Height];
        Features = new MapFeature?[Width * Height];
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            var col = y * Width + x;
            var g = GroundRows[y][x];
            Ground[col] = ground.TryGetValue(g, out var b) ? b : throw new Exception($"{Where("ground", y)}: '{g}' at column {x} isn't in the ground legend");
            var h = HeightRows[y][x];
            Levels[col] = h is >= '0' and <= '9' ? h - '0' : throw new Exception($"{Where("levels", y)}: '{h}' at column {x} isn't a digit");
            if (BaseZ + Levels[col] < 1 || BaseZ + Levels[col] >= Depth)
                throw new Exception($"{Where("levels", y)}: level {Levels[col]} at column {x} is outside the map's depth");
            var f = FeatureRows[y][x];
            if (f != '.')
                Features[col] = features.TryGetValue(f, out var t) ? t : throw new Exception($"{Where("features", y)}: '{f}' at column {x} isn't in the feature legend");
        }

        if (Center.Length != 2 || !Inside(Center[0], Center[1]))
            throw new Exception($"Map '{Name}': center must be [x, y] on the map");
        if (Stockpile.Length is not (0 or 4))
            throw new Exception($"Map '{Name}': stockpile must be [x, y, width, height]");
        // A trade's workplace whose def isn't loaded (its mod is off) leaves its plot empty; any other
        // building that isn't there is a mistake in the map.
        Absent = [.. Buildings.Where(b => b.Role != "" && !db.HasBuilding(b.Def)).Select(b => b.Def)];
        Buildings.RemoveAll(b => b.Role != "" && !db.HasBuilding(b.Def));
        foreach (var bld in Buildings)
            bld.Resolve(db, this);
        foreach (var l in Livestock)
            l.Resolve(db, this);
        foreach (var p in Festival)
            p.Resolve(db, this);
        foreach (var v in Veins)
            if (!db.AllVeins.Any(d => d.Name == v))
                throw new Exception($"Map '{Name}': there's no vein '{v}'");
        VeinDefs = Veins.Count == 0 ? db.AllVeins : [.. db.AllVeins.Where(d => Veins.Contains(d.Name))];
        foreach (var seam in Seams)
            seam.Resolve(db, this);
        foreach (var t in Tunnels)
            t.Resolve(this);
    }

    internal bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>A legend part, <c>"Name"</c>, <c>"Name@L"</c> or <c>"Name!"</c>: its def, fixed level (-1: on the ground) and whether it's locked.</summary>
    private (ThingDef Def, int Level, bool Locked) Part(string value)
    {
        var locked = value.EndsWith('!');
        var v = locked ? value[..^1] : value;
        var at = v.IndexOf('@');
        var level = at < 0 ? -1 : int.Parse(v[(at + 1)..]);
        var def = _db!.Thing(at < 0 ? v : v[..at]);
        if (locked && def.Building is not { IsDoor: true, Lockable: true })
            throw new Exception($"Map '{Name}': '{def.Name}' can't be locked ('!'): it isn't a lockable door");
        return (def, level, locked);
    }

    /// <summary>
    /// The level people stand on at a column, or a fixed level given (a building or a prop on a lake
    /// town's deck: <see cref="MapBuilding.Level"/>), counted from <see cref="BaseZ"/>.
    /// </summary>
    public int StandZ(int x, int y, int level) => level >= 0 ? BaseZ + level : StandZ(x, y);

    /// <summary>
    /// Where people stand in a column once the map's built: on a floor it lays at a fixed level (a
    /// lake town's deck) if it has one, else on the ground. A landing or a stockpile out on the water.
    /// </summary>
    public int SurfaceZ(int x, int y) =>
        Features[y * Width + x] is { Def.IsFloor: true, Level: >= 0 } deck ? BaseZ + deck.Level : StandZ(x, y);

    private void Check(List<string> rows, string grid)
    {
        if (rows.Count != Height)
            throw new Exception($"Map '{Name}': {grid} has {rows.Count} rows, not {Height}");
        for (var y = 0; y < rows.Count; y++)
            if (rows[y].Length != Width)
                throw new Exception($"Map '{Name}': {grid} row {y} is {rows[y].Length} long, not {Width}");
    }

    private char Key(string k, string legend) =>
        k.Length == 1 && k != "." ? k[0] : throw new Exception($"Map '{Name}': {legend} key '{k}' must be one character other than '.'");
}

/// <summary>
/// A structure a map places: its def, a fixed level (-1: on the ground), whether it's locked (a gate),
/// and for a floor, what stands on it (<see cref="On"/>: a dipping post on a deck).
/// </summary>
public readonly record struct MapFeature(ThingDef Def, int Level, bool Locked, ThingDef? On = null);

/// <summary>A preset building on a map, finished at the round's start.</summary>
public sealed class MapBuilding
{
    public string Def { get; set; } = "";

    /// <summary>A variant's name; empty: the building's first.</summary>
    public string Variant { get; set; } = "";

    /// <summary>The anchor column (x, y); it stands on the ground there.</summary>
    public int[] At { get; set; } = [];
    public int Facing { get; set; }

    /// <summary>What the round uses it for: Tavern, Forge, Shrine, TinkerShop, Home. Empty: it's just there.</summary>
    public string Role { get; set; } = "";

    /// <summary>
    /// The level it stands at, from the map's base (a house on a lake town's deck, over the water);
    /// -1: on the ground in its anchor's column.
    /// </summary>
    public int Level { get; set; } = -1;

    [JsonIgnore] public BuildingDef BuildingDef { get; private set; } = null!;
    [JsonIgnore] public int VariantIndex { get; private set; }

    internal void Resolve(DefDatabase db, MapDef map)
    {
        BuildingDef = db.Building(Def);
        if (At.Length != 2 || !map.Inside(At[0], At[1]))
            throw new Exception($"Map '{map.Name}': building '{Def}' needs at: [x, y] on the map");
        VariantIndex = Variant == "" ? 0 : BuildingDef.Variants.FindIndex(v => v.Name.Equals(Variant, StringComparison.OrdinalIgnoreCase));
        if (VariantIndex < 0)
            throw new Exception($"Map '{map.Name}': building '{Def}' has no variant '{Variant}'");
    }
}

/// <summary>Village animals placed at the start, spread round a spot.</summary>
public sealed class MapLivestock
{
    public string Creature { get; set; } = "";
    public int Count { get; set; } = 1;
    public int[] At { get; set; } = [];

    [JsonIgnore] public CreatureDef CreatureDef { get; private set; } = null!;

    internal void Resolve(DefDatabase db, MapDef map)
    {
        CreatureDef = db.Creature(Creature);
        if (At.Length != 2 || !map.Inside(At[0], At[1]))
            throw new Exception($"Map '{map.Name}': livestock '{Creature}' needs at: [x, y] on the map");
    }
}

/// <summary>A structure a map puts up later rather than at the start (the festival's lanterns and tables).</summary>
public sealed class MapProp
{
    public string Def { get; set; } = "";

    /// <summary>The column (x, y); it stands on the ground there (or at <see cref="Level"/>).</summary>
    public int[] At { get; set; } = [];
    public int Facing { get; set; }

    /// <summary>The level it stands at, from the map's base (on a deck); -1: on the ground.</summary>
    public int Level { get; set; } = -1;

    [JsonIgnore] public ThingDef ThingDef { get; private set; } = null!;

    internal void Resolve(DefDatabase db, MapDef map)
    {
        ThingDef = db.Thing(Def);
        if (ThingDef.Building == null)
            throw new Exception($"Map '{map.Name}': '{Def}' isn't a structure that can be put up");
        if (At.Length != 2 || !map.Inside(At[0], At[1]))
            throw new Exception($"Map '{map.Name}': '{Def}' needs at: [x, y] on the map");
    }
}

/// <summary>
/// An ore body a map is sure of: <see cref="Block"/> in the rock within about <see cref="Radius"/> of a
/// column, through a range of levels. Its edge is wobbled by the seed's noise (<c>Maps.MapBuilder</c>), so
/// a mine always has its iron but not always in the same cells. Like any ore it passes for the rock round
/// it until a face of it is opened (<c>Systems.Mining.ShownBlock</c>).
/// </summary>
public sealed class MapSeam
{
    public string Block { get; set; } = "";

    /// <summary>The middle column (x, y).</summary>
    public int[] At { get; set; } = [];
    public float Radius { get; set; } = 3;

    /// <summary>The lowest and highest level (from the map's base) it runs through.</summary>
    public int[] Levels { get; set; } = [];

    [JsonIgnore] public BlockDef BlockDef { get; private set; } = null!;

    internal void Resolve(DefDatabase db, MapDef map)
    {
        BlockDef = db.Block(Block);
        if (!BlockDef.Solid || BlockDef.IsWater)
            throw new Exception($"Map '{map.Name}': seam '{Block}' isn't solid rock");
        if (At.Length != 2 || !map.Inside(At[0], At[1]))
            throw new Exception($"Map '{map.Name}': seam '{Block}' needs at: [x, y] on the map");
        if (Radius <= 0)
            throw new Exception($"Map '{map.Name}': seam '{Block}' needs a radius above 0");
        if (Levels.Length != 2 || Levels[0] > Levels[1] || map.BaseZ + Levels[0] < 1 || map.BaseZ + Levels[1] >= map.Depth)
            throw new Exception($"Map '{map.Name}': seam '{Block}' needs levels: [lowest, highest] inside the map's depth");
    }
}

/// <summary>
/// A passage a map carves through the rock at one level: each cell's air, with the ground standing at
/// least two levels higher, so rock is left above it for a roof (a tunnel under open sky is a mistake in the map, not a cutting).
/// </summary>
public sealed class MapTunnel
{
    public string Label { get; set; } = "";

    /// <summary>The level (from the map's base) people walk in it.</summary>
    public int Level { get; set; }

    /// <summary>Its columns, [x, y] each.</summary>
    public List<int[]> Cells { get; set; } = [];

    internal void Resolve(MapDef map)
    {
        var name = Label == "" ? "a tunnel" : $"tunnel '{Label}'";
        if (map.BaseZ + Level < 1)
            throw new Exception($"Map '{map.Name}': {name} is below the bottom of the map");
        foreach (var c in Cells)
        {
            if (c.Length != 2 || !map.Inside(c[0], c[1]))
                throw new Exception($"Map '{map.Name}': {name} has a cell off the map");
            // Ground one level up is only the block the tunnel's cut from: a roof needs one more.
            if (map.Levels[c[1] * map.Width + c[0]] < Level + 2)
                throw new Exception($"Map '{map.Name}': {name} at {c[0]},{c[1]} has no rock above it");
        }
    }
}
