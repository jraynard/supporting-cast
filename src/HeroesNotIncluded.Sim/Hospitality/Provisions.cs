using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Hospitality;

/// <summary>
/// A hamper as the cook tied it (<see cref="Work.PackSession"/>): who packed it, and, hidden, a lid
/// tied slack. A slack lid lets the warmth out and the air in, and what comes out of it at the inn
/// is further gone than it looks (<see cref="Provisions.Unpack"/>). Its dishes are
/// <see cref="Stored"/> in it, each keeping its own rot, read as any food's.
/// </summary>
public struct Hamper
{
    public Entity Maker;

    /// <summary>Hidden: tied slack. Never shown; it shows only as food that turns sooner.</summary>
    public bool Loose;
}

/// <summary>
/// The cook supplies the inn (docs/DESIGN.md, "Cook"): the cook doesn't sell to the parties, the
/// innkeeper does. The innkeeper orders a hamper (<see cref="ServiceKind.Provisions"/>, paid from
/// their own purse) from behind the bar (<see cref="OrderProvisionsCommand"/>) or at the kitchen's
/// hatch; the cook packs one at the prep table and puts it up on the hatch. It's unpacked at the
/// inn: rations for the road onto the merchandise shelf, for sale (<see cref="Merchandise"/>), and,
/// where the town keeps the kitchen apart (<see cref="Split"/>: Reedmere's cookhouse on the shore,
/// its inn out at the landing), dishes onto the warming shelf for the lodgers' board (a
/// <see cref="ServiceKind.Meal"/> order at the inn itself, at no charge, served by the innkeeper).
/// An empty warming shelf makes a room worth less. Where the inn has its kitchen (Millbrook) the
/// innkeeper takes the hamper off its own hatch; where it's apart, a labourer runs it over
/// (<see cref="HamperRunJob"/>) for a small fee, and the innkeeper fetches it only with no labourer about.
/// </summary>
public static class Provisions
{
    /// <summary>The thing def a hamper is made as.</summary>
    public const string HamperDef = "Hamper";

    /// <summary>Servings a hamper holds: what the cook packs to fill an order.</summary>
    public const int Holds = 6;

    /// <summary>The AI innkeeper orders another hamper once the warming shelf has fewer servings than this.</summary>
    public const int ShelfLow = 2;

    /// <summary>The AI innkeeper orders another hamper once the merchandise shelf has fewer rations than this.</summary>
    public const int RationsLow = 2;

    /// <summary>Silver the innkeeper pays a labourer for running the hamper over from the cookhouse.</summary>
    public const int RunFee = 2;

    /// <summary>What a room's worth to an adventurer when the shelf's bare: no board to be had with it.</summary>
    public const float BareShelfWorth = 0.7f;

    /// <summary>How much further gone a slack-tied hamper's dishes come out (of the way to spoiled).</summary>
    public const float LooseRot = 0.3f;

    /// <summary>The words a lodger asks for their board in.</summary>
    public const string BoardAsk = "Supper's in with the room, I hope. Whatever's on the warming shelf.";

    /// <summary>Has this town an inn and a kitchen, under one roof or apart? Every hamper waits on it.</summary>
    public static bool Active(Simulation sim) =>
        !ShopSystem.BuildingFor(sim, TownRole.Tavern).IsNull && !ShopSystem.BuildingFor(sim, TownRole.Kitchen).IsNull;

    /// <summary>Do the tavern and the kitchen stand apart in this town? The board, the warming shelf and the labourer's run wait on it.</summary>
    public static bool Split(Simulation sim)
    {
        var inn = ShopSystem.BuildingFor(sim, TownRole.Tavern);
        if (inn.IsNull)
            return false;
        var kitchen = ShopSystem.BuildingFor(sim, TownRole.Kitchen);
        return !kitchen.IsNull && kitchen != inn;
    }

