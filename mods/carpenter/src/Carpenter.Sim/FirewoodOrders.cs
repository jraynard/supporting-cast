using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Carpentry;

/// <summary>Bundles of firewood a workplace has sent to the carpenter for: paid up front (<see cref="Paid"/>; 0 on the town's account), delivered a bundle at a time.</summary>
public sealed record FirewoodOrder(int Id, Entity By, Entity Building, int Bundles, int Delivered, int Paid);

/// <summary>
/// Firewood to order from the carpenter, as from the carter (Shops/CarterOrders): a keeper sends for bundles from their shop
/// window and pays at once; the carpenter (a player, a bot or the AI) splits the logs and carries the firewood to that
/// workplace's own fuel store (<see cref="FuelStores"/>), F at it, and is paid a bundle at a time as it goes in. Quicker
/// than the carter (no waiting for the morning cart) and the silver stays in town, with the carpenter. Where a player keeps the
/// woodshop, the shops the AI runs send their firewood orders here each evening rather than to the carter, so the carpenter
/// has steady paid work; what's on its way counts against the carter's standing orders (<see cref="Modding.SimRegistry.Incoming"/>).
/// </summary>
public sealed class FirewoodOrders : ISimSystem, ISaveState
{
    /// <summary>Firewood in a bundle, and what a bundle costs.</summary>
    public const int Bundle = 8, Price = 2;

    /// <summary>Bundles a workplace may have waiting at once.</summary>
    public const int MostBundles = 6;

    /// <summary>The hour the shops the AI runs send their orders (before the carter's, so it counts them).</summary>
    public const int OrderHour = 17;

    private readonly List<FirewoodOrder> _orders = [];
    private int _nextId = 1, _ordered = -1;

    public string Name => "FirewoodOrders";
    public int Interval => SimClock.TicksPerHour / 4;

    public IReadOnlyList<FirewoodOrder> All => _orders;

    public static FirewoodOrders Of(Simulation sim) => sim.System<FirewoodOrders>();

    public void Update(Simulation sim)
    {
        if (!sim.Round.Live)
            return;
        var day = sim.Clock.Day;
        if (sim.Clock.HourOfDay >= OrderHour && day > _ordered)
        {
            _ordered = day;
            Standing(sim);
        }
    }

    // ---------------- Who ----------------

    /// <summary>Someone in town keeps the woodshop: there's a carpenter to send to.</summary>
    public static bool HasCarpenter(Simulation sim) => Carpenters(sim).Any();

    private static IEnumerable<Entity> Carpenters(Simulation sim) =>
        sim.Creatures().Where(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == CarpenterModule.TradeName && !sim.World.Get<Creature>(e).Dead);

    /// <summary>A player keeps the woodshop: the town sends its firewood their way.</summary>
    public static bool PlayerCarpenter(Simulation sim) => Carpenters(sim).Any(e => Players.Steered(sim, e));

    /// <summary>The workplace someone keeps whose fires burn from a store of their own; Entity.Null if none.</summary>
    public static Entity WorkplaceOf(Simulation sim, Entity who)
    {
        if (!sim.World.TryGet<Trade>(who, out var trade) || !trade.Def.HasWorkplace || trade.Def.Name == CarpenterModule.TradeName)
            return Entity.Null;
        var building = ShopSystem.WorkplaceOf(sim, trade.Def);
        return StoreFor(sim, building).IsNull ? Entity.Null : building;
    }

    /// <summary>The fuel store at a workplace firewood goes into (the first that takes it).</summary>
    public static Entity StoreFor(Simulation sim, Entity building) =>
        FuelStores.In(sim, building).FirstOrDefault(e => TakesFirewood(sim, e), Entity.Null);

    private static bool TakesFirewood(Simulation sim, Entity store) =>
        sim.World.Get<Thing>(store).Def.Building!.StorageOnly is not { Count: > 0 } only || only.Contains(Stock.FirewoodDef);

    /// <summary>Bundles still to come for a building.</summary>
    public int Owed(Entity building) => _orders.Where(o => o.Building == building).Sum(o => o.Bundles - o.Delivered);

    /// <summary>The open orders for a building, oldest first.</summary>
    public IEnumerable<FirewoodOrder> For(Entity building) => _orders.Where(o => o.Building == building).OrderBy(o => o.Id);

    // ---------------- Ordering ----------------

