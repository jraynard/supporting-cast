using System.Text.Json.Serialization;

namespace HeroesNotIncluded.Sim.Defs;

public enum DamageType
{
    Cut,
    Pierce,
    Blunt,
    Burn,
    Bite,

    // Dragon breaths of the other elements (ElementDef.DamageType). Saved by value: append only.

    /// <summary>Frostbite from ice breath.</summary>
    Frost,

    /// <summary>A lightning strike.</summary>
    Shock,

    /// <summary>Poison eating at the flesh (breath, and the cloud's venom over time).</summary>
    Venom,
}

/// <summary>What a creature can still do, each 0..1, derived from its injuries (see <c>Components.Health</c>).</summary>
public enum Capacity
{
    Consciousness,
    Moving,
    Manipulation,
    Sight,
    Flying,
}

/// <summary>Roughly where on the body a part sits, so attacks from above or below favor some parts.</summary>
public enum PartHeight
{
    Low,
    Mid,
    High,
}

public sealed class BodyPartDef
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";

    /// <summary>Relative chance of being hit. Weights within a body needn't sum to 1.</summary>
    public float Coverage { get; set; } = 0.1f;

    /// <summary>Hit points for a creature with 100 max HP; scaled by <see cref="CreatureDef.MaxHp"/>.</summary>
    public float Hp { get; set; } = 20f;

    /// <summary>Destroying a vital part kills.</summary>
    public bool Vital { get; set; }

    /// <summary>How badly wounds here bleed.</summary>
    public float BleedFactor { get; set; } = 1f;

    public PartHeight Height { get; set; } = PartHeight.Mid;

    /// <summary>Capacities this part provides; each capacity averages the health of the parts providing it.</summary>
    public List<Capacity> Capacities { get; set; } = [];

    public override string ToString() => Name;
}

/// <summary>
/// The parts a creature can be hurt in. Injuries live on parts, so a cut arm spoils aim and a
/// torn wing grounds a dragon instead of everything draining one health bar.
/// </summary>
public sealed class BodyDef : Def
{
    public List<BodyPartDef> Parts { get; set; } = [];

    internal override void Resolve(DefDatabase db)
    {
        if (Parts.Count == 0)
            throw new Exception($"Body '{Name}' has no parts");
        foreach (var p in Parts)
        {
            if (string.IsNullOrEmpty(p.Label))
                p.Label = p.Name;
            if (p.Coverage <= 0 || p.Hp <= 0)
                throw new Exception($"Body '{Name}' part '{p.Name}' needs positive coverage and hp");
        }
    }

    [JsonIgnore] public float TotalHp => Parts.Sum(p => p.Hp);

    public int PartIndex(string name) => Parts.FindIndex(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Per damage type: how much wounds bleed and hurt.</summary>
public static class DamageTypes
{
    public static float Bleed(DamageType t) => t switch
    {
        DamageType.Cut => 1.2f,
        DamageType.Pierce => 1f,
        DamageType.Bite => 1f,
        DamageType.Blunt => 0.2f,
        _ => 0f, // burns cauterize
    };

    /// <summary>Chance a sizeable untended wound festers (bites are dirtiest, bruises rarely).</summary>
    public static float InfectChance(DamageType t) => t switch
    {
        DamageType.Bite => 0.3f,
        DamageType.Cut or DamageType.Pierce => 0.15f,
        DamageType.Burn => 0.1f,
        DamageType.Frost => 0.08f,
        DamageType.Venom => 0.12f,
        _ => 0.03f,
    };

    public static string Wound(DamageType t) => t switch
    {
        DamageType.Cut => "cut",
        DamageType.Pierce => "puncture",
        DamageType.Blunt => "bruise",
        DamageType.Burn => "burn",
        DamageType.Frost => "frostbite",
        DamageType.Shock => "lightning burn",
        DamageType.Venom => "poisoned wound",
        _ => "bite",
    };
}

/// <summary>
/// One way of attacking: a weapon's (<see cref="ThingDef.Weapon"/>) or a natural one (fists, a
/// dragon's bite). Melee reaches touching cells; ranged attacks need a clear line of fire and
/// lose accuracy beyond <see cref="OptimalRange"/>.
/// </summary>
public sealed class AttackDef
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";

    /// <summary>"hits", "shoots", "bites"... for the combat log.</summary>
    public string Verb { get; set; } = "hits";

    public bool Ranged { get; set; }

    /// <summary>Reach in cells for ranged attacks (melee always means touching).</summary>
    public float Range { get; set; } = 1f;

    public float OptimalRange { get; set; } = 1f;

    /// <summary>Base chance to hit, before distance, cover, height and the attacker's wounds.</summary>
    public float Accuracy { get; set; } = 0.75f;

    /// <summary>Chance lost per cell beyond <see cref="OptimalRange"/>.</summary>
    public float AccuracyFalloff { get; set; } = 0.03f;

    public float DamageMin { get; set; } = 5f;
    public float DamageMax { get; set; } = 10f;
    public DamageType DamageType { get; set; } = DamageType.Blunt;

    /// <summary>Cancels that much of the target's armor.</summary>
    public float Penetration { get; set; }

    /// <summary>Chance a hit glances off and does nothing.</summary>
    public float Glance { get; set; } = 0.1f;

    /// <summary>Seconds between attacks outside turn-based combat (hunting); 0 means 2.5 for ranged, 1.5 for melee.</summary>
    public float CooldownSeconds { get; set; }

    [JsonIgnore]
    public int CooldownTicks => (int)((CooldownSeconds > 0 ? CooldownSeconds : Ranged ? 2.5f : 1.5f) * SimClock.TicksPerSecond);

    public override string ToString() => Name;
}