    /// <summary>The inn the kitchen supplies, Entity.Null unless the town has both.</summary>
    public static Entity Inn(Simulation sim) => Active(sim) ? ShopSystem.BuildingFor(sim, TownRole.Tavern) : Entity.Null;

    /// <summary>The kitchen that supplies the inn (a cookhouse, or the inn's own), Entity.Null unless the town has both.</summary>
    public static Entity Cookhouse(Simulation sim) => Active(sim) ? ShopSystem.BuildingFor(sim, TownRole.Kitchen) : Entity.Null;

    /// <summary>A warming shelf (<see cref="BuildingProps.Warming"/>), finished.</summary>
    public static bool IsShelf(Simulation sim, Entity e) =>
        !e.IsNull && sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Building is { Warming: true } && !sim.World.Has<Construction>(e);

    /// <summary>A building's warming shelf, or Entity.Null.</summary>
    public static Entity ShelfOf(Simulation sim, Entity inn)
    {
        if (inn.IsNull || !sim.World.IsAlive(inn) || !sim.World.Has<PresetBuilding>(inn))
            return Entity.Null;
        foreach (var part in Buildings.Parts(sim, inn))
            if (IsShelf(sim, part))
                return part;
        return Entity.Null;
    }

    /// <summary>The inn's warming shelf where the town's split, else Entity.Null.</summary>
    public static Entity Shelf(Simulation sim) => Split(sim) ? ShelfOf(sim, Inn(sim)) : Entity.Null;

    /// <summary>The inn's merchandise shelf: the innkeeper's wares shelf, where the hampers' rations go on sale. Entity.Null if none.</summary>
    public static Entity Merchandise(Simulation sim)
    {
        var inn = Inn(sim);
        if (inn.IsNull)
            return Entity.Null;
        foreach (var part in Buildings.Parts(sim, inn).OrderBy(e => e.Index))
            if (Goods.IsDisplay(sim, part) && sim.World.Get<Thing>(part).Def.Building!.DisplayRole is TownRole.Tavern or TownRole.None)
                return part;
        return Entity.Null;
    }

    /// <summary>Rations on the inn's merchandise shelf.</summary>
    public static int RationsShelved(Simulation sim) =>
        Merchandise(sim) is { IsNull: false } shelf ? sim.StoredIn(shelf).Count(e => sim.World.Get<Thing>(e).Def.Rations) : 0;

    /// <summary>The inn wants a hamper: its merchandise shelf is short of rations, or (apart) its warming shelf of dishes.</summary>
    public static bool WantsSupplies(Simulation sim) =>
        (!Merchandise(sim).IsNull && RationsShelved(sim) < RationsLow) || (!Shelf(sim).IsNull && OnShelf(sim) < ShelfLow);

    /// <summary>A shelf a hamper's unpacked at: the warming shelf, or the inn's merchandise shelf.</summary>
    public static bool IsUnpackShelf(Simulation sim, Entity e) =>
        IsShelf(sim, e) || (!e.IsNull && e == Merchandise(sim));

    /// <summary>Where someone with a hamper unpacks it: the warming shelf where there's one, else the merchandise shelf.</summary>
    public static Entity UnpackAt(Simulation sim) => Shelf(sim) is { IsNull: false } warm ? warm : Merchandise(sim);

    public static bool IsHamper(Simulation sim, Entity e) =>
        !e.IsNull && sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Name == HamperDef;

    /// <summary>What goes in a hamper: rations for the road, and where the inn's kitchen is apart a cooked dish for the board.</summary>
    public static bool Packable(Simulation sim, ThingDef d) => d.Rations || (IsDish(d) && Split(sim));

    /// <summary>A cooked dish (not a drink): the lodgers' board.</summary>
    public static bool IsDish(ThingDef d) => d.Cooked && d.Nutrition > 0 && !d.Drink && !d.Rations;

