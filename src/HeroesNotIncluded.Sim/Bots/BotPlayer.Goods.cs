using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Bots;

/// <summary>
/// Gear made for sale and put on show in the shop, and sold to whoever asks: the smith's blades,
/// the empath's charms, the tinker's contraptions. A player keeps a few pieces out, prices each by
/// what it looks to be worth (and their greed), marks it down when it sits or is called too dear,
/// and carries it to whoever says they'll take it. Only what's of its own making, and only what
/// anyone can see of it: never a blade's hidden flaw or a contraption's binding.
/// </summary>
public sealed partial class BotPlayer
{
    /// <summary>Pieces of its own it keeps on show at once: a small stock, not a flooded shop. A greedy bot keeps one more.</summary>
    public const int StockOnShow = 2;

    /// <summary>Game hours a piece sits unsold before it comes down a little.</summary>
    public const float MarkDownHours = 4;

    /// <summary>What a markdown keeps of the price, and the least it'll come down to (a share of its first asking).</summary>
    public const float MarkDown = 0.85f, PriceFloor = 0.45f;

    private int StockWanted => StockOnShow + (Temper.Greed >= 0.7f ? 1 : 0);

    // Each piece it's put out: what it first asked, and when it last priced it.
    private readonly Dictionary<Entity, (int First, long Since)> _asked = [];

    // The piece it's just set down in the shop, to price once it's lying there.
    private Entity _placing = Entity.Null;
    private long _goodsHeardUpTo;

    /// <summary>One step of selling a piece on show to who asked for it: down off the shelf, over to them, into their hands. False when it's over.</summary>
    private bool Sell(Simulation sim, Entity me, PerceptionView view, Entity buyer, long now)
    {
        var w = sim.World;
        if (view.Orders().FirstOrDefault(o => o.Id == _task) is not { Status: OrderStatus.Open } order
            || GoodsFor(sim, me, order) is not { IsNull: false } item)
            return false;
        var label = w.Get<Thing>(item).Label;
        if (w.Get<Thing>(item).Spawned)
        {
            Doing = $"taking down the {label} for {order.PosterName}";
            // A hand free to take it, if the pack hasn't room: what's in it goes in the pack, or down.
            if (w.Get<Carrying>(me).Item is { IsNull: false } held && sim.PackOf(me) is var pack
                && (pack.IsNull || sim.ContainerRoom(pack, w.Get<Thing>(item).Def, w.Get<Thing>(item).Material) <= 0))
            {
                if (!pack.IsNull && sim.ContainerRoom(pack, w.Get<Thing>(held).Def, w.Get<Thing>(held).Material) > 0)
                    Send(new StowCommand(me));
                else
                    Send(new AvatarDropCommand(me, w.Get<Position>(me).Cell));
                return true;
            }
            var cell = w.Get<Thing>(item).Cell;
            if (AvatarSystem.InReach(w.Get<Position>(me).Cell, cell))
                Use(me, cell);
            else
                Walk(sim, me, cell, now);
            return true;
        }
        // Picked up into an empty hand, a weapon goes straight on: off again, to hand over.
        if (w.TryGet<Equipment>(me, out var eq) && eq.Weapon == item)
        {
            Send(new UnequipCommand(me, GearSlot.Weapon));
            return true;
        }
        Doing = $"selling {order.PosterName} the {label}";
        if (Reach(sim, me, buyer, now, returning: true))
            Send(new GiveCommand(me, buyer, item, 1));
        return true;
    }

    /// <summary>
    /// The piece of its own a goods order is for: the one on show (or on it, on its way over) whose
    /// name and price the buyer said. No two alike share a price (<see cref="Clashes"/>), so that's never in doubt.
    /// </summary>
    private static Entity GoodsFor(Simulation sim, Entity me, KnownOrder order)
    {
        var w = sim.World;
        foreach (var item in OnSale(sim, me))
            if (w.Get<ForSale>(item).Price == order.Price && order.Text.Contains(w.Get<Thing>(item).Label, StringComparison.Ordinal))
                return item;
        return Entity.Null;
    }

    /// <summary>Everything it has up for sale: on show in its shop, or on it (taken down to hand over), in a fixed order.</summary>
    private static List<Entity> OnSale(Simulation sim, Entity me)
    {
        var w = sim.World;
        var list = Goods.OnShow(sim).Where(g => w.Get<ForSale>(g.Item).Seller == me).Select(g => g.Item).ToList();
        foreach (var item in Loose(sim, me))
            if (w.TryGet<ForSale>(item, out var s) && s.Seller == me)
                list.Add(item);
        if (w.TryGet<Equipment>(me, out var eq) && w.IsAlive(eq.Weapon) && w.TryGet<ForSale>(eq.Weapon, out var sale) && sale.Seller == me)
            list.Add(eq.Weapon);
        return list;
    }

