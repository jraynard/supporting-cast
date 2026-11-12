using System.Reflection;
using System.Text.Json.Serialization;

namespace SupportingCast.Sim.Defs;

/// <summary>A state the mood system checks every update for situational thoughts (see <see cref="ThoughtDef.When"/>).</summary>
public enum ThoughtCondition
{
    None,
    /// <summary>The thought's <see cref="ThoughtDef.Need"/> is below <see cref="ThoughtDef.Below"/>; of several on one need, only the lowest band counts.</summary>
    NeedBelow,
    Cold,
    Hot,
    InPain,
    SeverePain,
    /// <summary>Awake in an indoor room with little light (no fire nearby at night).</summary>
    DarkIndoors,
    /// <summary>Held prisoner by the village.</summary>
    Imprisoned,
    /// <summary>Wearing jewelry (the Neck slot).</summary>
    WearingFinery,
    /// <summary>Ill from spoiled food (<c>Systems.FoodPoisoning</c>).</summary>
    FoodPoisoned,
}

/// <summary>
/// One reason a creature feels better or worse. A thought is situational while its
/// <see cref="When"/> condition holds, or a memory that lasts <see cref="DurationHours"/> after
/// something happened (added through <c>MoodSystem.Remember</c>). Offsets are mood points:
/// mood is 50 plus their sum, on a 0–100 scale (stored as 0–1).
/// </summary>
public sealed class ThoughtDef : Def
{
    public float Mood { get; set; }

    /// <summary>For a memory about someone (<c>Memory.Other</c>): how much it moves the opinion of them.</summary>
    public float Opinion { get; set; }

    /// <summary>How long a memory lasts. Ignored for situational thoughts.</summary>
    public float DurationHours { get; set; } = 24f;

    /// <summary>How many copies of the memory count at once (more refresh the oldest).</summary>
    public int StackLimit { get; set; } = 1;

    /// <summary>Each further copy counts this much of the one before.</summary>
    public float StackFalloff { get; set; } = 0.5f;

    /// <summary>Races that have this thought; empty means everyone with a mood.</summary>
    public List<Race> Races { get; set; } = [];

    public ThoughtCondition When { get; set; }

    /// <summary>For <see cref="ThoughtCondition.NeedBelow"/>: which need, and the level it holds under.</summary>
    public string? Need { get; set; }
    public float Below { get; set; }

    [JsonIgnore] public NeedDef? NeedDef { get; private set; }

    [JsonIgnore] public bool Situational => When != ThoughtCondition.None;

    public bool AppliesTo(Race race) => Races.Count == 0 || Races.Contains(race);

    internal override void Resolve(DefDatabase db)
    {
        if (When == ThoughtCondition.NeedBelow)
            NeedDef = db.Need(Need ?? throw new Exception($"Thought {Name} is a NeedBelow thought without a need"));
    }
}

/// <summary>
/// The memory thoughts the sim adds from code, looked up by field name once at load. A missing
/// def leaves its field null and the memory is just never added (minimal test def sets); the real
/// defs are checked to have all of them by <c>DefTests</c>.
/// </summary>
public sealed class KnownThoughts
{
    public ThoughtDef? AteRawMeat;
    public ThoughtDef? SleptOnGround;
    public ThoughtDef? SleptOutdoors;
    public ThoughtDef? SleptInBed;
    public ThoughtDef? VillagerDied;
    public ThoughtDef? WonFight;
    public ThoughtDef? LostFight;
    public ThoughtDef? FriendDied;
    public ThoughtDef? RivalDied;
    public ThoughtDef? RescuedMe;
    public ThoughtDef? TendedMe;
    public ThoughtDef? CapturedMe;
    public ThoughtDef? RecruitedMe;
    public ThoughtDef? FoughtBesideMe;
    public ThoughtDef? TurnedYouDown;
    public ThoughtDef? BossedAround;
    public ThoughtDef? WardenTalkedWithMe;
    public ThoughtDef? FedByWarden;
    public ThoughtDef? PrisonerEscaped;
    public ThoughtDef? AteCookedMeal;
    public ThoughtDef? UsesTheirWork;
    public ThoughtDef? AteSpoiledFood;
    private static readonly FieldInfo[] Fields = typeof(KnownThoughts).GetFields(BindingFlags.Public | BindingFlags.Instance);

    /// <summary>Every def name code refers to.</summary>
    public static IEnumerable<string> Names => Fields.Select(f => f.Name);