    /// <summary>Servings (or bundles) in a container: a hamper or the shelf.</summary>
    public static int Servings(Simulation sim, Entity container)
    {
        if (container.IsNull)
            return 0;
        var n = 0;
        foreach (var e in sim.StoredIn(container))
            if (sim.World.Get<Thing>(e).Def is var d && (d.Rations || IsDish(d)))
                n += sim.World.Get<Stack>(e).Count;
        return n;
    }

    /// <summary>The servings on the inn's warming shelf; 0 where there's none.</summary>
    public static int OnShelf(Simulation sim) => Servings(sim, Shelf(sim));

    /// <summary>The town's split, its inn has a shelf, and nothing's on it: a room's worth less.</summary>
    public static bool ShelfBare(Simulation sim) => Shelf(sim) is { IsNull: false } shelf && Servings(sim, shelf) == 0;

    /// <summary>Is this the inn's keeper: their trade works from the tavern (in a town with a kitchen to supply it)?</summary>
    public static bool KeepsInn(Simulation sim, Entity who) =>
        sim.World.TryGet<Trade>(who, out var t) && t.Def.Role == TownRole.Tavern && Active(sim);

    /// <summary>Does this person cook for the inn: their trade keeps the kitchen and packs its hampers?</summary>
    public static bool Cooks(Simulation sim, Entity who) =>
        sim.World.TryGet<Trade>(who, out var t) && t.Def.Role == TownRole.Kitchen && t.Def.Serving(ServiceKind.Provisions) && Active(sim);

    /// <summary>The inn's keeper, of those alive (the first, by entity), or Entity.Null.</summary>
    public static Entity Keeper(Simulation sim)
    {
        foreach (var e in sim.Creatures())
            if (KeepsInn(sim, e) && !sim.World.Get<Creature>(e).Dead)
                return e;
        return Entity.Null;
    }

    /// <summary>The open provisions order this person posted, if any.</summary>
    public static Order? Ordered(Simulation sim, Entity who) => sim.Shops.OpenOrderOf(who, ServiceKind.Provisions);

    /// <summary>The oldest open provisions order at the cookhouse that this person knows of.</summary>
    public static Order? Wanted(Simulation sim, Entity cook)
    {
        foreach (var o in sim.Shops.All)
            if (o.IsOpen && o.Kind.Kind == ServiceKind.Provisions && o.KnownBy.Contains(cook) && o.Poster != cook)
                return o;
        return null;
    }

    /// <summary>A packed hamper is up on the kitchen's hatch already, waiting to be collected.</summary>
    public static bool HamperUp(Simulation sim)
    {
        var hatch = Hatch.Of(sim, Cookhouse(sim));
        return !hatch.IsNull && sim.StoredIn(hatch).Exists(e => IsHamper(sim, e));
    }

    // ---------------- Ordering ----------------

    /// <summary>
    /// Why this person can't order a hamper from the cook now; null if they can. Only the inn's
    /// keeper orders, one at a time, and only with the silver to pay for it.
    /// </summary>
    public static string? Refusal(Simulation sim, Entity who)
    {
        if (!Active(sim))
            return "there's no kitchen to send to";
        if (!KeepsInn(sim, who))
            return "the inn's keeper orders its provisions";
        if (!sim.Round.Live)
            return "nobody's serving yet";
        if (Ordered(sim, who) != null)
            return "you've a hamper on order already";
        if (ShopPrices.Posted(sim, Cookhouse(sim), ServiceKind.Provisions) is not { Offered: true } sign)
            return "the cook isn't packing hampers";
        if (Hatch.Of(sim, Cookhouse(sim)).IsNull)
            return "the kitchen has no hatch to put a hamper up on";
        var spare = Math.Max(0, Coin.Count(sim, who) - sim.Shops.All.Where(o => o.IsOpen && o.Poster == who).Sum(o => o.Price));
        return sign.Price > spare ? $"a hamper is {sign.Price} silver, and you have {spare} to spare" : null;
    }

