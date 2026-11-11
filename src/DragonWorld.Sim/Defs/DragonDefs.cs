namespace DragonWorld.Sim.Defs;

/// <summary>
/// What makes a creature def a dragon breed (docs/plans/dragon-types.md): its name and place in the
/// creator. The breed's art, body and numbers are the creature def's own (life stages, sprites,
/// stats), so a new breed is a new def. Every <see cref="Race.Dragon"/> def is a breed; nothing else is.
/// </summary>
public sealed class BreedProps
{
    /// <summary>The creator's name for it ("Hearth dragon").</summary>
    public string Label { get; set; } = "";

    /// <summary>Its place in the creator's list; the lowest is the default breed (eggs and demos).</summary>
    public int Order { get; set; }

    /// <summary>A line for the creator's tooltip.</summary>
    public string Description { get; set; } = "";

    /// <summary>Chance a contented pair courts successfully, per day (<c>Systems.Breeding</c>).</summary>
    public float MatingChancePerDay { get; set; } = 0.25f;

    /// <summary>Days from mating to laying, and before she'll court again after a clutch.</summary>
    public float GestationDays { get; set; } = 3f;

    public float ClutchCooldownDays { get; set; } = 20f;

    /// <summary>Eggs in a clutch, at least and at most.</summary>
    public int ClutchMin { get; set; } = 1;

    public int ClutchMax { get; set; } = 2;
}

/// <summary>What a breath does to the cells it passes over (<c>Environment.Breath</c>).</summary>
public enum BreathEffect
{
    /// <summary>Fire: heats and sets alight.</summary>
    Ignite,

    /// <summary>Frost: cools toward a floor and puts fires out; never ignites.</summary>
    Freeze,

    /// <summary>Lightning: a little heat, and now and then a fire.</summary>
    Shock,

    /// <summary>Poison: withers plants, and leaves a lingering cloud where it lands.</summary>
    Cloud,

    /// <summary>Thorns and green: plants it touches regrow faster (<c>Components.Nurtured</c>).</summary>
    Bloom,
}

/// <summary>A status effect a breath leaves on whoever it catches (<c>Components.Afflictions</c>).</summary>
public enum StatusKind
{
    None,

    /// <summary>Slowed: fewer time units a turn and a slower walk, by <see cref="StatusProps.Strength"/>.</summary>
    Chill,

    /// <summary>Loses <see cref="StatusProps.TuShare"/> of its next turn's time units, and can't act for a moment out of combat.</summary>
    Stun,

    /// <summary>Takes <see cref="StatusProps.PerHour"/> damage an hour while it lasts.</summary>
    Venom,

    /// <summary>Held in place: can't walk or fly (it can still fight).</summary>
    Root,
}

public sealed class StatusProps
{
    public StatusKind Kind { get; set; }

    /// <summary>How long it lasts, in ticks (a combat round is 300).</summary>
    public int Ticks { get; set; }

    /// <summary>Chill: how much slower, 0..1.</summary>
    public float Strength { get; set; }

    /// <summary>Stun: the share of a turn's time units lost.</summary>
    public float TuShare { get; set; }

    /// <summary>Venom: damage per game hour.</summary>
    public float PerHour { get; set; }
}

/// <summary>Lightning leaping on from whoever it struck to others close by.</summary>
public sealed class ChainProps
{
    /// <summary>How far (in cells) it leaps from the last one struck.</summary>
    public float Radius { get; set; } = 3;

    /// <summary>How many more it can strike.</summary>
    public int Max { get; set; } = 2;

    /// <summary>Damage times this at each leap.</summary>
    public float Falloff { get; set; } = 0.6f;
}

/// <summary>Poison breath's lingering cloud (<c>Environment.VenomClouds</c>).</summary>
public sealed class CloudProps
{
    public float Radius { get; set; } = 1.5f;

    /// <summary>How long it hangs about, in game hours.</summary>
    public float Hours { get; set; } = 1;
}