    public string? Refusal(Simulation sim, Entity who, int bundles)
    {
        if (!sim.Round.Live)
            return "there's no carpenter to send to outside a season";
        if (!HasCarpenter(sim))
            return "nobody keeps the woodshop";
        var building = WorkplaceOf(sim, who);
        if (building.IsNull)
            return "your workplace keeps no firewood";
        if (bundles < 1)
            return "send for a bundle or more";
        var room = MostBundles - Owed(building);
        if (bundles > room)
            return room <= 0 ? "that's as much as the carpenter can carry you for now" : $"only {room} more bundles for now";
        if (Coin.Count(sim, who) < bundles * Price)
            return $"it's {bundles * Price} silver, and you've {Coin.Count(sim, who)}";
        return null;
    }

    /// <summary>Send for bundles, paying now. Returns why not, or null.</summary>
    public string? Order(Simulation sim, Entity who, int bundles)
    {
        if (Refusal(sim, who, bundles) is { } why)
            return why;
        var building = WorkplaceOf(sim, who);
        var paid = Spend(sim, who, bundles * Price);
        _orders.Add(new FirewoodOrder(_nextId++, who, building, bundles, 0, paid));
        TellCarpenters(sim, $"{sim.World.Get<Creature>(who).Name} sends for {Bundles(bundles)} of firewood for the {Label(sim, building)}.", building);
        return null;
    }

    /// <summary>Take back what's still to come of an order of one's own, its share of the silver paid back.</summary>
    public bool Cancel(Simulation sim, Entity who, int id)
    {
        var at = _orders.FindIndex(o => o.Id == id && o.By == who);
        if (at < 0)
            return false;
        var order = _orders[at];
        _orders.RemoveAt(at);
        var back = order.Paid - order.Delivered * Price;
        if (back > 0 && sim.World.IsAlive(who) && sim.World.Has<Carrying>(who))
            Inventory.Stow(sim, who, sim.CreateItem(sim.Defs.Thing(Coin.Silver), back));
        return true;
    }

    // ---------------- Delivering ----------------

    /// <summary>Firewood on someone, in hand and in the pack.</summary>
    public static int Carried(Simulation sim, Entity who) => FirewoodOn(sim, who).Sum(e => sim.World.Get<Stack>(e).Count);

    private static IEnumerable<Entity> FirewoodOn(Simulation sim, Entity who)
    {
        var w = sim.World;
        var held = w.Get<Carrying>(who).Item;
        if (w.IsAlive(held) && w.Get<Thing>(held).Def.Name == Stock.FirewoodDef)
            yield return held;
        var pack = sim.PackOf(who);
        if (!pack.IsNull)
            foreach (var e in sim.StoredIn(pack).Where(e => w.Get<Thing>(e).Def.Name == Stock.FirewoodDef).ToList())
                yield return e;
    }

    /// <summary>The carpenter, at a workplace's store with firewood on them and bundles owed there: what F does.</summary>
    public static bool CanDeliver(Simulation sim, Entity carpenter, Entity store) =>
        FuelStores.IsStore(sim, store) && sim.World.TryGet<Trade>(carpenter, out var t) && t.Def.Name == CarpenterModule.TradeName
        && Of(sim).Owed(Buildings.Of(sim, store)) > 0 && Carried(sim, carpenter) >= Bundle;

    /// <summary>Put what bundles they can into the store, oldest order first, and be paid for each. Returns bundles delivered.</summary>
    public int Deliver(Simulation sim, Entity carpenter, Entity store)
    {
        if (!CanDeliver(sim, carpenter, store))
            return 0;
        var w = sim.World;
        var building = Buildings.Of(sim, store);
        var done = 0;
        var pay = 0;
        foreach (var order in For(building).ToList())
        {
            var bundles = Math.Min(order.Bundles - order.Delivered, Carried(sim, carpenter) / Bundle);
            var put = 0;
            for (; put < bundles && Move(sim, carpenter, store, Bundle); put++) { }
            if (put == 0)
                break;
            done += put;
            pay += put * Price;
            var now = order with { Delivered = order.Delivered + put };
            var at = _orders.FindIndex(o => o.Id == order.Id);
            if (now.Delivered >= now.Bundles)
            {
                _orders.RemoveAt(at);
                if (w.IsAlive(order.By))
                    sim.Messages.Tell(order.By, MessageKind.Good, $"{w.Get<Creature>(carpenter).Name} has brought the firewood you sent for: {Bundles(order.Bundles)} in the {w.Get<Thing>(store).Label}.", w.Get<Thing>(store).Cell);
            }
            else
                _orders[at] = now;
            if (put < bundles)
                break; // the store's full
        }
        if (pay > 0)
        {
            Inventory.Stow(sim, carpenter, sim.CreateItem(sim.Defs.Thing(Coin.Silver), pay));
            sim.Messages.Tell(carpenter, MessageKind.Good, $"{Bundles(done)} of firewood into the {Label(sim, building)}'s {w.Get<Thing>(store).Label}: {pay} silver.", w.Get<Thing>(store).Cell);
        }
        return done;
    }

