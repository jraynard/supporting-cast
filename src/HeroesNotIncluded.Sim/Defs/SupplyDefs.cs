using System.Text.Json.Serialization;

namespace HeroesNotIncluded.Sim.Defs;

/// <summary>
/// Something the carter brings to order (data/defs/supplies.json): so many of a thing a lot, for so
/// much silver, to the workplace of one role, into its container of a kind, at most so many lots a
/// morning. The tradesman orders it (<c>Shops.CarterOrders</c>), and a shop the AI runs orders it by
/// its trade's <see cref="TradeDef.StandingOrders"/>. A def, so a mod's trade brings its own supplies.
/// </summary>
public sealed class SupplyDef : Def
{
    /// <summary>The thing brought.</summary>
    public string Def { get; set; } = "";

    /// <summary>Optional material it comes in (pine planks).</summary>
    public string Material { get; set; } = "";

    /// <summary>How many come in a lot.</summary>
    public int Per { get; set; } = 1;

    /// <summary>Silver a lot.</summary>
    public int Price { get; set; } = 1;

    /// <summary>The most lots the carter brings of it a morning.</summary>
    public int Max { get; set; } = 6;

    /// <summary>The town role (<see cref="TownRole"/>) of the workplace that orders it: "Forge".</summary>
    public string For { get; set; } = "";

    /// <summary>The container it's left in at that workplace, by def name: "Chest" unless said ("Larder", "LogBasket").</summary>
    public string Into { get; set; } = "Chest";

    /// <summary>Not to be had in a shortage (firewood, food): the carter comes without it.</summary>
    public bool Scarce { get; set; }

    [JsonIgnore] public ThingDef ThingDef { get; private set; } = null!;
    [JsonIgnore] public MaterialDef? MaterialDef { get; private set; }
    [JsonIgnore] public TownRole ForRole { get; private set; }

    internal override void Resolve(DefDatabase db)
    {
        ThingDef = db.Thing(Def);
        MaterialDef = string.IsNullOrEmpty(Material) ? null : db.Material(Material);
        ForRole = TownRoles.Parse(For, $"Supply '{Name}' for");
        if (ForRole == TownRole.None || Per < 1 || Price < 0 || Max < 1 || string.IsNullOrWhiteSpace(Into))
            throw new Exception($"Supply '{Name}': it needs a workplace role, a lot of one or more, a price and a most of one or more");
        if (string.IsNullOrEmpty(Label) || Label == Name)
            Label = MaterialDef != null ? $"{MaterialDef.Label} {ThingDef.Label}" : ThingDef.Label;
    }
}

/// <summary>What a shop the AI runs keeps in of a supply (<see cref="TradeDef.StandingOrders"/>): topped up to <see cref="Keep"/> each evening.</summary>
public sealed class StandingOrder
{
    public string Supply { get; set; } = "";
    public int Keep { get; set; }

    [JsonIgnore] public SupplyDef SupplyDef { get; private set; } = null!;

    internal void Resolve(DefDatabase db, string trade)
    {
        SupplyDef = db.Supply(Supply);
        if (Keep < 1)
            throw new Exception($"Trade '{trade}': a standing order for {Supply} keeps none");
    }
}
