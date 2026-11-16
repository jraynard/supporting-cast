namespace HeroesNotIncluded.Sim.Defs;

/// <summary>
/// What a building is to the town: where its trades work and its services are served. Trades
/// (<see cref="TradeDef.Role"/>), services (<see cref="ServiceDef.AtRole"/>) and parties
/// (<see cref="PartyDef.HauntRole"/>) name a role rather than a building, so a map decides who
/// shares a roof: Millbrook's inn is both the tavern and the kitchen, while a town with a
/// cookhouse of its own gives the cook a building apart from the innkeeper's.
/// </summary>
[Flags]
public enum TownRole
{
    None = 0,

    /// <summary>The bar, the rooms and the quest board: the innkeeper's, and where parties loiter.</summary>
    Tavern = 1 << 0,

    /// <summary>The kitchen and its serving hatch: the cook's.</summary>
    Kitchen = 1 << 1,

    Forge = 1 << 2,
    Shrine = 1 << 3,
    TinkerShop = 1 << 4,
    Woodshop = 1 << 5,
}

public static class TownRoles
{
    /// <summary>A single role by name ("Kitchen"); empty is <see cref="TownRole.None"/>.</summary>
    public static TownRole Parse(string name, string where) =>
        name.Length == 0 ? TownRole.None
        : Enum.TryParse<TownRole>(name, out var r) && r != TownRole.None && (r & (r - 1)) == 0 ? r
        : throw new Exception($"{where}: '{name}' isn't a town role ({string.Join(", ", Enum.GetNames<TownRole>().Skip(1))})");
}
