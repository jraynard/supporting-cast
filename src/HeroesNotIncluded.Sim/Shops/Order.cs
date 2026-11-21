using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Shops;

public enum OrderStatus : byte
{
    Open,
    Done,

    /// <summary>The asker gave up waiting.</summary>
    Expired,

    /// <summary>Called off by the asker, or they're gone.</summary>
    Cancelled,

    /// <summary>Turned away by the shop: "Sorry, we're out" (<see cref="ShopSystem.TurnAway"/>).</summary>
    Refused,
}

/// <summary>
/// One adventurer's order: who wants what, where, for how much and until when (docs/DESIGN.md,
/// "Shops and the line"). Nobody sees the list: an order is known only to those who heard it
/// asked (<see cref="KnownBy"/>), through <c>Perception</c>.
/// </summary>
public sealed class Order
{
    public int Id { get; init; }
    public required ServiceDef Kind { get; init; }
    public Entity Poster { get; init; } = Entity.Null;

    /// <summary>The asker's name as it was when they asked, so the list reads right after they're gone.</summary>
    public string PosterName { get; init; } = "";

    /// <summary>What was asked, in the asker's words.</summary>
    public string Text { get; init; } = "";

    /// <summary>The building it's to be done at; Entity.Null for anywhere.</summary>
    public Entity Where { get; init; } = Entity.Null;

    /// <summary>The thing in question (the blade to mend); Entity.Null if none. A piece asked for by name gets one once it's handed over.</summary>
    public Entity Subject { get; set; } = Entity.Null;

    /// <summary>
    /// Silver promised on completion: the shop's price when they ordered, whatever the sign says
    /// since. The inn's hamper is settled on delivery by what's in it (<c>Hospitality.Provisions.Charge</c>).
    /// </summary>
    public int Price { get; set; }

    /// <summary>What it was worth to them when they ordered; paying less is a bargain they think well of. 0 if unknown.</summary>
    public int Worth { get; init; }

    public long PostedTick { get; init; }
    public long Deadline { get; set; }

    /// <summary>Whoever said they'd see to it; Entity.Null until someone does.</summary>
    public Entity Assignee { get; set; } = Entity.Null;
    public string AssigneeName { get; set; } = "";

    public OrderStatus Status { get; set; }
    public long ClosedTick { get; set; }

    /// <summary>When the asker last said it aloud (they repeat themselves while nobody takes it).</summary>
    public long LastAsked { get; set; }

    /// <summary>For a meal: the flavour they said they fancied, if they said (<c>Hospitality.Tastes</c>).</summary>
    public Work.FlavourKind? Craving { get; set; }

    /// <summary>For a meal: the dish they asked for by name, at its price (<c>Shops.Menu</c>); null for whatever's up.</summary>
    public ThingDef? Dish { get; set; }

    /// <summary>
    /// For goods: the kind of piece they asked a shop to make, by name, when nothing on show suited
    /// them (<see cref="Goods.Wishes"/>); null for one bought off the shelf. Handing one over fills it,
    /// and so does putting one on the shop's shelf (they come and buy it).
    /// </summary>
    public ThingDef? Wants { get; set; }

    /// <summary>For goods asked for by name: a charm's mending (<see cref="Wants"/>); null for anything else.</summary>
    public MendingDef? WantsMending { get; set; }

    /// <summary>For a meal: they asked for the house dish by name (<c>Work.HouseDish</c>).</summary>
    public bool WantsHouse { get; set; }

    /// <summary>
    /// For healing: asked to be mended, not just bound (a player's, at a shrine pallet), so it's
    /// done when the wounds are, not when the bleeding stops (<c>Healing.LayOnHands.Hurt</c>).
    /// </summary>
    public bool Mend { get; set; }

    /// <summary>
    /// A commission (<see cref="Commissions"/>): pinned to the notice board, not asked aloud, for
    /// <see cref="Count"/> of <see cref="Wants"/>, made by whoever keeps the shop that makes them
    /// (<see cref="Where"/>) and paid when the whole lot's been brought.
    /// </summary>
    public bool Commission { get; set; }

    /// <summary>How many of <see cref="Wants"/> are wanted: 1 for a piece asked for by name.</summary>
    public int Count { get; set; } = 1;

    /// <summary>How many of them have been brought so far (a commission's lot can come in parts).</summary>
    public int Delivered { get; set; }

    /// <summary>Who knows of it, in the order they learned.</summary>
    public List<Entity> KnownBy { get; } = [];

    public bool IsOpen => Status == OrderStatus.Open;
}

/// <summary>
/// A customer you owe a visit (<c>PerceptionView.Awaiting</c>): an order you took on whose thing
/// changes hands, the customer still to hand it over or waiting to have it back. <see cref="At"/>
/// is where they are now: they said where they'd be, and you keep an eye out for your customers.
/// <see cref="Ready"/>: you have it and it looks done to you (a blade keen and sound, a box open,
/// a curio you've looked over), so it's time to take it back.
/// </summary>
public readonly record struct KnownErrand(int Order, ServiceKind Kind, Entity Who, string Name, int3 At, Entity Item, bool Ready)
{
    /// <summary>You have their thing: what's left is taking it back.</summary>
    public bool Returning => !Item.IsNull;
}

/// <summary>
/// An order as someone who knows of it sees it (<c>PerceptionView.Shops</c>). <see cref="Poster"/>
/// is who asked: they heard the voice, so they know whom to go to. <see cref="KindLabel"/> is what
/// it's for, the piece named where there is one ("their spiked club mended", "a charm of burns");
/// <see cref="Icon"/> that piece's sprite, or "" for the service's own icon. A piece asked for by name
/// (<see cref="Order.Wants"/>) is what was said aloud: <see cref="Wants"/>, its mending if a charm, and
/// <see cref="Where"/>, the shop it was asked of. A commission (<see cref="Commission"/>) was read off
/// the notice board, for <see cref="Count"/> of them, <see cref="Delivered"/> brought so far.
/// </summary>
public readonly record struct KnownOrder(
    int Id, ServiceKind Kind, string KindLabel, string PosterName, string Text, int Price,
    long PostedTick, long Deadline, string AssigneeName, OrderStatus Status, bool Mine, bool MineToDo, Entity Poster, string Icon = "",
    Defs.ThingDef? Wants = null, Defs.MendingDef? WantsMending = null, Entity Where = default,
    bool Commission = false, int Count = 1, int Delivered = 0)
{
    /// <summary>How many of <see cref="Wants"/> are still to be brought.</summary>
    public int Left => Math.Max(0, Count - Delivered);
}