    /// <summary>
    /// The inn's keeper orders a hamper from the cook, at the price on the kitchen's sign: word goes
    /// to the kitchen (its cook knows of it at once, <see cref="ShopSystem.Post"/>), wherever it's
    /// asked from. Null if ordered; else why not.
    /// </summary>
    public static string? Order(Simulation sim, Entity who)
    {
        if (Refusal(sim, who) is { } why)
            return why;
        var cookhouse = Cookhouse(sim);
        var def = sim.Defs.Service(ServiceKind.Provisions);
        var price = ShopPrices.Posted(sim, cookhouse, ServiceKind.Provisions)!.Value.Price;
        var text = def.Lines.Count == 0 ? $"A hamper for the inn, {price} silver." : string.Format(def.Lines[0], price, "");
        var order = sim.Shops.Post(sim, who, def, text, price, sim.Clock.Tick + SimClock.HoursToTicks(def.PatienceHours), Entity.Null, cookhouse, price);
        foreach (var cook in sim.Creatures())
            if (Cooks(sim, cook) && cook != who && sim.World.Has<PlayerCharacter>(cook))
                sim.Messages.Tell(cook, MessageKind.Info, $"{sim.World.Get<Creature>(who).Name} sends word from the inn: a hamper wanted, {price} silver. Pack it at the prep table and put it up on the hatch.",
                    sim.World.Get<Position>(cook).Cell);
        Hatch.TryCollect(sim, order); // one already up and they're at the front: in hand at once
        return null;
    }

    /// <summary>
    /// Does the innkeeper wait at the hatch for this hamper order? Only once a hamper's up, nobody's
    /// running it, and (where the kitchen's apart) there's no labourer about to run it for them.
    /// </summary>
    public static bool Waits(Simulation sim, Order e) =>
        HamperUp(sim) && e.Assignee.IsNull && !(Split(sim) && RunnerAbout(sim));

    /// <summary>
    /// The innkeeper takes the hamper that's up off the hatch by hand, at the hatch (the order's done
    /// and paid). False if there's none up, or no order of theirs, or they're not in reach.
    /// </summary>
    public static bool Collect(Simulation sim, Entity who)
    {
        var hatch = Hatch.Of(sim, Cookhouse(sim));
        if (hatch.IsNull || Ordered(sim, who) is not { } order || !order.Assignee.IsNull || !Inventory.CanReachContainer(sim, who, hatch)
            || sim.StoredIn(hatch).FirstOrDefault(e => IsHamper(sim, e), Entity.Null) is not { IsNull: false } hamper)
            return false;
        var maker = sim.World.Get<Hamper>(hamper).Maker;
        return Received(sim, sim.World.IsAlive(maker) ? maker : who, who, sim.TakeFromStack(hamper, 1), sim.Shops);
    }

    /// <summary>The inn's bar (a counter) within this person's reach, or Entity.Null: where the keeper sends for a hamper from.</summary>
    public static Entity BarInReach(Simulation sim, Entity who)
    {
        var inn = Inn(sim);
        if (inn.IsNull || !sim.World.TryGet<Position>(who, out var at))
            return Entity.Null;
        foreach (var part in Buildings.Parts(sim, inn).OrderBy(e => e.Index))
            if (sim.World.Get<Thing>(part).Def.Building is { Bar: true } && !sim.World.Has<Construction>(part) && AvatarSystem.InReach(sim, at.Cell, part))
                return part;
        return Entity.Null;
    }

    // ---------------- The run (a labourer) ----------------

    /// <summary>The trade that runs hampers over (the labourer's: <see cref="TradeDef.Gathers"/>).</summary>
    public static bool Runs(Simulation sim, Entity who) =>
        sim.World.TryGet<Trade>(who, out var t) && t.Def.Gathers && sim.World.Get<Creature>(who) is { Dead: false, Downed: false };

