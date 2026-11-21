using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Hospitality;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Bots;

/// <summary>
/// The cook's work beyond the stew pot, as a player at the keyboard does it: the kitchen's fires
/// fed from the log basket (the villagers leave a staff-only kitchen's fires to its cook), the
/// spit, the oven and the prep table, the butcher block, and dishes made ahead for the sign. It
/// reads the spit by its crust, the oven by its smell and the dough by its rise, never what's
/// hidden in them; the kitchen's own larder and log basket are common knowledge to it, as a
/// keeper knows their shelves.
/// </summary>
public sealed partial class BotPlayer
{
    /// <summary>Of each dish on the sign, how many it likes made ahead (on it and up on the hatch).</summary>
    public const int DishesAhead = 2;

    /// <summary>A kitchen fire below this share of what it holds is fed.</summary>
    public const float FeedBelow = 0.5f;

    /// <summary>Logs taken from the basket at a time.</summary>
    public const int LogsAtOnce = 8;

    // Its dishes are on the sign; and the dish the dough in hand is meant for.
    private bool _menuSet;
    private string? _doughFor;
    private KnownSpit? _spitSeen;

    /// <summary>A roast on the spit or a bake in the oven left to itself goes on browning: back to it (its fire fed first).</summary>
    private bool SeeToTheSpitAndOven(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var spit = Part(sim, me, "Spit");
        if (view.Spit(spit) is { Pieces: > 0 } roast)
        {
            if (!roast.Lit && FireBy(sim, spit) is { IsNull: false } fire)
                return FeedFire(sim, me, view, fire, now);
            Doing = "turning the roast";
            return !HandsFree(sim, me) || UseAt(sim, me, spit, now);
        }
        var oven = Part(sim, me, "Oven");
        if (view.Oven(oven) is { Loaves: > 0 } baking)
        {
            if (!baking.Lit)
                return FeedOven(sim, me, view, oven, now);
            Doing = baking.Pie ? "minding the pie in the oven" : "minding the bread in the oven";
            return !HandsFree(sim, me) || UseAt(sim, me, oven, now);
        }
        return false;
    }

    /// <summary>The fire the pots and spit hang by: fed before it runs low.</summary>
    private bool KeepTheFires(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        var pot = KitchenPot(sim, me);
        foreach (var by in new[] { pot, Part(sim, me, "Spit") })
            if (FireBy(sim, by) is { IsNull: false } fire && view.FuelIn(fire) is { } fuel
                && fuel < w.Get<Thing>(fire).Def.Building!.Fuel!.Capacity * FeedBelow)
                return FeedFire(sim, me, view, fire, now);
        return false; // the oven's fed as bread or a pie's begun (BakeDish): no wood burnt keeping it hot for nothing
    }

    /// <summary>The dishes its kitchen can make up on the sign, priced by its greed: once, as it opens.</summary>
    private bool OfferTheMenu(Simulation sim, Entity me, PerceptionView view)
    {
        if (_menuSet || MyShop(sim, me) is not { IsNull: false } shop)
            return false;
        _menuSet = true;
        // Not porridge: that's a morning pot of its own.
        foreach (var d in view.Menu(shop).Where(d => !d.Offered && !d.Dish.Menu!.Morning))
        {
            var price = Math.Max(1, (int)MathF.Round(d.Dish.Menu!.Price * (0.85f + 0.5f * Temper.Greed)));
            Send(new SetDishPriceCommand(me, shop, d.Dish, price, true));
            Doing = $"putting {d.Dish.Label} on the sign at {price}";
        }
        return false;
    }

    /// <summary>A side of pork or a bird in the larder, as the carter brought it: jointed or plucked at the block.</summary>
    private bool DressWhole(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        var block = Part(sim, me, "ButcherBlock");
        if (block.IsNull)
            return false;
        if (!Butchery.Side(sim, me).IsNull || !Butchery.Bird(sim, me).IsNull)
        {
            Doing = "dressing meat at the block";
            return UseAt(sim, me, block, now);
        }
        return FromLarder(sim, me, view, now, d => d.Joints != null || d.Plucks != null, 1, "a side to dress");
    }

