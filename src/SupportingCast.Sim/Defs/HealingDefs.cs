using System.Text.Json.Serialization;

namespace SupportingCast.Sim.Defs;

/// <summary>
/// A mending an empath casts on a wound they carry (<c>Healing.CastSession</c>): which kinds of
/// wound it takes hold of, what it costs, how much it heals, and the runes it's cast with. It also
/// says how each of those wounds feels to whoever carries it, since an empath knows what they've
/// taken by its feel, not by a name (<see cref="Feels"/>).
/// </summary>
public sealed class MendingDef : Def
{
    public List<DamageType> Heals { get; set; } = [];

    /// <summary>Also clears festering in the wound (<c>Components.Infection</c>), at a good enough cast.</summary>
    public bool Cleanses { get; set; }

    public float ManaCost { get; set; } = 20;

    /// <summary>Damage healed by a perfect cast (a poor one heals a quarter of it).</summary>
    public float Heal { get; set; } = 15;

    /// <summary>The cast, a rune at a time: L (A), R (D) or F, played on cue.</summary>
    public string Runes { get; set; } = "LRF";

    /// <summary>How a wound of each kind it heals feels to carry ("a clean, bright line of pain").</summary>
    public Dictionary<DamageType, string> Feels { get; set; } = [];

    internal override void Resolve(DefDatabase db)
    {
        if (Heals.Count == 0 && !Cleanses)
            throw new Exception($"Mending '{Name}' heals nothing");
        if (Runes.Length is < 1 or > 12 || Runes.Any(c => c is not ('L' or 'R' or 'F')))
            throw new Exception($"Mending '{Name}': runes are 1 to 12 of L, R and F");
        if (ManaCost <= 0 || Heal <= 0)
            throw new Exception($"Mending '{Name}': mana cost and heal must be positive");
        foreach (var t in Heals)
            if (!Feels.ContainsKey(t))
                throw new Exception($"Mending '{Name}' needs a feel for {t}");
    }
}
