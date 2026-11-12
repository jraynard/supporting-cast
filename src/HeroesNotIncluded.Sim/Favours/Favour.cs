namespace HeroesNotIncluded.Sim.Favours;

public enum FavourStatus : byte
{
    Open,
    Done,

    /// <summary>Nobody finished it in time.</summary>
    Expired,

    /// <summary>Called off by the asker, or they're gone.</summary>
    Cancelled,
}

/// <summary>
/// Something one player asks of the others, in their own words, for silver they name: the town's
/// odd jobs, outside any shop (<see cref="FavourBoard"/>). Known only to those who heard it asked
/// (<see cref="KnownBy"/>), through <c>Perception</c>.
/// </summary>
public sealed class Favour
{
    public int Id { get; init; }
    public Entity Poster { get; init; } = Entity.Null;

    /// <summary>The asker's name as it was when they asked, so the list reads right after they're gone.</summary>
    public string PosterName { get; init; } = "";

    /// <summary>What was asked, in the asker's words.</summary>
    public string Text { get; init; } = "";

    /// <summary>Silver promised on completion.</summary>
    public int Price { get; init; }

    public long PostedTick { get; init; }
    public long Deadline { get; init; }

    /// <summary>Whoever said they'd see to it; Entity.Null until someone does.</summary>
    public Entity Assignee { get; set; } = Entity.Null;
    public string AssigneeName { get; set; } = "";

    public FavourStatus Status { get; set; }
    public long ClosedTick { get; set; }

    /// <summary>Who knows of it, in the order they learned.</summary>
    public List<Entity> KnownBy { get; } = [];

    public bool IsOpen => Status == FavourStatus.Open;
}

/// <summary>A favour as someone who knows of it sees it (<c>PerceptionView.Favours</c>).</summary>
public readonly record struct KnownFavour(
    int Id, string PosterName, string Text, int Price, long PostedTick, long Deadline, string AssigneeName,
    FavourStatus Status, bool Mine, bool MineToDo, Entity Poster);
