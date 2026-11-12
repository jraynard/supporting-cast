using System.Text.Json.Serialization;

namespace SupportingCast.Sim.Defs;

/// <summary>A service a shop sells to adventurers. The code decides what completes each kind; the defs set its price and patience.</summary>
public enum ServiceKind
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

    /// <summary>A strongbox brought back from a quest, opened (the Tinker's).</summary>
    Unlock,

    /// <summary>A room for the night: a bed let to them upstairs at the inn (the Innkeeper's).</summary>
    Lodging,

    /// <summary>
    /// A piece of gear on show for sale in a shop (<c>Shops.ForSale</c>), at the price its keeper
    /// put on it. Its def's worth is per tier of the piece; it's on no sign.
    /// </summary>
    Goods,

    /// <summary>A named mixed drink, mixed at the bar (the Innkeeper's): dearer than an ale, and they want the one they asked for.</summary>
    MixedDrink,

    /// <summary>Their wagon, come off the road the worse for it, mended where it stands (the Tinker's).</summary>
    WagonRepair,

    /// <summary>A curio brought back from a quest looked over, and its worth told them (the Tinker's).</summary>
    Appraise,
}

/// <summary>
/// How a <see cref="ServiceKind"/> goes: where it's served, what a shop charges for it until its
/// keeper says otherwise, and how long the asker waits before giving up. Named after the kind it
/// describes ("Meal"), one each.
/// </summary>
public sealed class ServiceDef : Def
{
    /// <summary>The building it's served at (a building def name); empty for anywhere.</summary>
    public string At { get; set; } = "";

    /// <summary>
    /// Silver a shop charges until its keeper sets a price of their own (<c>Shops.SetPriceCommand</c>):
    /// what an AI keeper charges.
    /// </summary>
    public int DefaultPrice { get; set; } = 3;

    /// <summary>
    /// What it's worth to an adventurer, in silver (inclusive range, rolled each time they want it,
    /// then scaled by their kind's <see cref="AdventurerKindDef.Spend"/> and how badly they need it).
    /// A shop asking more gets "too dear" and no order.
    /// </summary>
    public int WorthMin { get; set; } = 3;
    public int WorthMax { get; set; } = 6;

    /// <summary>What an adventurer says when the price is more than it's worth to them. "{0}" is the price, "{1}" the thing in question.</summary>
    public List<string> TooDearLines { get; set; } = [];

    /// <summary>Game hours they wait before giving up.</summary>
    public float PatienceHours { get; set; } = 3;

    /// <summary>What adventurers say asking for it, one picked at random. "{0}" is the price, "{1}" the thing in question.</summary>
    public List<string> Lines { get; set; } = [];

    [JsonIgnore] public ServiceKind Kind { get; private set; }
    [JsonIgnore] public BuildingDef? AtDef { get; private set; }

    internal override void Resolve(DefDatabase db)
    {
        if (!Enum.TryParse<ServiceKind>(Name, out var kind))
            throw new Exception($"Service '{Name}' isn't one of {string.Join(", ", Enum.GetNames<ServiceKind>())}");
        Kind = kind;
        AtDef = At.Length > 0 ? db.Building(At) : null;
        if (DefaultPrice < 0 || DefaultPrice > Shops.ShopPrices.MaxPrice || WorthMin < 0 || WorthMin > WorthMax || PatienceHours <= 0)
            throw new Exception($"Service '{Name}': bad price, worth or patience");
    }
}
