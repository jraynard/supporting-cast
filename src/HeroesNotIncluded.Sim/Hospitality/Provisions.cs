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
/// The cook supplies the inn, where a town keeps them apart (docs/DESIGN.md, "Cook"; Reedmere's
/// cookhouse on the shore, its inn out at the landing). The innkeeper orders a hamper at the
/// cookhouse hatch (<see cref="ServiceKind.Provisions"/>, paid from their own purse), the cook packs
/// one with dishes at the prep table and puts it up, and the innkeeper carries it back in both arms
/// and unpacks it onto the inn's warming shelf. From the shelf the inn's lodgers have their board
/// (a <see cref="ServiceKind.Meal"/> order at the inn itself, at no charge, served by the innkeeper)
/// and quest parties take rations for the road; an empty shelf makes a room worth less.
/// Where the tavern and the kitchen are one building (Millbrook's inn) none of it applies:
/// <see cref="Split"/> is false and nothing here changes a thing.
/// </summary>
public static class Provisions
{
    /// <summary>The thing def a hamper is made as.</summary>
    public const string HamperDef = "Hamper";

    /// <summary>Servings a hamper holds: what the cook packs to fill an order.</summary>
    public const int Holds = 6;

    /// <summary>The AI innkeeper orders another hamper once the shelf has fewer servings than this.</summary>
    public const int ShelfLow = 2;

    /// <summary>What a room's worth to an adventurer when the shelf's bare: no board to be had with it.</summary>
    public const float BareShelfWorth = 0.7f;

    /// <summary>How much further gone a slack-tied hamper's dishes come out (of the way to spoiled).</summary>
    public const float LooseRot = 0.3f;

    /// <summary>The words a lodger asks for their board in.</summary>
    public const string BoardAsk = "Supper's in with the room, I hope. Whatever's on the warming shelf.";

    /// <summary>Do the tavern and the kitchen stand apart in this town? Everything here waits on it.</summary>
    public static bool Split(Simulation sim)
    {
        var inn = ShopSystem.BuildingFor(sim, TownRole.Tavern);
        if (inn.IsNull)
            return false;
        var kitchen = ShopSystem.BuildingFor(sim, TownRole.Kitchen);
        return !kitchen.IsNull && kitchen != inn;
    }

    /// <summary>The inn a cookhouse supplies, Entity.Null unless they're apart.</summary>
    public static Entity Inn(Simulation sim) => Split(sim) ? ShopSystem.BuildingFor(sim, TownRole.Tavern) : Entity.Null;

    /// <summary>The cookhouse that supplies the inn, Entity.Null unless they're apart.</summary>
    public static Entity Cookhouse(Simulation sim) => Split(sim) ? ShopSystem.BuildingFor(sim, TownRole.Kitchen) : Entity.Null;

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
    public static Entity Shelf(Simulation sim) => ShelfOf(sim, Inn(sim));

    public static bool IsHamper(Simulation sim, Entity e) =>
        !e.IsNull && sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Name == HamperDef;

    /// <summary>What goes in a hamper: a cooked dish, or rations for the road.</summary>
    public static bool Packable(ThingDef d) => d.Rations || (d.Cooked && d.Nutrition > 0 && !d.Drink);

    /// <summary>Servings (or bundles) in a container: a hamper or the shelf.</summary>
    public static int Servings(Simulation sim, Entity container)
    {
        if (container.IsNull)
            return 0;
        var n = 0;
        foreach (var e in sim.StoredIn(container))
            if (Packable(sim.World.Get<Thing>(e).Def))
                n += sim.World.Get<Stack>(e).Count;
        return n;
    }

    /// <summary>The servings on the inn's shelf; 0 where there's none.</summary>
    public static int OnShelf(Simulation sim) => Servings(sim, Shelf(sim));

    /// <summary>The town's split, its inn has a shelf, and nothing's on it: a room's worth less.</summary>
    public static bool ShelfBare(Simulation sim) => Shelf(sim) is { IsNull: false } shelf && Servings(sim, shelf) == 0;

    /// <summary>Is this the inn's keeper: their trade works from the tavern (where it's apart from the kitchen)?</summary>
    public static bool KeepsInn(Simulation sim, Entity who) =>
        sim.World.TryGet<Trade>(who, out var t) && t.Def.Role == TownRole.Tavern && Split(sim);

    /// <summary>Does this person cook at the cookhouse (where it's apart from the inn)?</summary>
    public static bool Cooks(Simulation sim, Entity who) =>
        sim.World.TryGet<Trade>(who, out var t) && t.Def.Role == TownRole.Kitchen && t.Def.Serving(ServiceKind.Provisions) && Split(sim);

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

    /// <summary>A packed hamper is up on the cookhouse hatch already, waiting to be collected.</summary>
    public static bool HamperUp(Simulation sim)
    {
        var hatch = Hatch.Of(sim, Cookhouse(sim));
        return !hatch.IsNull && sim.StoredIn(hatch).Exists(e => IsHamper(sim, e));
    }

    // ---------------- Ordering ----------------

