using System.Text.Json.Serialization;

namespace SupportingCast.Sim.Defs;

/// <summary>
/// One of the runes an empath weaves mendings from (<c>Healing.CastSession</c>). Runes are keyed
/// 1 to 9 in def order. A rune means nothing alone: which mending a weave makes depends on the
/// mix of runes in it, so a player can find mendings that aren't in their book yet.
/// </summary>
public sealed class RuneDef : Def
{
    /// <summary>The glyph's sprite (data/textures), or empty for the label's first letter.</summary>
    public string Icon { get; set; } = "";
}

/// <summary>
/// A mending an empath weaves (<c>Healing.CastSession</c>): the runes that make it, which kinds
/// of wound it takes hold of, what it costs and how much it heals. It also says how each of those
/// wounds feels to whoever carries it, since an empath knows what they've taken by its feel, not
/// by a name (<see cref="Feels"/>). Rune order doesn't matter, only the mix: that's what lets a
/// player find a mending by trying runes. Extra copies of the <see cref="Lead"/> rune make a
/// stronger, dearer cast.
/// </summary>
public sealed class MendingDef : Def
{
    /// <summary>The most runes a weave can hold.</summary>
    public const int MaxRunes = 6;

    public List<DamageType> Heals { get; set; } = [];

    /// <summary>Also clears festering in the wound (<c>Components.Infection</c>), at a good enough cast.</summary>
    public bool Cleanses { get; set; }

    /// <summary>Closes the wound as a dressing would: it stops bleeding (marks it tended).</summary>
    public bool Staunches { get; set; }

    /// <summary>Burn damage the cast leaves on the caster's part where it lands (searing hurts).</summary>
    public float Sears { get; set; }

    /// <summary>
    /// Steadies the caster: widens the calm band of their next draws by this share, for
    /// <see cref="SteadyHours"/>. A mending that only steadies needs no wound to land on.
    /// </summary>
    public float Steadies { get; set; }

    public float SteadyHours { get; set; } = 1f;

    public float ManaCost { get; set; } = 20;

    /// <summary>Damage healed by a perfect cast (a poor one heals a quarter of it).</summary>
    public float Heal { get; set; } = 15;

    /// <summary>The runes that make it, by name, in any order (a rune may repeat).</summary>
    public List<string> Runes { get; set; } = [];

    /// <summary>The rune whose extra copies strengthen it; the first rune if not given.</summary>
    public string Lead { get; set; } = "";

    /// <summary>Extra mana and healing for each extra copy of the lead rune; half the base if not given.</summary>
    public float? ManaPerLead { get; set; }
    public float? HealPerLead { get; set; }

    /// <summary>Written in a new empath's book. The rest are found by weaving them.</summary>
    public bool KnownAtStart { get; set; } = true;

    /// <summary>How a wound of each kind it heals feels to carry ("a clean, bright line of pain").</summary>
    public Dictionary<DamageType, string> Feels { get; set; } = [];

    /// <summary>The base runes resolved, sorted by index.</summary>
    [JsonIgnore]
    public List<RuneDef> RuneDefs { get; private set; } = [];

    [JsonIgnore]
    public RuneDef LeadRune { get; private set; } = null!;

    /// <summary>Lands with no wound to take hold of (it only steadies).</summary>
    public bool Targetless => Heals.Count == 0 && !Cleanses && !Staunches && Steadies > 0;

    /// <summary>Mana for a cast with <paramref name="extra"/> copies of the lead rune past the base.</summary>
    public float CostAt(int extra) => ManaCost + (ManaPerLead ?? ManaCost * 0.5f) * extra;

    /// <summary>Damage healed by a perfect cast with <paramref name="extra"/> lead runes past the base.</summary>
    public float HealAt(int extra) => Heal + (HealPerLead ?? Heal * 0.5f) * extra;

    internal override void Resolve(DefDatabase db)
    {
        if (Heals.Count == 0 && !Cleanses && !Staunches && Steadies <= 0)
            throw new Exception($"Mending '{Name}' does nothing");
        if (Runes.Count is < 1 || Runes.Count > MaxRunes)
            throw new Exception($"Mending '{Name}': 1 to {MaxRunes} runes");
        RuneDefs = Runes.Select(db.Rune).OrderBy(r => r.Index).ToList();
        LeadRune = db.Rune(string.IsNullOrEmpty(Lead) ? Runes[0] : Lead);
        if (!RuneDefs.Contains(LeadRune))
            throw new Exception($"Mending '{Name}': its lead rune {LeadRune.Name} isn't one of its runes");
        if (ManaCost <= 0 || Heal <= 0 || ManaPerLead < 0 || HealPerLead < 0 || Sears < 0 || Steadies < 0 || SteadyHours <= 0)
            throw new Exception($"Mending '{Name}': costs and amounts must be positive");
        foreach (var t in Heals)
            if (!Feels.ContainsKey(t))
                throw new Exception($"Mending '{Name}' needs a feel for {t}");
    }
}

/// <summary>How a wound's pain moves an empath's composure while they draw it (<c>Healing.DrawSession</c>).</summary>
public enum DrawPattern
{
    /// <summary>Sharp single jolts, either way.</summary>
    Spike,

    /// <summary>A jolt, and another the same way close behind: a ragged tear.</summary>
    Double,

    /// <summary>A slow swell back and forth, to be ridden rather than caught.</summary>
    Wave,

    /// <summary>A steady pull one way, with small jolts.</summary>
    Drift,

    /// <summary>Jolts, through hands gone numb: steering is slow to answer and slow to stop.</summary>
    Numb,

    /// <summary>Jolts that grow as more of it comes across.</summary>
    Grow,
}

/// <summary>
/// How drawing a wound of one kind feels in the hands (named for its <see cref="DamageType"/>),
/// so the draw teaches what the feel names: a cut jolts, a bruise swells, a burn pulls, frost numbs.
/// </summary>
public sealed class DrawFeelDef : Def
{
    public DrawPattern Pattern { get; set; }

    /// <summary>How often it jolts, and how hard, against a plain wound's.</summary>
    public float Rate { get; set; } = 1f;
    public float Strength { get; set; } = 1f;

    [JsonIgnore]
    public DamageType Type { get; private set; }

    internal override void Resolve(DefDatabase db)
    {
        if (!Enum.TryParse<DamageType>(Name, out var type))
            throw new Exception($"Draw feel '{Name}' isn't a kind of damage");
        Type = type;
        if (Rate <= 0 || Strength <= 0)
            throw new Exception($"Draw feel '{Name}': rate and strength must be positive");
    }
}
