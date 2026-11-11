namespace DragonWorld.Sim.Defs;

/// <summary>What a dragon is asking its keeper for (docs/plans/dragon-interactions.md, "Wants").</summary>
public enum DragonWant
{
    None,
    Food,
    Play,
    Scratch,
    Comfort,
    Groom,
    Treasure,
}

/// <summary>What an interaction does beyond its bond and affinity: the code that goes with a <see cref="DragonInteractionDef"/>.</summary>
public enum CareKind
{
    /// <summary>A pat, a scratch, a romp: nothing more than the bond.</summary>
    Plain,

    /// <summary>Hand-feeding what the keeper holds (its favourite counts double).</summary>
    Feed,

    /// <summary>Brushing out loose scales: while it's shedding, they come away as <c>DragonScale</c>s.</summary>
    Groom,

    /// <summary>Settling it when it's upset: only offered then.</summary>
    Comfort,

    /// <summary>A lesson towards a <see cref="TrickDef"/>.</summary>
    Train,

    /// <summary>Sleeping beside it: never chosen, it happens overnight.</summary>
    Rest,

    /// <summary>Treasure for its hoard (<c>Systems.Hoard.Give</c>).</summary>
    Gift,
}

/// <summary>
/// One way of caring for a dragon (data/defs/dragons.json). The player picks these at their dragon
/// and village keepers choose them by its want; both go through <c>Systems.Bonding.Interact</c>, so
/// the rules (novelty, answered wants, imprinting, trust's slowing gains) are the same for everyone.
/// </summary>
public sealed class DragonInteractionDef : Def
{
    public CareKind Kind { get; set; }

    /// <summary>Life stages it suits ("Hatchling"); empty for all.</summary>
    public List<string> Stages { get; set; } = [];

    /// <summary>Work ticks it takes (the player holds F); 0 is done at once.</summary>
    public int Ticks { get; set; }

    /// <summary>Added to the dragon's Bond need, and to its affinity, before the multipliers.</summary>
    public float Bond { get; set; }
    public float Affinity { get; set; }

    /// <summary>The memory it leaves on the dragon, about the one who did it.</summary>
    public string? Thought { get; set; }

    /// <summary>A tool kind that must be on the belt ("Brush").</summary>
    public string? Tool { get; set; }

    /// <summary>Done again within this many hours, it counts for half, then a quarter.</summary>
    public float NoveltyHours { get; set; } = 6f;

    /// <summary>The want it answers.</summary>
    public DragonWant Answers { get; set; }

    /// <summary>"Playing with Ember": what the job says while it's under way.</summary>
    public string Doing { get; set; } = "";

    /// <summary>"Play with {name}": the button and the F prompt.</summary>
    public string Prompt { get; set; } = "";

    public string PromptFor(string name) => Prompt.Replace("{name}", name);

    /// <summary>For <see cref="CareKind.Feed"/>: foods a dragon may have as its favourite (hashed from its name).</summary>
    public List<string> Favourites { get; set; } = [];

    public ThoughtDef? ThoughtDef { get; private set; }
    public List<ThingDef> FavouriteDefs { get; private set; } = [];

    public bool Suits(LifeStage stage) => Stages.Count == 0 || Stages.Contains(stage.Name);

    internal override void Resolve(DefDatabase db)
    {
        ThoughtDef = Thought is { Length: > 0 } t ? db.Thought(t) : null;
        FavouriteDefs = Favourites.Select(db.Thing).ToList();
        if (Doing.Length == 0)
            Doing = Label;
        if (Prompt.Length == 0)
            Prompt = char.ToUpperInvariant(Label[0]) + Label[1..] + " {name}";
    }
}

/// <summary>
/// A named stretch of affinity (wary, accepting, trusting, devoted, soulbound): what the player sees
/// instead of a number, and how quickly care still counts once there (<see cref="GainFactor"/>).
/// </summary>
public sealed class TrustLevelDef : Def
{
    public float MinAffinity { get; set; }

    /// <summary>Affinity gains are multiplied by this while at this level: devotion takes weeks, not a day.</summary>
    public float GainFactor { get; set; } = 1f;

    /// <summary>What reaching it brings, for the UI.</summary>
    public string Unlocks { get; set; } = "";
}

/// <summary>What knowing a trick changes (<see cref="TrickDef"/>).</summary>
public enum TrickEffect
{
    /// <summary>Comes when called from anywhere, and always does.</summary>
    Come,

    /// <summary>Can be told to stay put.</summary>
    Stay,

    /// <summary>Sneezes far less.</summary>
    HoldFire,

    /// <summary>Never shrugs its keeper off, and listens better mounted.</summary>
    Steady,

    /// <summary>Obeys better when given its <see cref="TrickDef.Work"/>.</summary>
    Work,
}

/// <summary>A trick taught over a few Train sessions; learned ones are kept on <c>Components.DragonCare</c>.</summary>
public sealed class TrickDef : Def
{
    public TrickEffect Effect { get; set; }

    /// <summary>Full sessions it takes (novelty makes crammed ones count less).</summary>
    public int Sessions { get; set; } = 3;

    /// <summary>For <see cref="TrickEffect.Work"/>: the work it applies to, and the obedience it adds.</summary>
    public List<WorkType> Work { get; set; } = [];

    public float Obedience { get; set; } = 0.2f;
}
