using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Shops;

/// <summary>
/// Commissions: so many of a thing wanted, for a price, pinned to the town's notice board in the
/// square. A commission is an <see cref="Order"/> for goods (<see cref="Order.Commission"/>) at the
/// shop that makes the thing, so it's taken, made and handed over as an adventurer's piece asked
/// for by name is (<see cref="ShopSystem.MayTake"/>, <see cref="ShopSystem.Received"/>), and paid
/// when the whole lot's come. Whoever holds the trade that makes it knows of it at once; anyone else
/// by reading the board. Players pin them at the board; an AI tradesperson short of something
/// another shop makes (<see cref="TradeDef.Wants"/>), or a visitor (<c>Director.Incidents</c>), has
/// one sent up.
/// </summary>
public static class Commissions
{
    /// <summary>Open commissions one person may have at once.</summary>
    public const int MaxOpen = 5;

    /// <summary>Most silver a commission may offer.</summary>
    public const int MaxPrice = 999;

    /// <summary>Longest a commission may stay up, in game hours.</summary>
    public const int MaxHours = 24;

    /// <summary>Most of one thing a commission may ask for.</summary>
    public const int MaxCount = 99;

    /// <summary>The def of the notice board.</summary>
    public const string BoardDef = "NoticeBoard";

    /// <summary>The town's notice board (the first, if there are more), or Entity.Null.</summary>
    public static Entity AnyBoard(Simulation sim)
    {
        foreach (var e in sim.World.Query<Thing>())
            if (IsBoard(sim, e))
                return e;
        return Entity.Null;
    }

    /// <summary>The notice board within reach of someone, or Entity.Null.</summary>
    public static Entity BoardBy(Simulation sim, Entity who)
    {
        var here = sim.World.Get<Position>(who).Cell;
        foreach (var c in sim.SpiralFrom(here, 1))
        {
            if (!sim.Map.InBounds(c))
                continue;
            var e = sim.Map.Structure[sim.Map.Idx(c)];
            if (IsBoard(sim, e) && AvatarSystem.InReach(sim, here, e))
                return e;
        }
        return Entity.Null;
    }

    public static bool IsBoard(Simulation sim, Entity e) =>
        !e.IsNull && sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Name == BoardDef;

    /// <summary>
    /// The shops that make a thing (a recipe at one of their finished stations), a shop a player keeps
    /// first, then in the order they were built.
    /// </summary>
    public static List<Entity> MakersOf(Simulation sim, ThingDef def)
    {
        var w = sim.World;
        var makers = new List<Entity>();
        foreach (var shop in w.Query<PresetBuilding>().OrderBy(e => e.Index))
            if (!w.Get<PresetBuilding>(shop).Demolishing && Goods.MadeAt(sim, shop).Contains(def))
                makers.Add(shop);
        return [.. makers.OrderBy(s => Goods.KeptByAPlayer(sim, w.Get<PresetBuilding>(s).Def) ? 0 : 1)];
    }

    /// <summary>Does a player keep a shop that makes this?</summary>
    public static bool MadeByAPlayer(Simulation sim, ThingDef def) =>
        MakersOf(sim, def) is [var first, ..] && Goods.KeptByAPlayer(sim, sim.World.Get<PresetBuilding>(first).Def);

    /// <summary>
    /// What can be commissioned: items some shop in town makes, by name. Not charms: a charm is
    /// asked for by its mending, at the shrine's own counter.
    /// </summary>
    public static List<ThingDef> Commissionable(Simulation sim)
    {
        var w = sim.World;
        var made = new HashSet<ThingDef>();
        foreach (var shop in w.Query<PresetBuilding>().OrderBy(e => e.Index))
            if (!w.Get<PresetBuilding>(shop).Demolishing)
                foreach (var def in Goods.MadeAt(sim, shop))
                    if (def.Category == ThingCategory.Item && !def.Charm)
                        made.Add(def);
        return [.. made.OrderBy(d => d.Label, StringComparer.Ordinal)];
    }

    /// <summary>How a commission reads on the board: "Wanted: 10 planks, 12 silver".</summary>
    public static string Words(ThingDef def, int count, int price) =>
        $"Wanted: {(count > 1 ? $"{count} {def.Label}" : Goods.WithArticle(def.Label))}, {price} silver";

