using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Shops;

/// <summary>
/// A lot of something ordered from the carter (<see cref="SupplyDef"/>) for a workplace: paid for
/// when it was ordered (<see cref="Paid"/>, none on the town's account), left there next morning.
/// </summary>
public sealed record SupplyOrder(int Id, string Supply, int Lots, Entity By, Entity Building, int Paid);

/// <summary>
/// The carter's order book. A tradesman orders what their workplace needs (<see cref="OrderSuppliesCommand"/>,
/// from the shop window), pays at once, and finds it in the workplace's container (<see cref="SupplyDef.Into"/>)
/// at <see cref="DeliveryHour"/> next morning; what won't fit is left beside it, and stays. With the road cut it
/// waits; in a shortage what's <see cref="SupplyDef.Scarce"/> doesn't come, and is paid back. A shop no player
/// holds is kept in by its trade's <see cref="TradeDef.StandingOrders"/>, ordered at <see cref="StandingHour"/>
/// on the town's account, so the town runs without anyone at it; a player's shop is theirs to keep in, and
/// keeping it well (enough, not too much, before a shortage) is part of the trade.
/// </summary>
public sealed class CarterOrders : ISimSystem, ISaveState
{
    public const int DeliveryHour = OreCarterSystem.DeliveryHour;

    /// <summary>The hour shops the AI runs send their standing orders.</summary>
    public const int StandingHour = 18;

    /// <summary>What a lot costs in a shortage, times its price.</summary>
    public const int ShortagePrice = 2;

    /// <summary>The most orders anyone may have waiting: a guard against a flood of commands.</summary>
    public const int MostOrders = 64;

    private readonly List<SupplyOrder> _orders = [];
    private int _nextId = 1;
    private int _delivered = -1, _standing = -1;

    public string Name => "CarterOrders";
    public int Interval => SimClock.TicksPerHour / 4;

    /// <summary>Every order waiting for the cart. Read a player's own through <see cref="PerceptionView.CarterOrders"/>.</summary>
    public IReadOnlyList<SupplyOrder> All => _orders;

    public static CarterOrders Of(Simulation sim) => sim.System<CarterOrders>();

    public void Update(Simulation sim)
    {
        if (!sim.Round.Live)
            return;
        var day = sim.Clock.Day;
        var hour = sim.Clock.HourOfDay;
        // The round's first morning is what it was stocked with: the cart comes from the next.
        if (_delivered < 0)
            _delivered = hour >= DeliveryHour ? day : day - 1;
        if (hour >= DeliveryHour && day > _delivered)
        {
            _delivered = day;
            Deliver(sim);
        }
        if (hour >= StandingHour && day > _standing)
        {
            _standing = day;
            Standing(sim);
        }
    }

    // ---------------- Ordering ----------------

    /// <summary>The supplies someone's workplace can order: their trade's role's, in load order. Empty without a workplace.</summary>
    public static List<SupplyDef> Catalogue(Simulation sim, Entity who) =>
        sim.World.TryGet<Trade>(who, out var t) && t.Def.HasWorkplace && !ShopSystem.WorkplaceOf(sim, t.Def).IsNull
            ? sim.Defs.AllSupplies.Where(s => s.ForRole == t.Def.Role).ToList() : [];

    /// <summary>Silver a lot costs now: dearer in a shortage.</summary>
    public static int Price(Simulation sim, SupplyDef supply) =>
        supply.Price * (Shortage(sim) ? ShortagePrice : 1);

    private static bool Shortage(Simulation sim) => sim.Director.Active.Any(c => c.Kind == CrisisKind.Shortage);

    /// <summary>
    /// The most lots of it the carter brings a morning: all of <see cref="SupplyDef.Max"/>, or the season's share
    /// of it while a player's trade brings the town that thing itself (the miner's ore).
    /// </summary>
    public static int MostLots(Simulation sim, SupplyDef supply) =>
        OreCarterSystem.SuppliedByPlayers(sim).Contains(supply.ThingDef.Name)
            ? Math.Max(1, (int)MathF.Round(supply.Max * sim.Defs.Season.SuppliedShare)) : supply.Max;

    /// <summary>Lots of it already ordered for a workplace, for tomorrow's cart.</summary>
    public int Ordered(Entity building, SupplyDef supply) =>
        _orders.Where(o => o.Building == building && o.Supply == supply.Name).Sum(o => o.Lots);

    /// <summary>Why someone can't order these lots now; null if they can.</summary>
    public string? Refusal(Simulation sim, Entity who, SupplyDef? supply, int lots)
    {
        if (!sim.Round.Live)
            return "there's no carter outside a season";
        if (supply == null || !Catalogue(sim, who).Contains(supply))
            return "that isn't for your workplace";
        if (lots < 1)
            return "order a lot or more";
        if (supply.Scarce && Shortage(sim))
            return $"there's no {supply.Label} to be had in the shortage";
        var building = ShopSystem.WorkplaceOf(sim, sim.World.Get<Trade>(who).Def);
        var room = MostLots(sim, supply) - Ordered(building, supply);
        if (lots > room)
            return room <= 0 ? $"the carter brings no more {supply.Label} than that in a morning" : $"the carter brings only {room} more lots of {supply.Label} a morning";
        if (_orders.Count(o => o.By == who) >= MostOrders)
            return "that's enough orders for one cart";
        var cost = Price(sim, supply) * lots;
        if (Coin.Count(sim, who) < cost)
            return $"it's {cost} silver, and you've {Coin.Count(sim, who)}";
        return null;
    }