    /// <summary>A labourer of the village's AI about to run the hamper (a player's may or may not: the keeper doesn't count on them).</summary>
    public static bool RunnerAbout(Simulation sim)
    {
        foreach (var e in sim.Creatures())
            if (Runs(sim, e) && !Players.Steered(sim, e) && sim.World.Get<Creature>(e).Faction == Faction.Village)
                return true;
        return false;
    }

    /// <summary>The open hamper order a hamper's up for, with nobody yet running it; null if none.</summary>
    public static Order? ToRun(Simulation sim)
    {
        if (!Split(sim) || !HamperUp(sim))
            return null;
        foreach (var o in sim.Shops.All)
            if (o.IsOpen && o.Kind.Kind == ServiceKind.Provisions && o.Assignee.IsNull)
                return o;
        return null;
    }

    /// <summary>The order this person's running a hamper for, if any.</summary>
    public static Order? Running(Simulation sim, Entity who)
    {
        foreach (var o in sim.Shops.All)
            if (o.IsOpen && o.Kind.Kind == ServiceKind.Provisions && o.Assignee == who && o.Poster != who)
                return o;
        return null;
    }

    /// <summary>
    /// A labourer takes the hamper off the kitchen's hatch to run it to the inn: into their arms, the
    /// order theirs to see through. False if there's none to run or they can't reach it.
    /// </summary>
    public static bool TakeToRun(Simulation sim, Entity who)
    {
        var hatch = Hatch.Of(sim, Cookhouse(sim));
        if (!Runs(sim, who) || ToRun(sim) is not { } order || order.Poster == who || hatch.IsNull || !Inventory.CanReachContainer(sim, who, hatch)
            || !sim.World.Get<Carrying>(who).Item.IsNull
            || sim.StoredIn(hatch).FirstOrDefault(e => IsHamper(sim, e), Entity.Null) is not { IsNull: false } hamper)
            return false;
        TakeInArms(sim, who, sim.TakeFromStack(hamper, 1));
        order.Assignee = who;
        order.AssigneeName = sim.World.Get<Creature>(who).Name;
        ShopSystem.Learn(order, who);
        sim.Messages.Tell(who, MessageKind.Info, $"You take the inn's hamper. Carry it over to the inn and unpack it at the {(Shelf(sim).IsNull ? "merchandise shelf" : "warming shelf")}: {order.PosterName} pays you {RunFee} silver.",
            sim.World.Get<Position>(who).Cell);
        return true;
    }

    /// <summary>The work key's prompt at the kitchen's hatch for a labourer with a hamper to run; null otherwise.</summary>
    public static string? RunPrompt(Simulation sim, Entity who, Entity hatch) =>
        Runs(sim, who) && !hatch.IsNull && hatch == Hatch.Of(sim, Cookhouse(sim)) && ToRun(sim) is { } o && o.Poster != who
        && sim.World.Get<Carrying>(who).Item.IsNull
            ? $"Run the inn's hamper over ({RunFee} silver from {o.PosterName})" : null;

    // ---------------- Packing ----------------

    /// <summary>Dishes (and rations) on someone (in hand first, then the pack) that would go in a hamper.</summary>
    public static List<Entity> DishesOn(Simulation sim, Entity who)
    {
        var w = sim.World;
        var list = new List<Entity>();
        if (w.TryGet<Carrying>(who, out var c) && !c.Item.IsNull && w.IsAlive(c.Item) && Packable(sim, w.Get<Thing>(c.Item).Def))
            list.Add(c.Item);
        if (sim.PackOf(who) is { IsNull: false } pack)
            foreach (var item in sim.StoredIn(pack))
                if (Packable(sim, w.Get<Thing>(item).Def))
                    list.Add(item);
        return list;
    }