    /// <summary>
    /// Pin a commission to the board: <paramref name="count"/> of a thing, for <paramref name="price"/>
    /// silver, up for <paramref name="hours"/>. You have to be at the board, unless it's sent up on your
    /// behalf (<paramref name="anywhere"/>: an AI at its work, a visitor). Null if nobody in town makes
    /// it, you've too many up, or you're not at the board.
    /// </summary>
    public static Order? Post(Simulation sim, Entity who, ThingDef? def, int count, int price, int hours, bool anywhere = false)
    {
        if (def == null || def.Category != ThingCategory.Item || def.Charm || !sim.World.Has<Creature>(who))
            return null;
        if (sim.Shops.All.Count(e => e.IsOpen && e.Commission && e.Poster == who) >= MaxOpen)
            return null;
        var board = anywhere ? AnyBoard(sim) : BoardBy(sim, who);
        if (board.IsNull)
        {
            if (!anywhere)
                sim.Messages.Add(MessageKind.Info, "Commissions are pinned to the notice board in the square: go to it first.", sim.LocationOf(who));
            return null;
        }
        if (MakersOf(sim, def) is not [var shop, ..])
            return null;
        count = Math.Clamp(count, 1, MaxCount);
        price = Math.Clamp(price, 0, MaxPrice);
        hours = Math.Clamp(hours, 1, MaxHours);
        var order = sim.Shops.Post(sim, who, sim.Defs.Service(ServiceKind.Goods), Words(def, count, price), price,
            sim.Clock.Tick + SimClock.HoursToTicks(hours), where: shop, wants: def, commission: true, count: count);
        sim.Messages.Add(MessageKind.Info, $"{order.PosterName} pins a commission to the notice board.", sim.World.Get<Thing>(board).Cell);
        return order;
    }

    /// <summary>Read the board: every commission up on it is known to you now. How many you hadn't known; -1 if you're not at it.</summary>
    public static int Read(Simulation sim, Entity who)
    {
        if (BoardBy(sim, who).IsNull)
            return -1;
        var learned = 0;
        foreach (var e in sim.Shops.All)
            if (e.IsOpen && e.Commission && !e.KnownBy.Contains(who))
            {
                ShopSystem.Learn(e, who);
                learned++;
            }
        return learned;
    }

    /// <summary>The asker takes their commission down. False if it isn't theirs, or is over.</summary>
    public static bool Cancel(Simulation sim, Entity who, int id)
    {
        if (sim.Shops.Find(id) is not { IsOpen: true, Commission: true } e || e.Poster != who)
            return false;
        sim.Shops.CallOff(sim, e);
        return true;
    }

    /// <summary>
    /// Someone standing at <paramref name="cell"/> who pinned a commission this item fills, that
    /// <paramref name="by"/> may bring to (nobody else is on it): the commission, or null.
    /// </summary>
    public static Order? PosterAt(Simulation sim, int3 cell, Entity by, Entity item)
    {
        var w = sim.World;
        foreach (var e in sim.Shops.All)
            if (e.IsOpen && e.Commission && e.Poster != by && (e.Assignee.IsNull || e.Assignee == by) && w.IsAlive(e.Poster)
                && w.TryGet<Position>(e.Poster, out var at) && at.Cell == cell && Goods.Fills(sim, e, item))
                return e;
        return null;
    }

    /// <summary>
    /// The town's own wants: each hour of a round, a trade held by the AI that's running short of
    /// something another shop makes (planks for the bench, firewood for the hearth) sends a
    /// commission up to the board for it, paid from its own purse, when a player keeps the shop
    /// that makes it (the AI doesn't make to order). One up at a time for each thing.
    /// </summary>
    public static void Wants(Simulation sim)
    {
        var w = sim.World;
        if (AnyBoard(sim).IsNull)
            return;
        foreach (var e in w.Query<Trade>())
        {
            var trade = w.Get<Trade>(e).Def;
            if (trade.Wants.Count == 0 || !trade.HasWorkplace || Players.Steered(sim, e) || w.Get<Creature>(e) is not { Dead: false, Downed: false }
                || w.Has<Adventurer>(e))
                continue;
            var shop = ShopSystem.WorkplaceOf(sim, trade);
            if (shop.IsNull)
                continue;
            foreach (var want in trade.Wants)
            {
                var def = want.ThingDef;
                if (sim.Shops.All.Any(o => o.IsOpen && o.Commission && o.Poster == e && o.Wants == def) || Stock(sim, shop, def) >= want.Below
                    || Coin.Count(sim, e) < want.Price || !MadeByAPlayer(sim, def) || MakersOf(sim, def)[0] == shop)
                    continue;
                Post(sim, e, def, want.Count, want.Price, (int)MathF.Ceiling(want.Hours), anywhere: true);
            }
        }
    }

    /// <summary>How many of a thing are kept at a building: in its chests and baskets, or lying inside it.</summary>
    public static int Stock(Simulation sim, Entity building, ThingDef def)
    {
        var w = sim.World;
        var n = 0;
        foreach (var part in Buildings.Parts(sim, building))
            if (w.Get<Thing>(part).Def.Building?.StorageStacks > 0)
                foreach (var item in sim.StoredIn(part))
                    if (w.Get<Thing>(item).Def == def)
                        n += w.Get<Stack>(item).Count;
        foreach (var item in sim.AvailableItems())
            if (w.Get<Thing>(item).Def == def && w.Get<Thing>(item).Spawned && Buildings.At(sim, w.Get<Thing>(item).Cell) == building)
                n += w.Get<Stack>(item).Count;
        return n;
    }
}

/// <summary>Sends the town's wants up to the notice board each hour of a round (<see cref="Commissions.Wants"/>).</summary>
public sealed class CommissionSystem : ISimSystem
{
    public string Name => "Commissions";
    public int Interval => SimClock.TicksPerHour;

    public void Update(Simulation sim)
    {
        if (sim.Round.Live)
            Commissions.Wants(sim);
    }
}