    /// <summary>What's loose on it: in hand, then in the pack.</summary>
    private static IEnumerable<Entity> Loose(Simulation sim, Entity me)
    {
        var w = sim.World;
        if (w.Get<Carrying>(me).Item is { IsNull: false } held && w.IsAlive(held))
            yield return held;
        if (sim.PackOf(me) is { IsNull: false } pack)
            foreach (var item in sim.StoredIn(pack))
                yield return item;
    }

    /// <summary>Of its own making: forged, fitted or woven by this character (a maker knows their own work).</summary>
    private static bool MadeBy(Simulation sim, Entity item, Entity me)
    {
        var w = sim.World;
        return (w.TryGet<Crafted>(item, out var c) && c.Maker == me) || (w.TryGet<Charm>(item, out var ch) && ch.Maker == me);
    }

    /// <summary>A piece on it ready to go out: gear of its own making, nobody else's, not the town's.</summary>
    private static bool ForTheShelf(Simulation sim, Entity me, Entity item)
    {
        var w = sim.World;
        return w.IsAlive(item) && Goods.IsGear(w.Get<Thing>(item).Def) && MadeBy(sim, item, me) && !w.Has<Entrusted>(item)
            && !w.Has<Militia.TownArms>(item) && (!w.TryGet<ForSale>(item, out var s) || s.Seller == me);
    }

    /// <summary>Pieces in its stock: on show, set down off sale for now, and made and waiting on it to go out.</summary>
    private int Stock(Simulation sim, Entity me)
    {
        var w = sim.World;
        return Goods.OnShow(sim).Count(g => w.Get<ForSale>(g.Item).Seller == me) + Loose(sim, me).Count(i => ForTheShelf(sim, me, i))
            + _asked.Keys.Count(e => w.IsAlive(e) && w.Get<Thing>(e).Spawned && !w.Has<ForSale>(e) && MadeBy(sim, e, me));
    }

    /// <summary>
    /// The shop's stock seen to: a piece just set down is priced; a piece made and on it goes out
    /// while there's room on show; one that's sat unsold, or that it heard called too dear, comes
    /// down a little. True while at it.
    /// </summary>
    private bool ShowGoods(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        if (MyShop(sim, me) is not { IsNull: false } shop)
            return false;
        Haggled(sim, me, view);
        if (FreshInTown(sim, view, now))
            return Shutters(sim, me, shop);
        if (_placing is { IsNull: false } put)
        {
            if (!w.IsAlive(put) || !w.Get<Thing>(put).Spawned || w.Has<ForSale>(put))
                _placing = Entity.Null; // priced, or picked back up
            else if (Buildings.At(sim, w.Get<Thing>(put).Cell) == shop)
            {
                _placing = Entity.Null;
                var ask = Asking(sim, put);
                while (Clashes(sim, me, put, ask))
                    ask++;
                _asked[put] = (ask, now);
                Send(new SetItemPriceCommand(me, put, ask, true));
                Doing = $"pricing the {w.Get<Thing>(put).Label} at {ask}";
                if (now >= _nextTalk)
                    Say(sim, me, "on_show", Volume.Say, now, thing: w.Get<Thing>(put).Label);
                return true;
            }
            else
            {
                // It landed outside the shop: back up with it.
                var at = w.Get<Thing>(put).Cell;
                if (AvatarSystem.InReach(w.Get<Position>(me).Cell, at))
                    Use(me, at);
                else
                    Walk(sim, me, at, now);
                return true;
            }
        }
        // Marked down once it's sat a while.
        foreach (var (item, _, price) in Goods.OnShow(sim))
        {
            if (w.Get<ForSale>(item).Seller != me || sim.Shops.All.Any(o => o.IsOpen && o.Subject == item))
                continue;
            var (first, since) = _asked.TryGetValue(item, out var a) ? a : (price, now);
            if (!_asked.ContainsKey(item))
                _asked[item] = (first, since);
            if (now - since >= SimClock.HoursToTicks(MarkDownHours) && Cheaper(price, first) is var lower && lower < price && !Clashes(sim, me, item, lower))
            {
                _asked[item] = (first, now);
                Send(new SetItemPriceCommand(me, item, lower, true));
                Doing = $"marking the {w.Get<Thing>(item).Label} down to {lower}";
                return true;
            }
        }
        // Pieces it took off sale while the town was busy with newcomers go back up at what they were.
        foreach (var e in _asked.Keys.OrderBy(e => e.Index).ToList())
            if (w.IsAlive(e) && !w.Has<ForSale>(e) && w.Get<Thing>(e).Spawned && Buildings.At(sim, w.Get<Thing>(e).Cell) == shop && MadeBy(sim, e, me))
            {
                var (first, _) = _asked[e];
                var ask = _offSale.GetValueOrDefault(e, first);
                while (Clashes(sim, me, e, ask))
                    ask++;
                _asked[e] = (first, now);
                Send(new SetItemPriceCommand(me, e, ask, true));
                Doing = $"putting the {w.Get<Thing>(e).Label} back on sale";
                return true;
            }
        if (Goods.OnShow(sim).Count(g => w.Get<ForSale>(g.Item).Seller == me) >= StockWanted)
            return false;
        var piece = Loose(sim, me).FirstOrDefault(i => ForTheShelf(sim, me, i) && Showable(sim, me, view, i), Entity.Null);
        if (piece.IsNull || DisplayCell(sim, me, shop) is not { } spot)
            return false;
        Doing = $"putting the {w.Get<Thing>(piece).Label} on show";
        if (!AvatarSystem.InReach(w.Get<Position>(me).Cell, spot))
        {
            Walk(sim, me, spot, now);
            return true;
        }
        if (w.Get<Carrying>(me).Item != piece)
            Send(new HoldCommand(me, piece));
        Send(new AvatarDropCommand(me, spot));
        _placing = piece;
        return true;
    }