    /// <summary>A few of each dish on the sign made ahead, for the rush: the bread kneaded first (it wants an hour to prove), then the roast, then a pie.</summary>
    private bool MakeAhead(Simulation sim, Entity me, PerceptionView view, long now)
    {
        if (MyShop(sim, me) is not { IsNull: false } shop)
            return false;
        var hatch = Hatch.Of(sim, shop);
        foreach (var name in new[] { Baking.Bread, Roasting.Dish, Pies.Pie })
        {
            if (view.Menu(shop).FirstOrDefault(d => d.Dish.Name == name) is not { Offered: true } posted)
                continue;
            var have = OnMe(sim, me, name) + view.InContainer(hatch).Where(d => sim.World.Get<Thing>(d).Def.Name == name).Sum(d => sim.World.Get<Stack>(d).Count);
            if (have < DishesAhead && MakeDish(sim, me, view, posted.Dish, now))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Rations for the road, for the inn's hampers: while a hamper's on order or the innkeeper's
    /// merchandise shelf is short of them, three portions packed at the prep table and tied, kept on
    /// it to go in the next hamper (the innkeeper sells them, not the cook). Two at most.
    /// </summary>
    private bool PackRations(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        if (!Provisions.Cooks(sim, me))
            return false;
        var wanted = Rations.HamperWanted(sim, me)
            || (!Provisions.Merchandise(sim).IsNull && Provisions.RationsShelved(sim) < Provisions.RationsLow);
        if (!wanted || Rations.Carried(sim, me).Count >= PackHamperJob.RationsPerHamper)
            return false;
        var table = Part(sim, me, "PrepTable");
        if (table.IsNull)
            return false;
        if (Rations.Carried(sim, me).FirstOrDefault(Entity.Null) is { IsNull: false } bundle && w.Get<Carrying>(me).Item == bundle)
        {
            Send(new StowCommand(me));
            return true;
        }
        // Three portions of something that keeps on the road, in hand (packing takes from the hand first, not the pack's roast).
        static bool Road(ThingDef d) => Rations.IsPortion(d) && !d.Cooked && !d.Roasts && !d.Grain && d.Name != Baking.Flour && d.Joints == null && d.Plucks == null && !d.IsSeasoner;
        var food = AvatarSystem.SupplyItem(sim, me, e => Road(w.Get<Thing>(e).Def) && w.Get<Stack>(e).Count >= Rations.Portions);
        if (food.IsNull)
            return FromLarder(sim, me, view, now, Road, Rations.Portions, "food for the road");
        if (w.Get<Carrying>(me).Item != food)
        {
            Send(new HoldCommand(me, food));
            return true;
        }
        Doing = "packing rations";
        return AtStation(sim, me, table, now, t => Send(new PrepCommand(me, t, PrepStep.Pack)));
    }

    /// <summary>One step toward a dish off the sign. False if it can't be made now (or the dough's still proving).</summary>
    private bool MakeDish(Simulation sim, Entity me, PerceptionView view, ThingDef dish, long now) => dish.Name switch
    {
        Roasting.Dish => Roast(sim, me, view, now),
        Baking.Bread => BakeDish(sim, me, view, pie: false, now),
        Pies.Pie => BakeDish(sim, me, view, pie: true, now),
        _ => false,
    };

    /// <summary>Meat on the spit (the fire under it fed first), turned till it's golden all round; meat from the larder, or a side dressed for it.</summary>
    private bool Roast(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var spit = Part(sim, me, "Spit");
        if (spit.IsNull)
            return false;
        // The spit as it last saw it: it knows it's empty from across the kitchen.
        if (view.Spit(spit) is { } seen)
            _spitSeen = seen;
        if (_spitSeen is not { } known || known.Spit != spit)
        {
            Doing = "going to the spit";
            return AtStation(sim, me, spit, now, _ => { });
        }
        if (known.Pieces == 0 && Roasting.Joint(sim, me).IsNull)
            return FromLarder(sim, me, view, now, d => d.Roasts, Roasting.MostPieces, "meat for the spit") || DressWhole(sim, me, view, now);
        if (FireBy(sim, spit) is { IsNull: false } fire && (!known.Lit || view.FuelIn(fire) is < 4f))
            return FeedFire(sim, me, view, fire, now);
        Doing = known.Pieces > 0 ? "turning the roast" : "putting meat on the spit";
        return !HandsFree(sim, me) || UseAt(sim, me, spit, now);
    }

    /// <summary>
    /// Bread or a pie: the oven fed, flour kneaded at the prep table; bread's dough left to prove
    /// till it's well risen, a pie's filled and crimped at once; then into the oven, by the smell.
    /// One ball of dough at a time, meant for one or the other.
    /// </summary>
    private bool BakeDish(Simulation sim, Entity me, PerceptionView view, bool pie, long now)
    {
        var w = sim.World;
        var oven = Part(sim, me, "Oven");
        var table = Part(sim, me, "PrepTable");
        if (oven.IsNull || table.IsNull)
            return false;
        var raw = Pies.RawIn(sim, me);
        if (pie && !raw.IsNull)
            return IntoTheOven(sim, me, view, oven, now);
        var dough = Baking.DoughIn(sim, me);
        if (!dough.IsNull)
        {
            if (pie != (_doughFor == Pies.Pie))
                return false; // that dough's for the other
            if (pie)
            {
                if (Pies.FillingPortionsOn(sim, me) < Pies.Filling)
                    return FromLarder(sim, me, view, now, d => Pies.IsFilling(d) && !d.Grain && d.Name != Baking.Flour && d.Joints == null && d.Plucks == null, Pies.Filling, "a filling");
                Doing = "making a pie";
                return AtStation(sim, me, table, now, t => Send(new PrepCommand(me, t, PrepStep.Pie)));
            }
            if (view.Dough(dough) is "slack" or "rising")
            {
                Doing = "letting the dough prove";
                return false;
            }
            return IntoTheOven(sim, me, view, oven, now);
        }
        if (!raw.IsNull)
            return false; // a pie to bake first
        // Dough put by in the larder while the oven was fed: back for it.
        if (sim.StoredIn(Larder(sim, me)).Any(e => w.Has<Dough>(e) || w.Has<RawPie>(e)))
            return FromLarder(sim, me, view, now, d => d.Name is Baking.DoughDef or Pies.Raw, 1, "the dough");
        // The oven's fed before the dough's made: with dough on it, the work key there puts it in.
        if (view.FuelIn(oven) is { } fuel && fuel < w.Get<Thing>(oven).Def.Building!.Fuel!.Capacity * FeedBelow)
            return FeedOven(sim, me, view, oven, now);
        if (Baking.FlourIn(sim, me).IsNull)
            return FromLarder(sim, me, view, now, d => d.Name == Baking.Flour, Baking.FlourPerDough, "flour");
        Doing = "kneading dough";
        _doughFor = pie ? Pies.Pie : Baking.Bread;
        return AtStation(sim, me, table, now, t => Send(new PrepCommand(me, t, PrepStep.Knead)));
    }

    /// <summary>The dough or pie on it into the oven; the oven cold, it's put by in the larder while the fire's seen to.</summary>
    private bool IntoTheOven(Simulation sim, Entity me, PerceptionView view, Entity oven, long now)
    {
        var w = sim.World;
        if (view.Oven(oven) is not { } known)
        {
            Doing = "going to the oven";
            return AtStation(sim, me, oven, now, _ => { });
        }
        if (known.Loaves > 0)
            return false; // something's in it already: seen to first
        if (known.Lit)
        {
            Doing = "putting it in the oven";
            return !HandsFree(sim, me) || UseAt(sim, me, oven, now);
        }
        var item = AvatarSystem.SupplyItem(sim, me, e => w.Has<Dough>(e) || w.Has<RawPie>(e));
        if (item.IsNull)
            return false;
        if (w.Get<Carrying>(me).Item == item)
        {
            Send(new StowCommand(me));
            return true;
        }
        Doing = "putting the dough by while the oven's lit";
        return AtStation(sim, me, Larder(sim, me), now, l => Send(new StoreFromPackCommand(me, l, item)));
    }

    /// <summary>
    /// Proved dough doesn't wait on the orders: puffed up, it's into the oven before it sinks (the
    /// oven's own baking is seen to as it's found, between orders).
    /// </summary>
    private bool DoughReady(Simulation sim, Entity me, PerceptionView view, long now)
    {
        if (_doughFor == Pies.Pie || Baking.DoughIn(sim, me) is not { IsNull: false } dough || view.Dough(dough) is not ("well risen" or "puffed up tall"))
            return false;
        var oven = Part(sim, me, "Oven");
        return !oven.IsNull && view.Oven(oven) is not { Loaves: > 0 } && IntoTheOven(sim, me, view, oven, now);
    }

    /// <summary>
    /// Take some of a kind of thing from the larder (as many as <paramref name="count"/>). False if
    /// there's none there; it says so, once an hour.
    /// </summary>
    private bool FromLarder(Simulation sim, Entity me, PerceptionView view, long now, Func<ThingDef, bool> fits, int count, string what)
    {
        var w = sim.World;
        var larder = Larder(sim, me);
        if (larder.IsNull || !sim.StoredIn(larder).Any(e => fits(w.Get<Thing>(e).Def)))
            return false;
        Doing = $"fetching {what}";
        return AtStation(sim, me, larder, now, l =>
        {
            if (view.InContainer(l).FirstOrDefault(e => fits(w.Get<Thing>(e).Def), Entity.Null) is { IsNull: false } stack)
                Send(new TakeFromStorageCommand(me, stack, count));
        });
    }

    /// <summary>
    /// Hands empty for what comes off the spit or out of the oven (into the pack it'd join a stack
    /// there): what's in them is stowed first. True once they're free.
    /// </summary>
    private bool HandsFree(Simulation sim, Entity me)
    {
        if (sim.World.Get<Carrying>(me).Item.IsNull)
            return true;
        Send(new StowCommand(me));
        return false;
    }

    /// <summary>The work key at a station, from beside it.</summary>
    private bool UseAt(Simulation sim, Entity me, Entity station, long now) =>
        AtStation(sim, me, station, now, s =>
        {
            Send(new AvatarWorkCommand(me, true, sim.World.Get<Thing>(s).Cell));
            Send(new AvatarWorkCommand(me, false));
        });

    // ---------------- The kitchen's fires ----------------

    /// <summary>The fire a pot or the spit hangs by (the kitchen's fireplace, or the oven beside it); Entity.Null if none.</summary>
    private static Entity FireBy(Simulation sim, Entity station)
    {
        var w = sim.World;
        if (station.IsNull || !w.TryGet<Thing>(station, out var t))
            return Entity.Null;
        foreach (var d in Maps.Map.Neighbors8)
        {
            var c = new int3(t.Cell.X + d.X, t.Cell.Y + d.Y, t.Cell.Z);
            if (sim.Map.InBounds(c) && sim.Map.Structure[sim.Map.Idx(c)] is { IsNull: false } s && !w.Has<Construction>(s)
                && w.Get<Thing>(s).Def.Building is { Fuel: not null, HeatOutput: > 0 })
                return s;
        }
        return Entity.Null;
    }

    /// <summary>
    /// The oven fed. With dough or a pie on it the work key there would put that in instead, so
    /// they're put by in the larder first.
    /// </summary>
    private bool FeedOven(Simulation sim, Entity me, PerceptionView view, Entity oven, long now)
    {
        var w = sim.World;
        var item = AvatarSystem.SupplyItem(sim, me, e => w.Has<Dough>(e) || w.Has<RawPie>(e));
        if (item.IsNull)
            return FeedFire(sim, me, view, oven, now);
        if (w.Get<Carrying>(me).Item == item)
        {
            Send(new StowCommand(me));
            return true;
        }
        Doing = "putting the dough by to feed the oven";
        return AtStation(sim, me, Larder(sim, me), now, l => Send(new StoreFromPackCommand(me, l, item)));
    }

    /// <summary>Feed a kitchen fire: logs from the basket, then into it from the front. False if there's no wood to be had.</summary>
    private bool FeedFire(Simulation sim, Entity me, PerceptionView view, Entity fire, long now)
    {
        var w = sim.World;
        var thing = w.Get<Thing>(fire);
        var props = thing.Def.Building!.Fuel!;
        bool IsFuel(Entity e) => w.IsAlive(e) && props.Takes(w.Get<Thing>(e).Def);
        Doing = $"feeding the {thing.Def.Label}";
        var pack = sim.PackOf(me);
        if (IsFuel(w.Get<Carrying>(me).Item) || (!pack.IsNull && sim.StoredIn(pack).Any(IsFuel)))
            return AtFire(sim, me, fire, now, f =>
            {
                Send(new AvatarWorkCommand(me, true, w.Get<Thing>(f).Cell));
                Send(new AvatarWorkCommand(me, false));
            });
        // The fire's own store (Work.FuelStores): the kitchen's log basket, the common room's or the shrine's rack.
        var basket = Sim.Work.FuelStores.For(sim, fire) is { IsNull: false } store ? store : Part(sim, me, "LogBasket");
        if (basket.IsNull || !sim.StoredIn(basket).Any(IsFuel))
        {
            Need(sim, me, sim.Defs.Thing(props.Def).Label, now);
            return false;
        }
        return AtStation(sim, me, basket, now, b =>
        {
            if (view.InContainer(b).FirstOrDefault(IsFuel, Entity.Null) is { IsNull: false } logs)
                Send(new TakeFromStorageCommand(me, logs, LogsAtOnce));
        });
    }

    /// <summary>At a fire: a fireplace set in the wall is worked from the room in front of it, anything else from beside it.</summary>
    private bool AtFire(Simulation sim, Entity me, Entity fire, long now, Action<Entity> use)
    {
        var thing = sim.World.Get<Thing>(fire);
        if (thing.Def.Building is not { SetInWall: true })
            return AtStation(sim, me, fire, now, use);
        var front = Environment.Warmth.FireCell(thing);
        var here = sim.World.Get<Position>(me).Cell;
        if (here == front || (AvatarSystem.InReach(sim, here, fire) && AvatarSystem.InReach(here, front)))
        {
            use(fire);
            return true;
        }
        if (now - _lastWalk >= ThinkTicks * 2)
        {
            Send(new AvatarGoToCommand(me, front));
            _lastWalk = now;
        }
        return true;
    }
}