    /// <summary>A bundle off the carpenter into the store. False if it won't fit or they're short.</summary>
    private static bool Move(Simulation sim, Entity carpenter, Entity store, int count)
    {
        if (Carried(sim, carpenter) < count)
            return false;
        var w = sim.World;
        foreach (var stack in FirewoodOn(sim, carpenter).ToList())
        {
            if (count <= 0)
                break;
            var take = Math.Min(count, w.Get<Stack>(stack).Count);
            var def = w.Get<Thing>(stack).Def;
            if (!sim.HasRoomFor(store, def, w.Get<Thing>(stack).Material))
                return false;
            sim.Store(store, Inventory.TakeFromPerson(sim, carpenter, stack, take));
            count -= take;
        }
        return count <= 0;
    }

    // ---------------- The shops the AI runs ----------------

    /// <summary>
    /// Each evening, with a player at the woodshop: every workplace the AI runs sends for the firewood its store wants to get back
    /// up to its keep (its trade's standing order for firewood), on the town's account, to the carpenter rather than the carter.
    /// </summary>
    private void Standing(Simulation sim)
    {
        if (!PlayerCarpenter(sim))
            return;
        var firewood = sim.Defs.Thing(Stock.FirewoodDef);
        foreach (var trade in sim.Defs.AllTrades)
        {
            if (!trade.HasWorkplace || CarterOrders.PlayerHeld(sim, trade) || trade.Name == CarpenterModule.TradeName)
                continue;
            var building = ShopSystem.WorkplaceOf(sim, trade);
            if (building.IsNull || StoreFor(sim, building).IsNull)
                continue;
            var keep = trade.StandingOrders.Where(o => o.SupplyDef.ThingDef == firewood).Sum(o => o.Keep);
            var have = FuelStores.In(sim, building).Sum(s => sim.StoredIn(s).Where(e => sim.World.Get<Thing>(e).Def == firewood).Sum(e => sim.World.Get<Stack>(e).Count));
            var wanted = (keep - have - Owed(building) * Bundle + Bundle - 1) / Bundle;
            wanted = Math.Min(wanted, MostBundles - Owed(building));
            if (wanted <= 0)
                continue;
            _orders.Add(new FirewoodOrder(_nextId++, Entity.Null, building, wanted, 0, 0));
            TellCarpenters(sim, $"The {Label(sim, building)} sends for {Bundles(wanted)} of firewood (the town pays).", building);
        }
    }

    // ---------------- Words ----------------

    public static string Bundles(int n) => n == 1 ? "a bundle" : $"{n} bundles";

    private static string Label(Simulation sim, Entity building) =>
        sim.World.TryGet<PresetBuilding>(building, out var b) ? b.Def.Label : "workplace";

    private static void TellCarpenters(Simulation sim, string text, Entity building)
    {
        foreach (var c in Carpenters(sim))
            sim.Messages.Tell(c, MessageKind.Info, text, sim.World.TryGet<PresetBuilding>(building, out var b) ? b.Anchor : null);
    }

    /// <summary>Silver out of someone's hands and pack, held against the order. Returns how much.</summary>
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

    // ---------------- Saving ----------------

    public void Save(SaveWriter w, Simulation sim)
    {
        w.Int(_nextId);
        w.Int(_ordered);
        w.Int(_orders.Count);
        foreach (var o in _orders)
        {
            w.Int(o.Id);
            w.Entity(o.By);
            w.Entity(o.Building);
            w.Int(o.Bundles);
            w.Int(o.Delivered);
            w.Int(o.Paid);
        }
    }

    public void Load(SaveReader r, Simulation sim)
    {
        _nextId = r.Int();
        _ordered = r.Int();
        _orders.Clear();
        for (var n = r.Count(1 << 10); n > 0; n--)
            _orders.Add(new FirewoodOrder(r.Int(), r.Entity(), r.Entity(), Math.Max(0, r.Int()), Math.Max(0, r.Int()), Math.Max(0, r.Int())));
    }
}