    /// <summary>
    /// Hours a party's been in town, by when it first saw them, before it reckons they've had the
    /// chance to see the innkeeper about work: past the hours they take to ask for a quest.
    /// </summary>
    public const float FreshHours = Shops.ShopSystem.QuestAfterHours + 2;

    /// <summary>Hours since it last saw an adventurer that it still reckons them about town.</summary>
    public const float SeenLatelyHours = 3;

    // The town's news read so far, and when it last told of someone riding in (none yet: wares are kept back).
    private long _logSeen, _lastArrival = long.MaxValue / 4;

    // Adventurers it's seen: when first, and when last.
    private readonly Dictionary<Entity, (long First, long Last)> _seenAbout = [];

    // What its pieces were at when it took them off sale.
    private readonly Dictionary<Entity, int> _offSale = [];

    /// <summary>
    /// Gear's kept off sale while there are newcomers in town: a newcomer's silver is for a quest
    /// first (the party's work is the town's living), so a keeper lets them get to the innkeeper
    /// before putting gear in front of them. It knows of newcomers by the town's news (a party
    /// riding in is in everyone's log) and by adventurers it's seen; before the season's first
    /// party has settled in, it keeps its wares back. True to keep them off.
    /// </summary>
    private bool FreshInTown(Simulation sim, PerceptionView view, long now)
    {
        var log = sim.Messages;
        for (var i = log.IndexAfter(_logSeen); i < log.Messages.Count; i++)
            if (log.Messages[i] is { To: null } m && Arrival(m.Text))
                _lastArrival = m.Tick;
        _logSeen = log.Total;
        if (now - _lastArrival < SimClock.HoursToTicks(FreshHours))
            return true;
        foreach (var e in view.Creatures())
            if (sim.World.TryGet<Adventurer>(e, out var a) && !a.PartyDef.Traveller)
                _seenAbout[e] = _seenAbout.TryGetValue(e, out var s) ? (s.First, now) : (now, now);
        foreach (var (first, last) in _seenAbout.Values)
            if (now - last < SimClock.HoursToTicks(SeenLatelyHours) && now - first < SimClock.HoursToTicks(FreshHours))
                return true;
        return false;
    }

    /// <summary>A line in the town's news saying a party has ridden in (<c>PartySystem</c>'s words; a lone traveller isn't after a quest).</summary>
    private static bool Arrival(string text) => text.Contains(" ride in from the ", StringComparison.Ordinal);

    /// <summary>Its pieces off sale (they stay where they lie), bar any someone's already asked for. True while at it.</summary>
    private bool Shutters(Simulation sim, Entity me, Entity shop)
    {
        var w = sim.World;
        foreach (var (item, _, price) in Goods.OnShow(sim))
        {
            if (w.Get<ForSale>(item).Seller != me || sim.Shops.All.Any(o => o.IsOpen && o.Subject == item))
                continue;
            _offSale[item] = price;
            if (!_asked.ContainsKey(item))
                _asked[item] = (price, sim.Clock.Tick);
            Send(new SetItemPriceCommand(me, item, 0, false));
            Doing = $"taking the {w.Get<Thing>(item).Label} off sale for now";
            return true;
        }
        return false;
    }

