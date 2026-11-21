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

    public int StockWanted => StockOnShow + (Temper.Greed >= 0.7f ? 1 : 0);

    // Each piece it's put out: what it first asked, and when it last priced it.
    private readonly Dictionary<Entity, (int First, long Since)> _asked = [];

    // The piece it's just put on its wares shelf, to price once it's there.
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
        if (w.TryGet<Stored>(item, out var onShelf) && Goods.IsDisplay(sim, onShelf.In))
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
            var cell = w.Get<Thing>(onShelf.In).Cell;
            if (Inventory.CanReachContainer(sim, me, onShelf.In))
                Send(new TakeFromStorageCommand(me, item));
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

    /// <summary>
    /// A piece someone asked this shop to make, by name (<see cref="Order.Wants"/>), as it heard it said: open,
    /// asked of its own shop, and not yet made (nothing of its own on show or on it fills it). The oldest first.
    /// A commission off the notice board too, nobody else on it, while it hasn't the lot to hand (<see cref="ToHand"/>).
    /// Not while a customer waits on the trade's own work (a mend, a lock): they come first, as at the shelf.
    /// </summary>
    public static KnownOrder? Asked(Simulation sim, Entity me, PerceptionView view)
    {
        if (MyShop(sim, me) is not { IsNull: false } shop || !sim.World.TryGet<Trade>(me, out var trade))
            return null;
        var serves = KindsFor(sim, trade.Def);
        if (view.Orders().Any(o => o.Status == OrderStatus.Open && !o.Mine && o.Kind is not (ServiceKind.Goods or ServiceKind.Quest) && serves.Contains(o.Kind)))
            return null;
        foreach (var o in view.Orders())
            if (o is { Kind: ServiceKind.Goods, Status: OrderStatus.Open, Wants: { } wants } && o.Where == shop
                && (o.Commission ? !SomeoneElsesTo(o) && ToHand(sim, me, shop, wants) < o.Left
                    : !OnSale(sim, me).Concat(Loose(sim, me)).Any(i => FillsAsk(sim, view, o, i) && (MadeBy(sim, i, me) || sim.World.Has<ForSale>(i)))))
                return o;
        return null;
    }

    /// <summary>A commission somebody else said they'd see to.</summary>
    private static bool SomeoneElsesTo(KnownOrder o) => o.AssigneeName.Length > 0 && !o.MineToDo;

    /// <summary>How many of a thing it has to hand for a commission: on it, and about its shop (its chests, or lying there).</summary>
    public static int ToHand(Simulation sim, Entity me, Entity shop, ThingDef def) =>
        OnMe(sim, me, def.Name) + Shops.Commissions.Stock(sim, shop, def);

    /// <summary>
    /// A commission it has the lot of to hand: taken to the one who pinned it and handed over, paid for
    /// once it's all come; fetched from its chests (or off the floor) first if it isn't on it. True while at it.
    /// </summary>
    private bool DeliverCommission(Simulation sim, Entity me, PerceptionView view, Entity shop, KnownOrder o, long now)
    {
        var w = sim.World;
        if (SomeoneElsesTo(o) || o.Wants is not { } wants)
            return false;
        var onMe = OnMe(sim, me, wants.Name);
        if (onMe >= o.Left)
        {
            var stack = Loose(sim, me).FirstOrDefault(i => w.Get<Thing>(i).Def == wants && !w.Has<ForSale>(i), Entity.Null);
            if (stack.IsNull)
                return false;
            Doing = $"taking {o.KindLabel} to {o.PosterName}";
            if (Reach(sim, me, o.Poster, now, returning: true))
                Send(new GiveCommand(me, o.Poster, stack, Math.Min(o.Left, w.Get<Stack>(stack).Count)));
            return true;
        }
        if (ToHand(sim, me, shop, wants) < o.Left)
            return false;
        var lying = Buildings.Parts(sim, shop).Where(e => w.Get<Thing>(e).Def.Building?.StorageStacks > 0).OrderBy(e => e.Index)
            .SelectMany(sim.StoredIn)
            .Concat(sim.AvailableItems().Where(i => w.Get<Thing>(i).Spawned && Buildings.At(sim, w.Get<Thing>(i).Cell) == shop).OrderBy(i => i.Index))
            .FirstOrDefault(i => w.Get<Thing>(i).Def == wants, Entity.Null);
        if (lying.IsNull)
            return false;
        Doing = $"fetching {wants.Label} for {o.PosterName}";
        return Fetch(sim, me, view, lying, o.Left - onMe, now);
    }

    /// <summary>Is this piece the one asked for: the thing named, and for a charm the mending it was asked of (as it knows its own charm).</summary>
    public static bool FillsAsk(Simulation sim, PerceptionView view, KnownOrder asked, Entity item) =>
        sim.World.IsAlive(item) && sim.World.TryGet<Thing>(item, out var t) && t.Def == asked.Wants
        && (asked.WantsMending == null || view.Charm(item)?.Mending == asked.WantsMending);

    /// <summary>The recipe at this station that makes what was asked for, if it can make it now (it knows it, has the tools and, unless not <paramref name="needMakings"/>, the makings to hand).</summary>
    public static RecipeDef? AskedRecipe(Simulation sim, Entity me, Entity station, KnownOrder asked, bool needMakings = true)
    {
        var w = sim.World;
        if (station.IsNull || asked.Wants is not { } want)
            return null;
        foreach (var r in w.Get<Thing>(station).Def.Recipes)
            if (r.Products.Count == 1 && r.Products[0].ThingDef == want && RecipeBooks.Knows(sim, me, r) && Crafting.CanMake(sim, me, r)
                && Crafting.MissingTool(sim, me, station, r) == null
                && (!needMakings || r.Ingredients.TrueForAll(x => Obtainable(sim, me, x))))
                return r;
        return null;
    }

    /// <summary>A piece that fills an open ask it knows of (asked of its shop): it goes to the asker, not on the shelf.</summary>
    private static bool ForAnAsk(Simulation sim, Entity me, PerceptionView view, Entity item) =>
        MyShop(sim, me) is { IsNull: false } shop
        && view.Orders().Any(o => o is { Kind: ServiceKind.Goods, Status: OrderStatus.Open, Wants: not null } && o.Where == shop && FillsAsk(sim, view, o, item));

    /// <summary>
    /// A piece of its own making on it that fills someone's ask, taken to them and handed over (paid on the spot,
    /// as a player's would be): the oldest ask first. True while at it.
    /// </summary>
    private bool Deliver(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        if (MyShop(sim, me) is not { IsNull: false } shop)
            return false;
        foreach (var o in view.Orders())
        {
            if (o is not { Kind: ServiceKind.Goods, Status: OrderStatus.Open, Wants: not null } || o.Where != shop || !w.IsAlive(o.Poster))
                continue;
            if (o.Commission)
            {
                if (DeliverCommission(sim, me, view, shop, o, now))
                    return true;
                continue;
            }
            var piece = Loose(sim, me).FirstOrDefault(i => ForTheShelf(sim, me, i) && !w.Has<ForSale>(i) && Showable(sim, me, view, i) && FillsAsk(sim, view, o, i), Entity.Null);
            if (piece.IsNull && w.TryGet<Equipment>(me, out var eq) && ForTheShelf(sim, me, eq.Weapon) && FillsAsk(sim, view, o, eq.Weapon))
            {
                Send(new UnequipCommand(me, GearSlot.Weapon)); // picked up into an empty hand, a weapon goes straight on: off again
                return true;
            }
            if (piece.IsNull)
                continue;
            Doing = $"taking the {w.Get<Thing>(piece).Label} to {o.PosterName}";
            if (Reach(sim, me, o.Poster, now, returning: true))
            {
                if (Inventory.InOwnPack(sim, me, piece) && w.Get<Carrying>(me).Item != piece)
                    Send(new HoldCommand(me, piece));
                Send(new GiveCommand(me, o.Poster, piece, 1));
            }
            return true;
        }
        return false;
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
    public int Stock(Simulation sim, Entity me)
    {
        var w = sim.World;
        return Goods.OnShow(sim).Count(g => w.Get<ForSale>(g.Item).Seller == me) + Loose(sim, me).Count(i => ForTheShelf(sim, me, i))
            + _asked.Keys.Count(e => w.IsAlive(e) && OnMyShelf(sim, me, e) && !w.Has<ForSale>(e) && MadeBy(sim, e, me));
    }

    /// <summary>Lying on a wares shelf it sells from.</summary>
    private static bool OnMyShelf(Simulation sim, Entity me, Entity item) =>
        sim.World.TryGet<Stored>(item, out var s) && Goods.CanSellAt(sim, me, s.In);

    /// <summary>
    /// The shop's stock seen to: a piece just set down is priced; a piece made and on it goes out
    /// while there's room on show; one that's sat unsold, or that it heard called too dear, comes
    /// down a little. True while at it.
    /// </summary>
    public bool ShowGoods(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        if (MyShop(sim, me) is not { IsNull: false } shop)
            return false;
        Haggled(sim, me, view);
        // A piece made to someone's ask goes to them by hand, first: on the shelf, anyone might buy it before they came.
        if (Deliver(sim, me, view, now))
            return true;
        if (FreshInTown(sim, view, now))
            return Shutters(sim, me, shop);
        if (_placing is { IsNull: false } put)
        {
            if (!w.IsAlive(put) || !(OnMyShelf(sim, me, put) || Loose(sim, me).Contains(put)))
                _placing = Entity.Null; // sold, or gone
            else if (OnMyShelf(sim, me, put))
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
                _placing = Entity.Null; // still on it: it goes out again below
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
            if (w.IsAlive(e) && !w.Has<ForSale>(e) && OnMyShelf(sim, me, e) && MadeBy(sim, e, me))
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
        var shelf = Goods.DisplayOf(sim, shop, me);
        var piece = Loose(sim, me).FirstOrDefault(i => ForTheShelf(sim, me, i) && Showable(sim, me, view, i) && !ForAnAsk(sim, me, view, i)
            && !shelf.IsNull && sim.ContainerRoom(shelf, w.Get<Thing>(i).Def, w.Get<Thing>(i).Material) > 0, Entity.Null);
        return !piece.IsNull && PutOnShow(sim, me, piece, shelf, now);
    }

    /// <summary>A piece of its own carried to its wares shelf and set there, to be priced once it's down (<see cref="_placing"/>).</summary>
    private bool PutOnShow(Simulation sim, Entity me, Entity piece, Entity shelf, long now)
    {
        var w = sim.World;
        Doing = $"putting the {w.Get<Thing>(piece).Label} on the {w.Get<Thing>(shelf).Label}";
        if (!Inventory.CanReachContainer(sim, me, shelf))
        {
            Walk(sim, me, w.Get<Thing>(shelf).Cell, now);
            return true;
        }
        // From the pack straight onto the shelf; from the hands, by putting it there (F at the shelf).
        if (Inventory.InOwnPack(sim, me, piece))
            Send(new StoreFromPackCommand(me, shelf, piece));
        else if (w.TryGet<Equipment>(me, out var eq) && eq.Weapon == piece)
        {
            Send(new UnequipCommand(me, GearSlot.Weapon));
            return true;
        }
        else
            Use(me, w.Get<Thing>(shelf).Cell);
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

    /// <summary>Its pieces off sale (they stay on the shelf), bar any someone's already asked for. True while at it.</summary>
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