    /// <summary>
    /// Why this person can't order a hamper at the cookhouse now; null if they can. Only the inn's
    /// keeper orders, only where the inn and the cookhouse stand apart, one at a time, and only
    /// with the silver to pay for it.
    /// </summary>
    public static string? Refusal(Simulation sim, Entity who)
    {
        if (!Split(sim))
            return "the inn has its own kitchen";
        if (!KeepsInn(sim, who))
            return "the inn's keeper orders its provisions";
        if (!sim.Round.Live)
            return "nobody's serving yet";
        if (Ordered(sim, who) != null)
            return "you've a hamper on order already";
        if (ShopPrices.Posted(sim, Cookhouse(sim), ServiceKind.Provisions) is not { Offered: true } sign)
            return "the cook isn't packing hampers";
        var spare = Math.Max(0, Coin.Count(sim, who) - sim.Shops.All.Where(o => o.IsOpen && o.Poster == who).Sum(o => o.Price));
        return sign.Price > spare ? $"a hamper is {sign.Price} silver, and you have {spare} to spare" : null;
    }

    /// <summary>The inn's keeper orders a hamper at the cookhouse, at the price on its sign. Null if ordered; else why not.</summary>
    public static string? Order(Simulation sim, Entity who)
    {
        if (Refusal(sim, who) is { } why)
            return why;
        var cookhouse = Cookhouse(sim);
        var def = sim.Defs.Service(ServiceKind.Provisions);
        var price = ShopPrices.Posted(sim, cookhouse, ServiceKind.Provisions)!.Value.Price;
        var text = def.Lines.Count == 0 ? $"A hamper for the inn, {price} silver." : string.Format(def.Lines[0], price, "");
        var order = sim.Shops.Post(sim, who, def, text, price, sim.Clock.Tick + SimClock.HoursToTicks(def.PatienceHours), Entity.Null, cookhouse, price);
        Hatch.TryCollect(sim, order); // one already up and nobody ahead: in hand at once
        return null;
    }

    // ---------------- Packing ----------------

    /// <summary>Dishes on someone (in hand first, then the pack) that would go in a hamper.</summary>
    public static List<Entity> DishesOn(Simulation sim, Entity who)
    {
        var w = sim.World;
        var list = new List<Entity>();
        if (w.TryGet<Carrying>(who, out var c) && !c.Item.IsNull && w.IsAlive(c.Item) && Packable(w.Get<Thing>(c.Item).Def))
            list.Add(c.Item);
        if (sim.PackOf(who) is { IsNull: false } pack)
            foreach (var item in sim.StoredIn(pack))
                if (Packable(w.Get<Thing>(item).Def))
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
        TakeInArms(sim, to, hamper);
        shops.Complete(sim, order, giver);
        return true;
    }

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

    /// <summary>The hamper someone holds, if they're at a warming shelf with one.</summary>
    public static Entity HamperFor(Simulation sim, Entity who, Entity shelf)
    {
        var held = sim.World.Get<Carrying>(who).Item;
        return IsShelf(sim, shelf) && IsHamper(sim, held) ? held : Entity.Null;
    }

    /// <summary>The work key's prompt at a warming shelf with a hamper in your arms; null otherwise.</summary>
    public static string? StepAt(Simulation sim, Entity who, Entity shelf) =>
        HamperFor(sim, who, shelf).IsNull ? null : $"Unpack the hamper onto the {sim.World.Get<Thing>(shelf).Label}";

    /// <summary>
    /// Unpack the hamper in someone's arms onto a warming shelf: every dish that fits goes up, a
    /// slack lid's further gone than it looks; the cloth and basket are folded away. False if they
    /// had none, or weren't in reach.
    /// </summary>
    public static bool Unpack(Simulation sim, Entity who, Entity shelf)
    {
        var w = sim.World;
        var hamper = HamperFor(sim, who, shelf);
        if (hamper.IsNull || !AvatarSystem.InReach(sim, w.Get<Position>(who).Cell, shelf))
            return false;
        var loose = w.Get<Hamper>(hamper).Loose;
        var count = 0;
        foreach (var dish in sim.StoredIn(hamper))
        {
            if (loose && w.Has<Rot>(dish))
                w.Get<Rot>(dish).Progress = Math.Min(1f, w.Get<Rot>(dish).Progress + LooseRot);
            var n = w.Get<Stack>(dish).Count;
            var taken = sim.TakeFromStack(dish, n);
            var left = sim.Store(shelf, taken);
            count += n - (left.IsNull ? 0 : w.Get<Stack>(left).Count);
            if (!left.IsNull)
                sim.PlaceItemNear(left, w.Get<Thing>(shelf).Cell, byHand: true);
        }
        w.Get<Carrying>(who).Item = Entity.Null;
        w.Destroy(hamper);
        sim.Messages.Tell(who, MessageKind.Info, $"You unpack {count} {(count == 1 ? "serving" : "servings")} onto the {w.Get<Thing>(shelf).Label}.", w.Get<Thing>(shelf).Cell);
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

    // ---------------- The road ----------------

    /// <summary>
    /// A member setting out with no rations of their own takes a bundle from the inn's shelf, if
    /// it has one (<c>Expeditions.ExpeditionSystem</c>). True if they did.
    /// </summary>
    public static bool TakeRations(Simulation sim, Entity who)
    {
        if (Work.Rations.Carried(sim, who).Count > 0 || Shelf(sim) is not { IsNull: false } shelf)
            return false;
        foreach (var e in sim.StoredIn(shelf))
            if (sim.World.Has<Work.Ration>(e))
            {
                Inventory.Stow(sim, who, sim.TakeFromStack(e, 1));
                return true;
            }
        return false;
    }

    // ---------------- Speech ----------------

    /// <summary>The cook calls the hamper up through the hatch.</summary>
    public static void CallUp(Simulation sim, Entity cook) =>
        sim.Speech.Say(sim, cook, "Hamper's up for the inn!", Volume.Say);
}