    /// <summary>
    /// Would this price make the piece sound like another of its own on show? A buyer names a piece
    /// by what it is and what it costs ("that charm, 12 silver"), so no two alike share a price.
    /// </summary>
    private static bool Clashes(Simulation sim, Entity me, Entity item, int price)
    {
        var w = sim.World;
        var label = w.Get<Thing>(item).Label;
        return OnSale(sim, me).Exists(e => e != item && w.Get<ForSale>(e).Price == price && w.Get<Thing>(e).Label == label);
    }

    /// <summary>Whether it'd put this piece out: a charm it knows went loose isn't sold (it's torn).</summary>
    private static bool Showable(Simulation sim, Entity me, PerceptionView view, Entity item) =>
        view.Charm(item) is not { Quality: < WeakCharm };

    /// <summary>A markdown from this price, no lower than the floor under its first asking.</summary>
    private static int Cheaper(int price, int first) =>
        Math.Max(Math.Max(1, (int)MathF.Round(first * PriceFloor)), Math.Min(price - 1, (int)MathF.Round(price * MarkDown)));

    /// <summary>
    /// What it asks for a piece: what a buyer would think a plain one of its tier worth (the middle
    /// of the market's range), by how well it's made or woven, as anyone can see, and its greed.
    /// </summary>
    private int Asking(Simulation sim, Entity item)
    {
        var def = sim.Defs.Service(ServiceKind.Goods);
        var tier = Math.Max(1, sim.World.Get<Thing>(item).Def.GearTier);
        var worth = (def.WorthMin + def.WorthMax) * 0.5f * tier * Goods.WorthFactor(sim, item);
        return Math.Clamp((int)MathF.Round(worth * (0.15f + 0.35f * Temper.Greed)), 1, ShopPrices.MaxPrice);
    }

    /// <summary>
    /// A cell in the shop to set a piece down on: clear floor it can reach, near the front of the
    /// line where buyers stand, but off it, out of doorways and away from the heat of a forge.
    /// </summary>
    private static int3? DisplayCell(Simulation sim, Entity me, Entity shop)
    {
        var w = sim.World;
        var map = sim.Map;
        var line = ShopSystem.LineCells(sim, shop);
        var here = w.Get<Position>(me).Cell;
        var from = line.Count > 0 ? line[0] : w.Get<PresetBuilding>(shop).Anchor;
        var hot = w.Get<Creature>(me).Tolerance(sim.Clock.Tick).SafeMax - 12;
        foreach (var c in sim.SpiralFrom(from, 10))
        {
            if (!map.InBounds(c) || Buildings.At(sim, c) != shop || !map.Passable(c) || line.Contains(c) || Environment.Warmth.FeltAt(sim, c) > hot)
                continue;
            var i = map.Idx(c);
            if (!map.Item[i].IsNull || !map.Structure[i].IsNull || !sim.Regions.CanReach(here, c, PathEnd.Touch))
                continue;
            return c;
        }
        return null;
    }

    /// <summary>
    /// What it's heard said of its pieces on show: called too dear by someone who wanted it, it comes
    /// down a step (a greedy one holds out against some of it), as the sign does.
    /// </summary>
    private void Haggled(Simulation sim, Entity me, PerceptionView view)
    {
        var w = sim.World;
        var def = sim.Defs.Service(ServiceKind.Goods);
        var mine = Goods.OnShow(sim).Where(g => w.Get<ForSale>(g.Item).Seller == me).ToList();
        foreach (var line in view.Heard())
        {
            if (line.Id <= _goodsHeardUpTo)
                continue;
            _goodsHeardUpTo = line.Id;
            if (line.Speaker is not { } who || who == me || line.Clarity != Clarity.Clear || !w.Has<Adventurer>(who))
                continue;
            foreach (var (item, _, price) in mine)
            {
                if (!line.Text.Contains(w.Get<Thing>(item).Label, StringComparison.Ordinal) || !TooDear(def, price, line.Text))
                    continue;
                if (_slipRand.Chance(Temper.Greed * 0.6f))
                    continue;
                var first = _asked.TryGetValue(item, out var a) ? a.First : price;
                if (Cheaper(price, first) is var lower && lower < price && !Clashes(sim, me, item, lower))
                {
                    _asked[item] = (first, sim.Clock.Tick);
                    Send(new SetItemPriceCommand(me, item, lower, true));
                }
            }
        }
    }
}
