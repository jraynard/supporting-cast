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
    private static readonly Dictionary<string, TownRole> Added = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    /// <summary>
    /// A single role by name ("Kitchen"); empty is <see cref="TownRole.None"/>. A name the game doesn't have
    /// (a mod's workplace, "Glassworks") is a role of its own, given the next free bit the first time it's
    /// named: roles are only ever read from defs by name, so the bit never travels.
    /// </summary>
    public static TownRole Parse(string name, string where)
    {
        if (name.Length == 0)
            return TownRole.None;
        if (Enum.TryParse<TownRole>(name, true, out var r) && r != TownRole.None && (r & (r - 1)) == 0 && Enum.IsDefined(r))
            return r;
        if (!name.All(c => char.IsLetterOrDigit(c) || c == '_'))
            throw new Exception($"{where}: '{name}' isn't a town role's name ({string.Join(", ", Enum.GetNames<TownRole>().Skip(1))}, or a word of your own)");
        lock (Gate)
        {
            if (Added.TryGetValue(name, out var added))
                return added;
            var bit = BitsUsed + Added.Count;
            if (bit >= 31)
                throw new Exception($"{where}: too many town roles (no room for '{name}')");
            return Added[name] = (TownRole)(1 << bit);
        }
    }

    /// <summary>Bits the game's own roles take.</summary>
    private static readonly int BitsUsed = Enum.GetValues<TownRole>().Select(v => (int)v).Where(v => v > 0).Select(v => 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)v)).DefaultIfEmpty(0).Max();

    /// <summary>A role's name: the game's own, or a mod's as it was first named.</summary>
    public static string NameOf(TownRole role) =>
        Enum.IsDefined(role) ? role.ToString() : Added.FirstOrDefault(kv => kv.Value == role).Key ?? role.ToString();
}

/// <summary>Reads a role in defs by name, a mod's own included (<see cref="TownRoles.Parse"/>).</summary>
internal sealed class TownRoleJsonConverter : System.Text.Json.Serialization.JsonConverter<TownRole>
{
    public override TownRole Read(ref System.Text.Json.Utf8JsonReader reader, Type type, System.Text.Json.JsonSerializerOptions options) =>
        TownRoles.Parse(reader.GetString() ?? "", "a role");

    public override void Write(System.Text.Json.Utf8JsonWriter writer, TownRole value, System.Text.Json.JsonSerializerOptions options) =>
        writer.WriteStringValue(TownRoles.NameOf(value));
}
