using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Shops;

/// <summary>
/// An item its keeper has put up for sale, at a price they named (<see cref="SetItemPriceCommand"/>).
/// It's on show while it's on a wares shelf in a shop they sell from: adventurers who'd be better off
/// with it, and can afford it, line up to buy it (<see cref="Goods"/>). The price is posted, like the sign.
/// </summary>
public struct ForSale
{
    public Entity Seller;
    public int Price;
}

/// <summary>
/// Goods for sale: what's on show on the town's wares shelves (<see cref="BuildingProps.Display"/>),
/// and which of it an adventurer would buy. A shelf is the one place a keeper sells from, so it's
/// where a player finds selling. They judge gear by what anyone can see of it (its tier, kind and
/// how well it's made), never by anything hidden in it.
/// </summary>
public static class Goods
{
    /// <summary>A wares shelf (finished): what's put on it is for sale.</summary>
    public static bool IsDisplay(Simulation sim, Entity e) =>
        !e.IsNull && sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Building is { Display: true }
        && !sim.World.Has<Construction>(e);

    /// <summary>
    /// The shop this person sells from at a wares shelf: it's part of a shop they keep, and sells for
    /// their trade (the inn's merchandise shelf is the innkeeper's, not the cook's). Entity.Null if not.
    /// </summary>
    public static Entity SellsAt(Simulation sim, Entity who, Entity shelf)
    {
        var w = sim.World;
        if (!IsDisplay(sim, shelf) || !w.TryGet<PartOf>(shelf, out var part) || !ShopPrices.Keeps(sim, who, part.Building))
            return Entity.Null;
        var role = w.Get<Thing>(shelf).Def.Building!.DisplayRole;
        return role == TownRole.None || w.Get<Trade>(who).Def.Role == role ? part.Building : Entity.Null;
    }

    /// <summary>Can this person sell from this shelf?</summary>
    public static bool CanSellAt(Simulation sim, Entity who, Entity shelf) => !SellsAt(sim, who, shelf).IsNull;

    /// <summary>The wares shelf this person sells from in a shop (the first, by entity), or Entity.Null.</summary>
    public static Entity DisplayOf(Simulation sim, Entity shop, Entity seller)
    {
        if (shop.IsNull || !sim.World.IsAlive(shop) || !sim.World.Has<PresetBuilding>(shop))
            return Entity.Null;
        foreach (var part in Buildings.Parts(sim, shop).OrderBy(e => e.Index))
            if (CanSellAt(sim, seller, part))
                return part;
        return Entity.Null;
    }

