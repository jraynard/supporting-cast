using SupportingCast.Sim.Defs;

namespace SupportingCast.Sim.Errands;

public enum ErrandStatus : byte
{
    Open,
    Done,

    /// <summary>The asker gave up waiting.</summary>
    Expired,

    /// <summary>Called off by the asker, or they're gone.</summary>
    Cancelled,
}

/// <summary>
/// One demand: who wants what, where, for how much and until when (docs/DESIGN.md, "Errands are
/// the shared spine"). Adventurers post them and players post them to each other. Nobody sees the
/// list: an errand is known only to those who heard it asked (<see cref="KnownBy"/>), through
/// <c>Perception</c>.
/// </summary>
public sealed class Errand
{
    public int Id { get; init; }
    public required ErrandKindDef Kind { get; init; }
    public Entity Poster { get; init; } = Entity.Null;

    /// <summary>The asker's name as it was when they asked, so the list reads right after they're gone.</summary>
    public string PosterName { get; init; } = "";

    /// <summary>What was asked, in the asker's words.</summary>
    public string Text { get; init; } = "";

    /// <summary>The building it's to be done at; Entity.Null for anywhere.</summary>
    public Entity Where { get; init; } = Entity.Null;

    /// <summary>The thing in question (the blade to mend); Entity.Null if none.</summary>
    public Entity Subject { get; init; } = Entity.Null;

    /// <summary>Silver promised on completion.</summary>
    public int Price { get; init; }

    public long PostedTick { get; init; }
    public long Deadline { get; init; }

    /// <summary>Whoever said they'd see to it; Entity.Null until someone does.</summary>
    public Entity Assignee { get; set; } = Entity.Null;
    public string AssigneeName { get; set; } = "";

    public ErrandStatus Status { get; set; }
    public long ClosedTick { get; set; }

    /// <summary>When the asker last said it aloud (they repeat themselves while nobody takes it).</summary>
    public long LastAsked { get; set; }

    /// <summary>Who knows of it, in the order they learned.</summary>
    public List<Entity> KnownBy { get; } = [];

    public bool IsOpen => Status == ErrandStatus.Open;
}

/// <summary>An errand as someone who knows of it sees it (<c>PerceptionView.Errands</c>).</summary>
public readonly record struct KnownErrand(
    int Id, ErrandKind Kind, string KindLabel, string PosterName, string Text, int Price,
    long PostedTick, long Deadline, string AssigneeName, ErrandStatus Status, bool Mine, bool MineToDo);
