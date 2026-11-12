using SupportingCast.Sim.Defs;

namespace SupportingCast.Sim.Shops;

public enum OrderStatus : byte
{
    Open,
    Done,

    /// <summary>The asker gave up waiting.</summary>
    Expired,

    /// <summary>Called off by the asker, or they're gone.</summary>
    Cancelled,
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

    /// <summary>The thing in question (the blade to mend); Entity.Null if none.</summary>
    public Entity Subject { get; init; } = Entity.Null;

    /// <summary>Silver promised on completion: the shop's price when they ordered, whatever the sign says since.</summary>
    public int Price { get; init; }

    /// <summary>What it was worth to them when they ordered; paying less is a bargain they think well of. 0 if unknown.</summary>
    public int Worth { get; init; }

    public long PostedTick { get; init; }
    public long Deadline { get; init; }

    /// <summary>Whoever said they'd see to it; Entity.Null until someone does.</summary>
    public Entity Assignee { get; set; } = Entity.Null;
    public string AssigneeName { get; set; } = "";

    public OrderStatus Status { get; set; }
    public long ClosedTick { get; set; }

    /// <summary>When the asker last said it aloud (they repeat themselves while nobody takes it).</summary>
    public long LastAsked { get; set; }

    /// <summary>Who knows of it, in the order they learned.</summary>
    public List<Entity> KnownBy { get; } = [];

    public bool IsOpen => Status == OrderStatus.Open;
}

/// <summary>
/// An order as someone who knows of it sees it (<c>PerceptionView.Shops</c>). <see cref="Poster"/>
/// is who asked: they heard the voice, so they know whom to go to.
/// </summary>
public readonly record struct KnownOrder(
    int Id, ServiceKind Kind, string KindLabel, string PosterName, string Text, int Price,
    long PostedTick, long Deadline, string AssigneeName, OrderStatus Status, bool Mine, bool MineToDo, Entity Poster);
