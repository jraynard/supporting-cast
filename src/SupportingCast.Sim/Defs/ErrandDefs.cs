using System.Text.Json.Serialization;

namespace SupportingCast.Sim.Defs;

/// <summary>What an errand asks for. The code decides what completes each kind; the defs set its price and patience.</summary>
public enum ErrandKind
{
    /// <summary>Food, handed to them (the Innkeeper's).</summary>
    Meal,

    /// <summary>A drink, handed to them (the Innkeeper's).</summary>
    Drink,

    /// <summary>A worn weapon or piece of armor mended (the Blacksmith's).</summary>
    Repair,

    /// <summary>Their wounds seen to (the Healer's).</summary>
    Healing,

    /// <summary>Somewhere to go and something to kill (the Innkeeper's, who brokers quests).</summary>
    Quest,

    /// <summary>Anything a player asks of another, in their own words; the poster says when it's done.</summary>
    Request,

    /// <summary>A strongbox brought back from a quest, opened (the Carpenter's).</summary>
    Unlock,
}

/// <summary>
/// How an <see cref="ErrandKind"/> goes: where it's served, what it pays and how long the asker
/// waits before giving up. Named after the kind it describes ("Meal"), one each.
/// </summary>
public sealed class ErrandKindDef : Def
{
    /// <summary>The building it's served at (a building def name); empty for anywhere.</summary>
    public string At { get; set; } = "";

    /// <summary>Silver offered (inclusive range). Players set their own on a request.</summary>
    public int PriceMin { get; set; } = 2;
    public int PriceMax { get; set; } = 5;

    /// <summary>Game hours they wait before giving up (players set their own on a request).</summary>
    public float PatienceHours { get; set; } = 3;

    /// <summary>What adventurers say asking for it, one picked at random. "{0}" is the price, "{1}" the thing in question.</summary>
    public List<string> Lines { get; set; } = [];

    [JsonIgnore] public ErrandKind Kind { get; private set; }
    [JsonIgnore] public BuildingDef? AtDef { get; private set; }

    internal override void Resolve(DefDatabase db)
    {
        if (!Enum.TryParse<ErrandKind>(Name, out var kind))
            throw new Exception($"Errand kind '{Name}' isn't one of {string.Join(", ", Enum.GetNames<ErrandKind>())}");
        Kind = kind;
        AtDef = At.Length > 0 ? db.Building(At) : null;
        if (PriceMin < 0 || PriceMin > PriceMax || PatienceHours <= 0)
            throw new Exception($"Errand kind '{Name}': bad price or patience");
    }
}