    internal static KnownThoughts Resolve(IReadOnlyDictionary<string, ThoughtDef> defs)
    {
        var known = new KnownThoughts();
        foreach (var f in Fields)
            f.SetValue(known, defs.TryGetValue(f.Name, out var def) ? def : null);
        return known;
    }
}

/// <summary>Who an interaction can be with.</summary>
public enum InteractionTarget
{
    /// <summary>Another person of the same faction.</summary>
    Person,
}

/// <summary>
/// Something one person does with another nearby (<c>SocialSystem</c>): a chat, a slight, a word
/// of comfort. Each leaves memories (with opinions of each other) on one or both sides. The pick
/// among those allowed is weighted: <see cref="Weight"/> × opinion and Social skill factors.
/// </summary>
public sealed class InteractionDef : Def
{
    public InteractionTarget With { get; set; }

    public float Weight { get; set; } = 1f;

    /// <summary>Weight × (1 + this × opinion / 100), clamped to 0–3: positive for friendly talk, negative for slights.</summary>
    public float OpinionFactor { get; set; }

    /// <summary>Weight × (1 + this × (Social − average) / average): skilled talkers pick deeper ones.</summary>
    public float SocialFactor { get; set; }

    /// <summary>Only with someone the initiator thinks at least this well of (opinion points).</summary>
    public float MinOpinion { get; set; } = -100f;

    /// <summary>Only when the other's mood is below this (comforting someone who's down).</summary>
    public float TargetMoodBelow { get; set; } = 1f;

    /// <summary>Memories left on the initiator and the recipient, about each other (either may be empty).</summary>
    public string? InitiatorThought { get; set; }
    public string? RecipientThought { get; set; }

    /// <summary>Social experience for the initiator.</summary>
    public float Xp { get; set; } = 40f;

    /// <summary>
    /// Shows a player character a recipe the initiator knows and they don't (<c>RecipeBooks.Teachable</c>);
    /// only possible when there is one, so it never shifts the social rolls otherwise.
    /// </summary>
    public bool Teaches { get; set; }

    /// <summary>Past-tense verb for the log: "{a} chatted with {b}".</summary>
    public string Verb { get; set; } = "talked with";

    [JsonIgnore] public ThoughtDef? InitiatorThoughtDef { get; private set; }
    [JsonIgnore] public ThoughtDef? RecipientThoughtDef { get; private set; }

    internal override void Resolve(DefDatabase db)
    {
        if (InitiatorThought != null)
            InitiatorThoughtDef = db.Thought(InitiatorThought);
        if (RecipientThought != null)
            RecipientThoughtDef = db.Thought(RecipientThought);
    }
}

/// <summary>
/// A lasting part of someone's nature: a constant mood offset, and factors on how much given
/// thoughts (mood and opinion alike) and interactions weigh for them. Humans get 0–2 at spawn
/// (<c>TraitRules.Roll</c>); the player picks the avatar's.
/// </summary>
public sealed class TraitDef : Def
{
    public float Mood { get; set; }

    /// <summary>Thought name → factor on its mood and opinion (0 ignores it).</summary>
    public Dictionary<string, float> ThoughtFactors { get; set; } = [];

    /// <summary>Interaction name → factor on how often this person starts it.</summary>
    public Dictionary<string, float> InteractionFactors { get; set; } = [];

    /// <summary>Traits that can't be held together with this one (either side may list it).</summary>
    public List<string> ConflictsWith { get; set; } = [];

    /// <summary>Relative chance of being rolled.</summary>
    public float Commonness { get; set; } = 1f;

    [JsonIgnore] public Dictionary<ThoughtDef, float> ThoughtFactorDefs { get; } = [];
    [JsonIgnore] public Dictionary<InteractionDef, float> InteractionFactorDefs { get; } = [];
    [JsonIgnore] public List<TraitDef> Conflicts { get; } = [];

    internal override void Resolve(DefDatabase db)
    {
        ThoughtFactorDefs.Clear();
        InteractionFactorDefs.Clear();
        Conflicts.Clear();
        foreach (var (name, f) in ThoughtFactors)
            ThoughtFactorDefs[db.Thought(name)] = f;
        foreach (var (name, f) in InteractionFactors)
            InteractionFactorDefs[db.Interaction(name)] = f;
        foreach (var name in ConflictsWith)
            Conflicts.Add(db.Trait(name));
    }

    public bool ConflictsWithTrait(TraitDef other) => other == this || Conflicts.Contains(other) || other.Conflicts.Contains(this);
}
