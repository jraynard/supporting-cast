using System.Text.Json;
using System.Text.Json.Serialization;

namespace DragonWorld.Sim.Defs;

/// <summary>
/// A rung of the tool ladder (stone, copper, bronze, iron, steel, dragonforged), named so defs can
/// say <c>"tier": "Bronze"</c> and the UI can say "needs a bronze pick" (docs/plans/mining-progression.md).
/// The number is what's compared: a tool of tier N breaks blocks of <see cref="BlockDef.DigTier"/> ≤ N
/// and meets recipes of <see cref="RecipeDef.ToolTier"/> ≤ N.
/// </summary>
public sealed class TierDef : Def
{
    public int Tier { get; set; }
}

/// <summary>
/// A tier as written in a def: a number, or the name of a <see cref="TierDef"/> resolved at load.
/// Reads as its number everywhere else (<c>int</c> conversion).
/// </summary>
[JsonConverter(typeof(TierRefConverter))]
public struct TierRef
{
    public int Value;

    /// <summary>The tier's name as written, until <see cref="DefDatabase.ResolveTier"/> turns it into <see cref="Value"/>.</summary>
    public string? Name;

    public static implicit operator int(TierRef t) => t.Value;

    public static implicit operator TierRef(int value) => new() { Value = value };

    public override readonly string ToString() => Name ?? Value.ToString();
}

internal sealed class TierRefConverter : JsonConverter<TierRef>
{
    public override TierRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.Number => new TierRef { Value = reader.GetInt32() },
        JsonTokenType.String => new TierRef { Name = reader.GetString() },
        _ => throw new JsonException("A tier is a number or a tier name (\"Bronze\")"),
    };

    public override void Write(Utf8JsonWriter writer, TierRef value, JsonSerializerOptions options)
    {
        if (value.Name != null)
            writer.WriteStringValue(value.Name);
        else
            writer.WriteNumberValue(value.Value);
    }
}

/// <summary>
/// Where an ore (or clay) is laid into the ground by <c>MapGenerator</c>: inside which rock, how far
/// below the surface, in blobs of hashed noise. Each vein has its own <see cref="Salt"/>, so adding
/// one never moves another (docs/plans/mining-progression.md, "veins" in data/defs/mining.json).
/// </summary>
public sealed class VeinDef : Def
{
    /// <summary>The block placed.</summary>
    public string Block { get; set; } = "";

    /// <summary>Blocks it may replace (rock kinds, or "Sand" for clay banks).</summary>
    public List<string> Hosts { get; set; } = [];

    /// <summary>Levels below the surface it lies at: 1 is the top block of the column.</summary>
    public int DepthMin { get; set; } = 1;
    public int DepthMax { get; set; } = 4;

    /// <summary>Noise feature size in cells, and how high it must rise for the vein (0..1: higher is rarer).</summary>
    public float Scale { get; set; } = 7f;
    public float Threshold { get; set; } = 0.7f;

    /// <summary>Seed offset for this vein's noise.</summary>
    public uint Salt { get; set; }

    /// <summary>Only in columns at least this damp (clay by the water).</summary>
    public float MoistureMin { get; set; }

    /// <summary>The same blob on every level (a flat bank), instead of shifting with depth (a seam).</summary>
    public bool Flat { get; set; }

    [JsonIgnore] public BlockDef BlockDef { get; private set; } = null!;
    [JsonIgnore] public List<BlockDef> HostDefs { get; } = [];

    internal override void Resolve(DefDatabase db)
    {
        BlockDef = db.Block(Block);
        HostDefs.Clear();
        foreach (var h in Hosts)
            HostDefs.Add(db.Block(h));
    }
}

/// <summary>
/// Something a harvest yields only sometimes: <see cref="Chance"/> per harvest, rolled from a hash
/// of the place (never <c>sim.Rand</c>), so a boulder's ore doesn't shift anything else.
/// </summary>
public sealed class BonusYield
{
    public string Def { get; set; } = "";
    public int Count { get; set; } = 1;
    public float Chance { get; set; }

    [JsonIgnore] public ThingDef ThingDef { get; internal set; } = null!;
}

/// <summary>
/// Bellows beside a station: while a smith works there they pump them, pushing <see cref="Heat"/>
/// more into it (°C·tiles per hour) up to its own cap plus <see cref="MaxTempBonus"/>. How iron and
/// steel are reached without a dragon, on coal.
/// </summary>
public sealed class BellowsProps
{
    public float Heat { get; set; } = 2000;
    public float MaxTempBonus { get; set; } = 140;

    /// <summary>
    /// About how much hotter they hold the station than its fuel's reach (<see cref="FuelProps.Reach"/>),
    /// so a craft can be started that only the bellows bring it to. Checked by a test like the reaches.
    /// </summary>
    public float Lift { get; set; } = 270;
}