    /// <summary>
    /// Pack a hamper from the dishes on the packer, up to <see cref="Holds"/> servings: into their
    /// arms if they're free, else set down beside them. Entity.Null if they had nothing to pack.
    /// </summary>
    public static Entity MakeHamper(Simulation sim, Entity packer, bool loose)
    {
        var w = sim.World;
        var dishes = DishesOn(sim, packer);
        if (dishes.Count == 0)
            return Entity.Null;
        var hamper = sim.CreateItem(sim.Defs.Thing(HamperDef), 1);
        w.Add(hamper, new Hamper { Maker = packer, Loose = loose });
        var left = Holds;
        foreach (var dish in dishes)
        {
            if (left <= 0)
                break;
            var t = w.Get<Thing>(dish);
            var n = Math.Min(left, Math.Min(w.Get<Stack>(dish).Count, sim.ContainerRoom(hamper, t.Def, t.Material)));
            if (n <= 0)
                continue;
            left -= n;
            var taken = Portion(sim, dish, n, packer);
            if (!sim.Store(hamper, taken).IsNull)
                throw new InvalidOperationException("room was counted, so it must fit");
        }
        ref var hands = ref w.Get<Carrying>(packer);
        if (hands.Item.IsNull)
            hands.Item = hamper;
        else
            sim.PlaceItemNear(hamper, w.Get<Position>(packer).Cell, byHand: true);
        return hamper;
    }

    /// <summary>
    /// A hamper handed to whoever ordered it (off the hatch, or from the cook's own hands): the order's
    /// done and paid. It goes into their arms, what they held into the pack or set down. True if it
    /// was theirs.
    /// </summary>
    internal static bool Received(Simulation sim, Entity giver, Entity to, Entity hamper, ShopSystem shops)
    {
        if (!IsHamper(sim, hamper) || shops.OpenOrderOf(to, ServiceKind.Provisions) is not { } order)
            return false;
        Charge(order, Servings(sim, hamper));
        TakeInArms(sim, to, hamper);
        shops.Complete(sim, order, giver);
        return true;
    }

    /// <summary>
    /// A hamper's paid for by what's in it: the sign's price is for a full one (<see cref="Holds"/>
    /// servings), so a hamper of two bundles of rations costs a third of it. At least a silver.
    /// </summary>
    public static void Charge(Order order, int servings) =>
        order.Price = Math.Min(order.Price, Math.Max(1, (int)MathF.Ceiling(order.Price * Math.Clamp(servings, 0, Holds) / (float)Holds)));

    /// <summary>What a serving costs the inn, by the kitchen's sign (a full hamper's price over what it holds); 0 if there's no sign.</summary>
    public static int PerServing(Simulation sim) =>
        ShopPrices.Posted(sim, Cookhouse(sim), ServiceKind.Provisions) is { } sign ? (int)MathF.Ceiling(sign.Price / (float)Holds) : 0;

    /// <summary>Something bulky into someone's arms: what they held goes into the pack, or is set down.</summary>
    public static void TakeInArms(Simulation sim, Entity who, Entity item)
    {
        var w = sim.World;
        ref var hands = ref w.Get<Carrying>(who);
        if (!hands.Item.IsNull)
        {
            var held = hands.Item;
            hands.Item = Entity.Null;
            var left = sim.PackOf(who) is { IsNull: false } pack ? sim.Store(pack, held) : held;
            if (!left.IsNull)
                sim.PlaceItemNear(left, w.Get<Position>(who).Cell, byHand: true);
        }
        w.Get<Carrying>(who).Item = item;
    }

    /// <summary>
    /// <paramref name="n"/> of a dish's stack as an item of its own, keeping who made it and how it
    /// tastes (a split stack would lose them). Out of <paramref name="from"/>'s hands if it's held there.
    /// </summary>
    public static Entity Portion(Simulation sim, Entity dish, int n, Entity? holder = null)
    {
        var from = holder ?? Entity.Null;
        var w = sim.World;
        var made = w.TryGet<Crafted>(dish, out var c) ? c : (Crafted?)null;
        var flavoured = w.TryGet<Work.Dish>(dish, out var d) ? d : (Work.Dish?)null;
        var taken = !from.IsNull && w.Has<Carrying>(from) ? Inventory.TakeFromPerson(sim, from, dish, n) : sim.TakeFromStack(dish, n);
        if (made is { } m && !w.Has<Crafted>(taken))
            w.Add(taken, m);
        if (flavoured is { } f && !w.Has<Work.Dish>(taken))
            w.Add(taken, f);
        return taken;
    }