/// <summary>Send for bundles of firewood from the carpenter (<see cref="FirewoodOrders.Order"/>): from the shop window.</summary>
public sealed record OrderFirewoodCommand(Entity Avatar, int Bundles) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (!sim.World.IsAlive(Avatar) || !sim.World.Has<Trade>(Avatar) || !sim.World.Has<Carrying>(Avatar))
            return;
        if (FirewoodOrders.Of(sim).Order(sim, Avatar, Bundles) is { } why)
            sim.Messages.Tell(Avatar, MessageKind.Info, $"No firewood sent for: {why}.");
    }
}

/// <summary>Take back an order of firewood not yet brought (<see cref="FirewoodOrders.Cancel"/>).</summary>
public sealed record CancelFirewoodCommand(Entity Avatar, int Id) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (sim.World.IsAlive(Avatar) && sim.World.Has<Carrying>(Avatar))
            FirewoodOrders.Of(sim).Cancel(sim, Avatar, Id);
    }
}

/// <summary>An AI carpenter carries split firewood from by the chopping block to a workplace that sent for it.</summary>
public sealed class FirewoodRunJob(Entity firewood, Entity store) : Job
{
    /// <summary>How near the chopping block (or in the woodshop) firewood must lie to be the carpenter's own splitting.</summary>
    public const int ByTheBlock = 4;

    /// <summary>The carpenter's own split firewood: lying by its chopping block.</summary>
    public static IEnumerable<Entity> Split(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var block = Carpenters.StationOf(sim, Carpenters.WoodshopOf(sim, pawn), Carpenters.BlockDef);
        if (block.IsNull)
            return [];
        var at = w.Get<Thing>(block).Cell;
        return sim.AvailableItems().Where(e => w.Get<Thing>(e).Def.Name == Stock.FirewoodDef && !w.Has<Stored>(e)
            && Map.Distance(w.Get<Thing>(e).Cell, at) <= ByTheBlock);
    }

    public static Job? For(Simulation sim, Entity pawn)
    {
        var orders = FirewoodOrders.Of(sim);
        var w = sim.World;
        foreach (var order in orders.All.OrderBy(o => o.Id))
        {
            var store = FirewoodOrders.StoreFor(sim, order.Building);
            if (store.IsNull || sim.Reservations.IsReservedByOther(store, pawn))
                continue;
            var from = w.Get<Position>(pawn).Cell;
            var wood = Split(sim, pawn).Where(e => w.Get<Stack>(e).Count >= FirewoodOrders.Bundle && !sim.Reservations.IsReservedByOther(e, pawn))
                .OrderBy(e => Map.Distance(w.Get<Thing>(e).Cell, from)).ThenBy(e => e.Index).FirstOrDefault(Entity.Null);
            if (wood.IsNull)
                return null; // nothing split yet: splitting comes first (Carpenters.Split)
            return new FirewoodRunJob(wood, store);
        }
        return null;
    }

    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Carrying firewood";
        if (!Reserve(firewood) || !Reserve(store)) yield break;
        foreach (var s in GoTo(Get<Thing>(firewood).Cell, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;
        if (!Sim.IsAvailable(firewood) || !Get<Carrying>(Pawn).Item.IsNull)
        {
            Fail("can't take the firewood");
            yield break;
        }
        var owed = FirewoodOrders.Of(Sim).Owed(Buildings.Of(Sim, store)) * FirewoodOrders.Bundle;
        Get<Carrying>(Pawn).Item = Sim.TakeFromStack(firewood, Math.Max(FirewoodOrders.Bundle, Math.Min(owed, Get<Stack>(firewood).Count)));
        if (!Alive(store))
        {
            Fail("the store is gone");
            yield break;
        }
        foreach (var s in GoToBeside(store)) yield return s;
        if (HasFailed || !Alive(store)) yield break;
        foreach (var s in Wait(SimClock.TicksPerSecond * 2)) yield return s;
        FirewoodOrders.Of(Sim).Deliver(Sim, Pawn, store);
        // What's over is set down by the store, not carried about.
        var left = Get<Carrying>(Pawn).Item;
        if (Sim.World.IsAlive(left))
        {
            Get<Carrying>(Pawn).Item = Entity.Null;
            Sim.PlaceItemNear(left, Get<Thing>(store).Cell);
        }
    }
}