    /// <summary>Order lots of a supply for someone's workplace, paying for them now. Returns why not, or null.</summary>
    public string? Order(Simulation sim, Entity who, SupplyDef? supply, int lots)
    {
        if (Refusal(sim, who, supply, lots) is { } why)
            return why;
        var cost = Price(sim, supply!) * lots;
        var paid = Spend(sim, who, cost);
        var building = ShopSystem.WorkplaceOf(sim, sim.World.Get<Trade>(who).Def);
        _orders.Add(new SupplyOrder(_nextId++, supply!.Name, lots, who, building, paid));
        return null;
    }

    /// <summary>Take back an order of one's own before the cart comes, the silver paid back. False if it isn't theirs or waiting.</summary>
    public bool Cancel(Simulation sim, Entity who, int id)
    {
        var at = _orders.FindIndex(o => o.Id == id && o.By == who);
        if (at < 0)
            return false;
        var order = _orders[at];
        _orders.RemoveAt(at);
        Refund(sim, order);
        return true;
    }

    /// <summary>Silver out of someone's hands and pack, to the carter (gone from town). Returns how much.</summary>
    private static int Spend(Simulation sim, Entity who, int amount)
    {
        var spent = 0;
        foreach (var stack in Coin.Stacks(sim, who))
        {
            if (spent >= amount)
                break;
            var take = Math.Min(amount - spent, sim.World.Get<Stack>(stack).Count);
            sim.World.Destroy(Inventory.TakeFromPerson(sim, who, stack, take));
            spent += take;
        }
        return spent;
    }

    private static void Refund(Simulation sim, SupplyOrder order)
    {
        if (order.Paid <= 0 || !sim.World.IsAlive(order.By) || !sim.World.Has<Carrying>(order.By))
            return;
        Inventory.Stow(sim, order.By, sim.CreateItem(sim.Defs.Thing(Coin.Silver), order.Paid));
    }

    // ---------------- The shops the AI runs ----------------

    /// <summary>A player steers someone in this trade: theirs to keep in, not the town's.</summary>
    public static bool PlayerHeld(Simulation sim, TradeDef trade)
    {
        foreach (var e in sim.Creatures())
            if (sim.World.TryGet<Trade>(e, out var t) && t.Def == trade && !sim.World.Get<Creature>(e).Dead && Players.Steered(sim, e))
                return true;
        return false;
    }

    /// <summary>How many of a supply's thing are in its container at a workplace now (what a standing order tops up).</summary>
    public static int InStock(Simulation sim, Entity building, SupplyDef supply)
    {
        var box = Into(sim, building, supply);
        return box.IsNull ? 0 : sim.StoredIn(box).Where(e => sim.World.Get<Thing>(e).Def == supply.ThingDef).Sum(e => sim.World.Get<Stack>(e).Count);
    }

    /// <summary>Lots a standing order wants to bring its workplace back up to its keep; 0 if none.</summary>
    public int Wanted(Simulation sim, Entity building, StandingOrder order)
    {
        var supply = order.SupplyDef;
        if (supply.Scarce && Shortage(sim))
            return 0;
        var short_ = order.Keep - InStock(sim, building, supply) - Ordered(building, supply) * supply.Per
            - Modding.SimContent.Incoming(sim, building, supply.ThingDef); // already on its way some other way (the carpenter's)
        if (short_ <= 0)
            return 0;
        return Math.Min((short_ + supply.Per - 1) / supply.Per, MostLots(sim, supply) - Ordered(building, supply));
    }

    /// <summary>Each evening: every workplace no player keeps sends for what its standing orders want, on the town's account.</summary>
    private void Standing(Simulation sim)
    {
        foreach (var trade in sim.Defs.AllTrades)
        {
            if (trade.StandingOrders.Count == 0 || !trade.HasWorkplace || PlayerHeld(sim, trade))
                continue;
            var building = ShopSystem.WorkplaceOf(sim, trade);
            if (building.IsNull)
                continue;
            foreach (var order in trade.StandingOrders)
                if (Wanted(sim, building, order) is var lots and > 0)
                    _orders.Add(new SupplyOrder(_nextId++, order.Supply, lots, Entity.Null, building, 0));
        }
    }

    // ---------------- The cart ----------------

    /// <summary>The container a supply's left in at a workplace (its <see cref="SupplyDef.Into"/>, else the chest); Entity.Null if neither.</summary>
    public static Entity Into(Simulation sim, Entity building, SupplyDef supply)
    {
        if (building.IsNull || !sim.World.IsAlive(building))
            return Entity.Null;
        var parts = Buildings.Parts(sim, building).Where(e => !sim.World.Has<Construction>(e)).OrderBy(e => e.Index).ToList();
        return parts.FirstOrDefault(e => sim.World.Get<Thing>(e).Def.Name == supply.Into, Entity.Null) is { IsNull: false } box ? box
            : parts.FirstOrDefault(e => sim.World.Get<Thing>(e).Def.Name == "Chest", Entity.Null);
    }