    // ---------------- Unpacking ----------------

    /// <summary>
    /// The hamper someone holds, if they're at the inn's warming or merchandise shelf with one and
    /// it's theirs to unpack (the keeper's, or the labourer running it).
    /// </summary>
    public static Entity HamperFor(Simulation sim, Entity who, Entity shelf)
    {
        var held = sim.World.Get<Carrying>(who).Item;
        return IsUnpackShelf(sim, shelf) && IsHamper(sim, held) && (KeepsInn(sim, who) || Running(sim, who) != null) ? held : Entity.Null;
    }

    /// <summary>The work key's prompt at the inn's shelf with a hamper in your arms; null otherwise.</summary>
    public static string? StepAt(Simulation sim, Entity who, Entity shelf) =>
        HamperFor(sim, who, shelf).IsNull ? null : "Unpack the hamper at the inn";

    /// <summary>The price rations go on the merchandise shelf at: what the keeper asks for those there already, else the shelf's default.</summary>
    private static int RationPrice(Simulation sim, Entity keeper, Entity bundle)
    {
        if (Merchandise(sim) is { IsNull: false } shelf)
            foreach (var e in sim.StoredIn(shelf))
                if (e != bundle && sim.World.Get<Thing>(e).Def.Rations && sim.World.TryGet<ForSale>(e, out var s) && s.Seller == keeper)
                    return s.Price;
        return Math.Max(Goods.DefaultPrice(sim, bundle), PerServing(sim) + 1); // never less than the cook charged for it
    }

    /// <summary>
    /// Unpack the hamper in someone's arms at the inn: its rations onto the merchandise shelf, for
    /// sale at the keeper's price; its dishes onto the warming shelf; a slack lid's further gone than
    /// it looks; what won't fit is set down by the shelf; the cloth and basket are folded away. A
    /// labourer who ran it over finishes the order: the keeper pays the cook, and the runner their fee.
    /// False if they had none, or weren't in reach of the shelf.
    /// </summary>
    public static bool Unpack(Simulation sim, Entity who, Entity shelf)
    {
        var w = sim.World;
        var hamper = HamperFor(sim, who, shelf);
        if (hamper.IsNull || !AvatarSystem.InReach(sim, w.Get<Position>(who).Cell, shelf))
            return false;
        var run = Running(sim, who);
        if (run != null)
            Charge(run, Servings(sim, hamper));
        var keeper = KeepsInn(sim, who) ? who : run?.Poster ?? Keeper(sim);
        var loose = w.Get<Hamper>(hamper).Loose;
        var maker = w.Get<Hamper>(hamper).Maker;
        var merchandise = Merchandise(sim);
        var warming = Shelf(sim);
        var count = 0;
        foreach (var dish in sim.StoredIn(hamper))
        {
            if (loose && w.Has<Rot>(dish))
                w.Get<Rot>(dish).Progress = Math.Min(1f, w.Get<Rot>(dish).Progress + LooseRot);
            var n = w.Get<Stack>(dish).Count;
            var rations = w.Get<Thing>(dish).Def.Rations;
            var to = rations ? merchandise : warming;
            var taken = sim.TakeFromStack(dish, n);
            var left = to.IsNull ? taken : sim.Store(to, taken);
            count += n - (left.IsNull ? 0 : w.Get<Stack>(left).Count);
            if (rations && !to.IsNull && left != taken && w.IsAlive(taken) && w.Has<Stored>(taken) && !keeper.IsNull)
                Goods.Set(sim, keeper, taken, RationPrice(sim, keeper, taken), true);
            if (!left.IsNull)
                sim.PlaceItemNear(left, w.Get<Thing>(shelf).Cell, byHand: true);
        }
        w.Get<Carrying>(who).Item = Entity.Null;
        w.Destroy(hamper);
        sim.Messages.Tell(who, MessageKind.Info, $"You unpack {count} {(count == 1 ? "serving" : "servings")} at the inn"
            + (merchandise.IsNull ? "." : ": the rations go up on the merchandise shelf, for sale."), w.Get<Thing>(shelf).Cell);
        if (run != null)
        {
            sim.Shops.Complete(sim, run, w.IsAlive(maker) && maker != run.Poster ? maker : who);
            if (w.IsAlive(run.Poster))
                Coin.Pay(sim, run.Poster, who, RunFee);
        }
        return true;
    }

