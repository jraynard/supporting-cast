using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Shops;

/// <summary>
/// The price sign at a shop, on its <see cref="PresetBuilding"/>: what its keeper charges for each
/// service the building serves, and whether they're offering it at all. The keeper sets it, not
/// the customer, so price is the keeper's lever: too dear and nobody comes. It's posted for anyone
/// to read (<c>PerceptionView.Prices</c>). A shop without one charges the defs' defaults
/// (<see cref="ServiceDef.DefaultPrice"/>), as an AI keeper does.
/// </summary>
public sealed class ShopPrices
{
    /// <summary>The most silver a sign may ask.</summary>
    public const int MaxPrice = 999;

    /// <summary>Services on a sign: the game's and the mods' (<see cref="DefDatabase.ServiceKindCount"/>).</summary>
    public int Count => Price.Length;

    /// <summary>Silver asked, by <see cref="ServiceKind"/>.</summary>
    public readonly int[] Price;

    /// <summary>Whether the shop is offering it, by <see cref="ServiceKind"/>.</summary>
    public readonly bool[] Offered;

    private ShopPrices(int count) => (Price, Offered) = (new int[count], new bool[count]);

    /// <summary>A sign with every service at its default price, offered.</summary>
    public static ShopPrices Defaults(DefDatabase defs)
    {
        var p = new ShopPrices(defs.ServiceKindCount);
        foreach (var d in defs.AllServices)
        {
            p.Price[(int)d.Kind] = d.DefaultPrice;
            p.Offered[(int)d.Kind] = true;
        }
        return p;
    }

    /// <summary>
    /// The services a building serves (those whose <see cref="ServiceDef.AtRole"/> it has), in def
    /// order. The inn's hampers only at a cookhouse apart from the inn (<see cref="Hospitality.Provisions"/>).
    /// </summary>
    public static IEnumerable<ServiceDef> ServicesAt(Simulation sim, Entity building)
    {
        if (!sim.World.IsAlive(building) || !sim.World.TryGet<PresetBuilding>(building, out var b))
            yield break;
        foreach (var d in sim.Defs.AllServices)
            if (b.Def.Has(d.AtRole) && (d.Kind != ServiceKind.Provisions || building == Hospitality.Provisions.Cookhouse(sim)))
                yield return d;
    }

    /// <summary>What the sign says for one service; null if the building doesn't serve it.</summary>
    public static PostedPrice? Posted(Simulation sim, Entity building, ServiceKind kind)
    {
        foreach (var d in ServicesAt(sim, building))
            if (d.Kind == kind)
                return Read(sim, building, d);
        return null;
    }

    /// <summary>The whole sign: every service the building serves, in def order.</summary>
    public static List<PostedPrice> Board(Simulation sim, Entity building) =>
        ServicesAt(sim, building).Select(d => Read(sim, building, d)).ToList();

    private static PostedPrice Read(Simulation sim, Entity building, ServiceDef d)
    {
        var k = (int)d.Kind;
        return sim.World.TryGet<ShopPrices>(building, out var p)
            ? new PostedPrice(d.Kind, d.Label, p.Price[k], p.Offered[k])
            : new PostedPrice(d.Kind, d.Label, d.DefaultPrice, true);
    }

    /// <summary>Does this person keep the shop: their trade works from a building of its kind?</summary>
    public static bool Keeps(Simulation sim, Entity who, Entity building) =>
        sim.World.IsAlive(building) && sim.World.TryGet<PresetBuilding>(building, out var b)
        && sim.World.TryGet<Trade>(who, out var trade) && trade.Def.WorksAt(b.Def);

    /// <summary>
    /// The keeper changes the sign. False (and nothing changes) unless they keep the shop, it serves
    /// the service, and it's their trade's to see to (the cook prices the meals, not the drinks). The price is clamped to 0..<see cref="MaxPrice"/>.
    /// </summary>
    public static bool Set(Simulation sim, Entity who, Entity building, ServiceKind kind, int price, bool offered)
    {
        if (!Enum.IsDefined(kind) || !Keeps(sim, who, building) || Posted(sim, building, kind) == null
            || !sim.World.Get<Trade>(who).Def.Serving(kind))
            return false;
        var w = sim.World;
        if (!w.Has<ShopPrices>(building))
            w.Add(building, Defaults(sim.Defs));
        var p = w.Get<ShopPrices>(building);
        p.Price[(int)kind] = Math.Clamp(price, 0, MaxPrice);
        p.Offered[(int)kind] = offered;
        return true;
    }
}

/// <summary>One line of a shop's sign, as anyone reading it sees it.</summary>
public readonly record struct PostedPrice(ServiceKind Kind, string Label, int Price, bool Offered);
