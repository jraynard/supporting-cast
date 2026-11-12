using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Shops;

/// <summary>
/// An item its keeper has put up for sale, at a price they named (<see cref="SetItemPriceCommand"/>).
/// It's on show while it lies in a shop they keep: adventurers who'd be better off with it, and can
/// afford it, line up to buy it (<see cref="Goods"/>). The price is posted, like the sign.
/// </summary>
public struct ForSale
{
    public Entity Seller;
    public int Price;
}

/// <summary>
/// Goods for sale: what's on show in the town's shops, and which of it an adventurer would buy. They
/// judge gear by what anyone can see of it (its tier, kind and how well it's made), never by
/// anything hidden in it.
/// </summary>
public static class Goods
{
    /// <summary>
    /// The shop an item's on show in: it's for sale, lying on the map inside a building its seller
    /// keeps. Entity.Null if it isn't on show (taken off the shelf, carried off, the seller gone).
    /// </summary>
    public static Entity ShownIn(Simulation sim, Entity item)
    {
        var w = sim.World;
        if (!w.IsAlive(item) || !w.TryGet<ForSale>(item, out var sale) || !w.TryGet<Thing>(item, out var t) || !t.Spawned
            || w.Has<Entrusted>(item))
            return Entity.Null;
        var shop = Buildings.At(sim, t.Cell);
        return !shop.IsNull && ShopPrices.Keeps(sim, sale.Seller, shop) ? shop : Entity.Null;
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
    /// to be lying in a shop they keep (to put it up) or be theirs on sale (to take it off). False
    /// if nothing changed.
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
        if (!t.Spawned || !IsGear(t.Def) || Buildings.At(sim, t.Cell) is not { IsNull: false } shop || !ShopPrices.Keeps(sim, who, shop))
            return false;
        if (w.Has<ForSale>(item))
            w.Remove<ForSale>(item);
        w.Add(item, new ForSale { Seller = who, Price = Math.Clamp(price, 0, ShopPrices.MaxPrice) });
        return true;
    }

    /// <summary>What adventurers buy: weapons, armor (apparel that turns a blade or a blow), and the empath's charms.</summary>
    public static bool IsGear(ThingDef def) =>
        def.Weapon != null || def.Apparel is { } a && (a.Sharp > 0 || a.Blunt > 0) || def.Charm;

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
        return QualityWorth(sim.World.TryGet<Crafted>(item, out var made) ? made.Quality : Quality.Plain);
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
        if (def.Charm)
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
