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
/// entry <c>"Name@L"</c> stands at level <c>L</c> rather than on the ground: a bridge over a river.</item>
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

    /// <summary>Top block per column (y * Width + x).</summary>
    [JsonIgnore] public BlockDef[] Ground { get; private set; } = [];

    /// <summary>Level per column, from <see cref="BaseZ"/>.</summary>
    [JsonIgnore] public int[] Levels { get; private set; } = [];

    /// <summary>Feature per column, and the fixed level it stands at (-1: on the ground).</summary>
    [JsonIgnore] public MapFeature?[] Features { get; private set; } = [];

    /// <summary>The surface z people stand on in a column.</summary>
    public int StandZ(int x, int y) => BaseZ + Levels[y * Width + x];

    internal override void Resolve(DefDatabase db)
    {
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
            var locked = value.EndsWith('!');
            var v = locked ? value[..^1] : value;
            var at = v.IndexOf('@');
            var level = at < 0 ? -1 : int.Parse(v[(at + 1)..]);
            var def = db.Thing(at < 0 ? v : v[..at]);
            if (locked && def.Building is not { IsDoor: true, Lockable: true })
                throw new Exception($"Map '{Name}': '{def.Name}' can't be locked ('!'): it isn't a lockable door");
            features[Key(k, "feature legend")] = new MapFeature(def, level, locked);
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
        foreach (var bld in Buildings)
            bld.Resolve(db, this);
        foreach (var l in Livestock)
            l.Resolve(db, this);
        foreach (var p in Festival)
            p.Resolve(db, this);
    }

    internal bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

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

/// <summary>A structure a map places: its def, a fixed level (-1: on the ground), and whether it's locked (a gate).</summary>
public readonly record struct MapFeature(ThingDef Def, int Level, bool Locked);

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

    /// <summary>The column (x, y); it stands on the ground there.</summary>
    public int[] At { get; set; } = [];
    public int Facing { get; set; }

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