    /// <summary>The morning's cart: every order left where it goes, or (the road cut) left waiting.</summary>
    private void Deliver(Simulation sim)
    {
        var w = sim.World;
        var shortage = Shortage(sim);
        var left = new Dictionary<Entity, List<string>>();
        var cut = new HashSet<Entity>();
        foreach (var order in _orders.ToList())
        {
            var supply = sim.Defs.SupplyOrNull(order.Supply);
            var box = supply == null ? Entity.Null : Into(sim, order.Building, supply);
            if (supply == null || box.IsNull)
            {
                _orders.Remove(order); // the workplace or its store is gone: the silver back
                Refund(sim, order);
                continue;
            }
            var cell = w.Get<Thing>(box).Cell;
            if (OreCarterSystem.CutOff(sim, cell))
            {
                cut.Add(order.Building);
                continue; // waits for the road
            }
            _orders.Remove(order);
            if (supply.Scarce && shortage)
            {
                Refund(sim, order);
                if (!order.By.IsNull)
                    sim.Messages.Tell(order.By, MessageKind.Warning, $"The carter came without your {supply.Label}: there's none to be had in the shortage. You've had your silver back.", cell);
                continue;
            }
            for (var n = order.Lots * supply.Per; n > 0;)
            {
                var take = Math.Min(n, supply.ThingDef.StackLimit);
                n -= take;
                var rest = sim.Store(box, sim.CreateItem(supply.ThingDef, take, supply.MaterialDef));
                if (!rest.IsNull)
                    sim.PlaceItemNear(rest, cell); // what won't fit stands beside it: over-ordered
            }
            (left.TryGetValue(order.Building, out var list) ? list : left[order.Building] = []).Add($"{order.Lots * supply.Per} {supply.Label}");
            if (!order.By.IsNull)
                sim.Messages.Tell(order.By, MessageKind.Info, $"The carter's left your {order.Lots * supply.Per} {supply.Label} in the {w.Get<Thing>(box).Def.Label}.", cell);
        }
        foreach (var (building, goods) in left.OrderBy(kv => kv.Key.Index))
            if (w.TryGet<PresetBuilding>(building, out var b))
                sim.Messages.Add(MessageKind.Info, $"The carter's been by the {b.Def.Label}: {string.Join(", ", goods)}.", b.Anchor);
        foreach (var building in cut.OrderBy(e => e.Index))
            if (w.TryGet<PresetBuilding>(building, out var b))
                sim.Messages.Add(MessageKind.Warning, $"The carter couldn't get through to the {b.Def.Label}, the {(sim.Repairs.Breaches.Count > 0 ? sim.Repairs.Breaches[0].Label : "road")} down: its order waits.", b.Anchor);
    }

    public void Save(SaveWriter w, Simulation sim)
    {
        w.Int(_nextId);
        w.Int(_delivered);
        w.Int(_standing);
        w.Int(_orders.Count);
        foreach (var o in _orders)
        {
            w.Int(o.Id);
            w.String(o.Supply);
            w.Int(o.Lots);
            w.Entity(o.By);
            w.Entity(o.Building);
            w.Int(o.Paid);
        }
    }

    public void Load(SaveReader r, Simulation sim)
    {
        _nextId = r.Int();
        _delivered = r.Int();
        _standing = r.Int();
        _orders.Clear();
        for (var n = r.Count(1 << 12); n > 0; n--)
            _orders.Add(new SupplyOrder(r.Int(), r.String(), r.Int(), r.Entity(), r.Entity(), r.Int()));
    }
}

/// <summary>Order lots of a supply from the carter for your workplace, paid now (<see cref="CarterOrders.Order"/>).</summary>
public sealed record OrderSuppliesCommand(Entity Avatar, string Supply, int Lots) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (!InventoryCommand.Ready(sim, Avatar))
            return;
        var supply = string.IsNullOrEmpty(Supply) ? null : sim.Defs.SupplyOrNull(Supply);
        var at = sim.World.Get<Position>(Avatar).Cell;
        if (CarterOrders.Of(sim).Order(sim, Avatar, supply, Lots) is { } why)
            sim.Messages.Tell(Avatar, MessageKind.Info, $"Can't order that: {why}.", at);
        else
            sim.Messages.Tell(Avatar, MessageKind.Good, $"Ordered {Lots * supply!.Per} {supply.Label} from the carter: it comes at first light.", at);
    }
}

/// <summary>Take back an order of your own before the cart comes, and have the silver back.</summary>
public sealed record CancelSuppliesCommand(Entity Avatar, int Order) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (InventoryCommand.Ready(sim, Avatar))
            CarterOrders.Of(sim).Cancel(sim, Avatar, Order);
    }
}