/// <summary>
/// What a dragon of an element breathes and can do with it (docs/plans/dragon-types.md), keyed by
/// <see cref="Components.DragonElement"/> name. Fire's numbers are the breath the game has always
/// had: its factors are all 1, so the shape is the life stage's own.
/// </summary>
public sealed class ElementDef : Def
{
    /// <summary>The verb for messages and buttons: "breathes frost".</summary>
    public string Breath { get; set; } = "";

    /// <summary>The noun for buttons: "Breathe frost". Defaults to the label.</summary>
    public string Noun { get; set; } = "";

    /// <summary>The glowing core of its flames and breath, as hex RRGGBB.</summary>
    public string Color { get; set; } = "FF4A24";

    public DamageType DamageType { get; set; } = DamageType.Burn;

    /// <summary>Damage per ray that catches a creature, [min, max].</summary>
    public float[] Damage { get; set; } = [1.5f, 3f];

    /// <summary>How fiercely it starts fires or does its effect (job and combat breaths; sneezes keep their own).</summary>
    public float Intensity { get; set; } = 0.9f;

    /// <summary>How far it spills round where a ray strikes, in cells.</summary>
    public float Splash { get; set; } = 1.5f;

    /// <summary>Factors on the life stage's breath range, cone and rays (at least two rays).</summary>
    public float Range { get; set; } = 1;
    public float Cone { get; set; } = 1;
    public float Rays { get; set; } = 1;

    public BreathEffect Effect { get; set; }

    /// <summary>Shock: the chance a cell it passes over catches fire.</summary>
    public float IgniteChance { get; set; }

    /// <summary>Freeze: degrees of cooling per ray cell (scaled by intensity), never below <see cref="CoolFloor"/>.</summary>
    public float Cool { get; set; }
    public float CoolFloor { get; set; }

    /// <summary>Freeze: how much of a fire's strength each ray cell puts out.</summary>
    public float Douse { get; set; }

    /// <summary>Cloud: damage to a plant's health per ray cell (and per hour in the cloud).</summary>
    public float Wilt { get; set; }

    public StatusProps? Status { get; set; }
    public ChainProps? Chain { get; set; }
    public CloudProps? Cloud { get; set; }

    /// <summary>
    /// The colony work an element's breath does (<c>WorkGivers</c>): only dragons of an element
    /// that lists a breath work type can be given it.
    /// </summary>
    public List<WorkType> Work { get; set; } = [];

    /// <summary>Degrees lower its comfort and safe minimums go (an ice dragon shrugs off the cold).</summary>
    public float ColdTolerance { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public Components.DragonElement Element { get; private set; }

    /// <summary><see cref="Color"/> as 0xRRGGBB.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public uint Rgb { get; private set; }

    /// <summary>The breath work types: only dragons do them, and only the ones their element lists.</summary>
    public static bool IsBreathWork(WorkType t) => t is WorkType.Burn or WorkType.Stoke or WorkType.Chill or WorkType.Quarry or WorkType.Blight or WorkType.Nurture;

    internal override void Resolve(DefDatabase db)
    {
        if (!Enum.TryParse<Components.DragonElement>(Name, out var element))
            throw new Exception($"Element '{Name}' isn't a DragonElement");
        Element = element;
        if (Color.Length != 6 || !uint.TryParse(Color, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            throw new Exception($"Element '{Name}': color '{Color}' isn't RRGGBB");
        Rgb = rgb;
        if (Damage.Length != 2 || Damage[1] < Damage[0])
            throw new Exception($"Element '{Name}': damage must be [min, max]");
        if (Work.Exists(t => !IsBreathWork(t)))
            throw new Exception($"Element '{Name}': only breath work goes in its work list");
        if (Breath.Length == 0)
            Breath = $"breathes {Label}";
        if (Noun.Length == 0)
            Noun = Label;
    }
}