    // ---------------- Board ----------------

    /// <summary>
    /// The inn a lodger has their board at: they've a room there tonight, the town's split, and the
    /// shelf has a dish they can eat. Entity.Null otherwise (they order a meal like anyone).
    /// </summary>
    public static Entity BoardAt(Simulation sim, Entity who)
    {
        if (!Lodging.Lodged(sim, who) || Shelf(sim) is not { IsNull: false } shelf)
            return Entity.Null;
        var inn = Buildings.Of(sim, sim.World.Get<LodgeStay>(who).Bed);
        if (inn != Inn(sim) || DishFor(sim, shelf, who).IsNull)
            return Entity.Null;
        return inn;
    }

    /// <summary>Is this a lodger's board: a meal ordered at a building that isn't a kitchen (the inn, where it's apart)?</summary>
    public static bool IsBoard(Simulation sim, Order e) =>
        e.Kind.Kind == ServiceKind.Meal && sim.World.IsAlive(e.Where) && sim.World.TryGet<PresetBuilding>(e.Where, out var b) && !b.Def.Has(TownRole.Kitchen);

    /// <summary>A dish on a shelf that this diner can eat (a cooked one; rations are for the road), or Entity.Null.</summary>
    public static Entity DishFor(Simulation sim, Entity shelf, Entity diner)
    {
        var w = sim.World;
        if (shelf.IsNull || !w.TryGet<Creature>(diner, out var c))
            return Entity.Null;
        foreach (var e in sim.StoredIn(shelf))
            if (w.Get<Thing>(e).Def is { Cooked: true, Nutrition: > 0, Rations: false } d && d.EdibleBy(c.Def) && !sim.Reservations.IsReservedByOther(e, Entity.Null))
                return e;
        return Entity.Null;
    }

    // ---------------- Speech ----------------

    /// <summary>The cook calls the hamper up through the hatch.</summary>
    public static void CallUp(Simulation sim, Entity cook) =>
        sim.Speech.Say(sim, cook, "Hamper's up for the inn!", Volume.Say);
}

/// <summary>
/// The inn's keeper sends to the kitchen for a hamper from behind the bar (<see cref="Provisions.Order"/>):
/// no walk to the hatch to ask. A playtester couldn't see how the innkeeper got supplies from the cook.
/// </summary>
public sealed record OrderProvisionsCommand(Entity Avatar) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!w.IsAlive(Avatar) || !w.Has<Avatar>(Avatar) || !w.TryGet<Creature>(Avatar, out var c) || c.Dead || c.Downed
            || Provisions.BarInReach(sim, Avatar) is not { IsNull: false } bar)
            return;
        var at = w.Get<Thing>(bar).Cell;
        if (Provisions.Order(sim, Avatar) is { } why)
            sim.Messages.Tell(Avatar, MessageKind.Info, $"Can't send for a hamper: {why}.", at);
        else
            sim.Messages.Tell(Avatar, MessageKind.Good, Provisions.Split(sim)
                ? "You send word to the cookhouse for a hamper. It'll be put up on their hatch; a labourer will run it over if one's about."
                : "You send word to the kitchen for a hamper. It'll be put up on the serving hatch for you to take.", at);
    }
}