    /// <summary>The wares shelf this person sells from at their own workplace, or Entity.Null.</summary>
    public static Entity MyDisplay(Simulation sim, Entity seller)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(seller, out var trade) || !trade.Def.HasWorkplace)
            return Entity.Null;
        foreach (var shop in w.Query<PresetBuilding>().OrderBy(e => e.Index))
            if (trade.Def.WorksAt(w.Get<PresetBuilding>(shop).Def) && DisplayOf(sim, shop, seller) is { IsNull: false } shelf)
                return shelf;
        return Entity.Null;
    }

    /// <summary>
    /// The shop an item's on show in: it's for sale, on a wares shelf its seller sells from.
    /// Entity.Null if it isn't on show (taken down, carried off, the seller gone).
    /// </summary>
    public static Entity ShownIn(Simulation sim, Entity item)
    {
        var w = sim.World;
        if (!w.IsAlive(item) || !w.TryGet<ForSale>(item, out var sale) || !w.TryGet<Stored>(item, out var stored)
            || w.Has<Entrusted>(item) || !w.IsAlive(sale.Seller))
            return Entity.Null;
        return SellsAt(sim, sale.Seller, stored.In);
    }

    /// <summary>Everything on show for sale, in a fixed order (entity index).</summary>
    public static List<(Entity Item, Entity Shop, int Price)> OnShow(Simulation sim)
    {
        var list = new List<(Entity, Entity, int)>();
        foreach (var item in sim.World.Query<ForSale>().OrderBy(e => e.Index))
            if (ShownIn(sim, item) is { IsNull: false } shop)
                list.Add((item, shop, sim.World.Get<ForSale>(item).Price));
        return list;
    }

    /// <summary>
    /// The keeper puts an item up for sale at <paramref name="price"/>, or takes it off sale. It has
    /// to be on a wares shelf they sell from (to put it up or change its price) or be theirs on sale
    /// (to take it off). False if nothing changed.
    /// </summary>
    public static bool Set(Simulation sim, Entity who, Entity item, int price, bool forSale)
    {
        var w = sim.World;
        if (!w.IsAlive(item) || !w.TryGet<Thing>(item, out var t) || !w.Has<Stack>(item) || w.Has<Entrusted>(item))
            return false;
        if (!forSale)
        {
            if (!w.TryGet<ForSale>(item, out var old) || old.Seller != who)
                return false;
            w.Remove<ForSale>(item);
            return true;
        }
        if (!IsGear(t.Def) || !w.TryGet<Stored>(item, out var stored) || !CanSellAt(sim, who, stored.In))
            return false;
        if (w.Has<ForSale>(item))
            w.Remove<ForSale>(item);
        w.Add(item, new ForSale { Seller = who, Price = Math.Clamp(price, 0, ShopPrices.MaxPrice) });
        return true;
    }

    /// <summary>
    /// Something's just been put on a wares shelf by <paramref name="who"/>: if it's gear and they sell
    /// from there, it's for sale at once, at its old price if it had one, else <see cref="DefaultPrice"/>.
    /// </summary>
    public static void Shelved(Simulation sim, Entity who, Entity item)
    {
        var w = sim.World;
        if (!w.IsAlive(item) || !w.TryGet<Stored>(item, out var stored) || !CanSellAt(sim, who, stored.In))
            return;
        var price = w.TryGet<ForSale>(item, out var had) ? had.Price : DefaultPrice(sim, item);
        Set(sim, who, item, price, true);
    }

    /// <summary>
    /// Taken off a wares shelf by hand: it's off sale, unless someone's waiting to buy it (the keeper
    /// taking it down to hand it over keeps the sale open).
    /// </summary>
    public static void TakenDown(Simulation sim, Entity item)
    {
        if (sim.World.IsAlive(item) && sim.World.Has<ForSale>(item)
            && !sim.Shops.All.Any(o => o.IsOpen && o.Kind.Kind == ServiceKind.Goods && o.Subject == item))
            sim.World.Remove<ForSale>(item);
    }

    /// <summary>
    /// A fair first asking for a piece: about a third of what a buyer would think a plain one of its
    /// tier worth, by how well it's made or woven, as anyone can see. The keeper changes it as they like.
    /// </summary>
    public static int DefaultPrice(Simulation sim, Entity item)
    {
        var def = sim.Defs.Service(ServiceKind.Goods);
        var tier = Math.Max(1, sim.World.Get<Thing>(item).Def.GearTier);
        var worth = (def.WorthMin + def.WorthMax) * 0.5f * tier * WorthFactor(sim, item);
        return Math.Clamp((int)MathF.Round(worth * DefaultShare), 1, ShopPrices.MaxPrice);
    }

    /// <summary>The share of a piece's worth a shelf asks by default (<see cref="DefaultPrice"/>).</summary>
    public const float DefaultShare = 0.3f;

    /// <summary>What adventurers buy: weapons, armor (apparel that turns a blade or a blow), the empath's charms, the tinker's contraptions, the cook's rations and the carpenter's quivers.</summary>
    public static bool IsGear(ThingDef def) =>
        def.Weapon != null || def.Apparel is { } a && (a.Sharp > 0 || a.Blunt > 0) || def.Charm || def.Contraption != null || def.Rations || def.Quiver;

    /// <summary>Gear carried for the road rather than worn or wielded: stowed in the pack when bought.</summary>
    private static bool ForTheRoad(ThingDef def) => def.Charm || def.Contraption != null || def.Rations || def.Quiver;

    /// <summary>
    /// Would they be better off with this? A weapon of the kind they fight with (in hand or at
    /// range) a tier above theirs, or armor for a slot they've nothing in, or a tier above what's
    /// in it. Judged by what anyone can see.
    /// </summary>
    public static bool IsUpgrade(Simulation sim, Entity who, Entity item)
    {
        var w = sim.World;
        var def = w.Get<Thing>(item).Def;
        if (def.Charm)
        {
            // A ward for the road: a couple, and not two of the same mending.
            var mine = Healing.Charms.Carried(sim, who);
            return w.TryGet<Charm>(item, out var c) && mine.Count < Healing.Charms.MostCarried
                && !mine.Exists(e => w.Get<Charm>(e).Mending == c.Mending);
        }
        if (def.Rations)
            // Food for the road: one bundle, judged by how it's made (it can't be smelt through the cloth).
            return Work.Rations.Carried(sim, who).Count < Work.Rations.MostCarried;
        if (def.Quiver)
            // Arrows for the road: one quiver, and only for someone who shoots.
            return Carpentry.Fletching.Shoots(sim, who) && Carpentry.Fletching.Carried(sim, who).Count < Carpentry.Fletching.MostCarried;
        if (def.Contraption is { } contraption)
        {
            // Kit for the road: a couple, and not two that do the same.
            var mine = Tinkering.Contraptions.Carried(sim, who);
            return mine.Count < Tinkering.Contraptions.MostCarried && !mine.Exists(e => w.Get<Thing>(e).Def.Contraption!.Use == contraption.Use);
        }
        if (def.Weapon is { } attack)
        {
            if (!w.TryGet<Equipment>(who, out var eq) || !w.IsAlive(eq.Weapon))
                return true;
            var mine = w.Get<Thing>(eq.Weapon).Def;
            return (mine.Weapon?.Ranged ?? false) == attack.Ranged && def.GearTier > mine.GearTier;
        }
        if (def.Apparel is not { } apparel || !IsGear(def))
            return false;
        var worn = w.TryGet<Apparel>(who, out var a) ? a[apparel.Slot] : Entity.Null;
        return !w.IsAlive(worn) || def.GearTier > w.Get<Thing>(worn).Def.GearTier;
    }

    /// <summary>
    /// What a piece is worth against a plain one of its tier, by what a buyer can see: how well
    /// it's made, or for a charm the mending in it and how strongly it's woven (not how well).
    /// </summary>
    public static float WorthFactor(Simulation sim, Entity item)
    {
        if (sim.World.TryGet<Charm>(item, out var charm))
            return Healing.Charms.Worth * (1f + 0.5f * charm.Extra);
        var quality = QualityWorth(sim.World.TryGet<Crafted>(item, out var made) ? made.Quality : Quality.Plain);
        var def = sim.World.Get<Thing>(item).Def;
        return def.Contraption != null ? Tinkering.Contraptions.Worth * quality : def.Rations ? Work.Rations.Worth * quality
            : def.Quiver ? Carpentry.Fletching.Worth * quality : quality;
    }

    /// <summary>How much better made pieces are worth, by quality.</summary>
    public static float QualityWorth(Quality q) => q switch
    {
        Quality.Crude => 0.7f,
        Quality.Fine => 1.3f,
        Quality.Masterwork => 1.7f,
        _ => 1f,
    };

    /// <summary>Hand a bought piece to its buyer: they take it up (or put it on), and keep what it replaces in their pack.</summary>
    public static void TakeUp(Simulation sim, Entity who, Entity item)
    {
        var w = sim.World;
        if (w.Has<ForSale>(item))
            w.Remove<ForSale>(item);
        var def = w.Get<Thing>(item).Def;
        if (ForTheRoad(def))
        {
            Inventory.Stow(sim, who, item);
            return;
        }
        if (def.Weapon != null)
        {
            if (w.TryGet<Equipment>(who, out var eq) && w.IsAlive(eq.Weapon))
            {
                var old = eq.Weapon;
                w.Remove<Equipment>(who);
                Inventory.Stow(sim, who, old);
            }
            sim.EquipItem(who, item);
            return;
        }
        var slot = def.Apparel!.Slot;
        if (w.TryGet<Apparel>(who, out var a) && w.IsAlive(a[slot]))
        {
            var old = a[slot];
            w.Get<Apparel>(who)[slot] = Entity.Null;
            Inventory.Stow(sim, who, old);
        }
        sim.WearItem(who, item);
    }
}
