using System.Text.Json.Serialization;

namespace DragonWorld.Sim.Defs;

/// <summary>What makes a creature a wild animal: what it's worth to a hunter and how its herds behave.</summary>
public sealed class AnimalProps
{
    /// <summary>The label for several of them ("hares"); defaults to the label (deer).</summary>
    public string Plural { get; set; } = "";

    /// <summary>Raw meat from butchering the carcass.</summary>
    public int MeatYield { get; set; } = 10;

    /// <summary>Raw hides from butchering (tanned into leather at a tanning rack).</summary>
    public int HideYield { get; set; }

    /// <summary>Weight of the carcass (kg): how much it slows whoever carries it to a butcher spot.</summary>
    public float Mass { get; set; } = 40f;

    /// <summary>Work-ticks to butcher the carcass.</summary>
    public int ButcherWork { get; set; } = 300;

    public int HerdMin { get; set; } = 1;
    public int HerdMax { get; set; } = 1;

    /// <summary>Relative chance an arriving herd is of this kind.</summary>
    public float Commonness { get; set; } = 1;

    /// <summary>Anyone who isn't wild coming closer than this (cells) sends it running.</summary>
    public float FleeRadius { get; set; } = 8;

    /// <summary>Fights back when hurt instead of running.</summary>
    public bool Aggressive { get; set; }

    /// <summary>Days an unbutchered carcass lasts at 10–30°C (like <see cref="ThingDef.RotDays"/>).</summary>
    public float CorpseRotDays { get; set; } = 2;

    /// <summary>
    /// A predator: never in the herds that wander in, only sent by the storyteller
    /// (<c>Systems.WildEvents</c>) as a pack of <see cref="PackMin"/>..<see cref="PackMax"/> that comes
    /// for the village and fights turn by turn, each one costing <see cref="Points"/> of the raid points,
    /// from day <see cref="MinDay"/>.
    /// </summary>
    public bool Predator { get; set; }

    public int PackMin { get; set; } = 1;
    public int PackMax { get; set; } = 1;
    public float Points { get; set; } = 30;
    public int MinDay { get; set; } = 3;

    /// <summary>How long a herd stays before moving on, in game hours (min, max).</summary>
    public float[] StayHours { get; set; } = [18, 36];
}

/// <summary>How often herds arrive and how many animals the map holds (see <c>WildlifeSystem</c>).</summary>
public sealed class WildlifeDef : Def
{
    public float FirstArrivalHours { get; set; } = 10;
    public float MeanHoursBetween { get; set; } = 20;
    public int MaxAnimals { get; set; } = 14;

    /// <summary>
    /// Fewer animals than this left in the valley and herds come sooner (<see cref="ScarceArrivalFactor"/>
    /// of the wait), and they're of the <see cref="StartingKind"/>.
    /// </summary>
    public int MinAnimals { get; set; }

    public float ScarceArrivalFactor { get; set; } = 0.4f;

    /// <summary>Herds already grazing when a new map is generated.</summary>
    public int StartingHerds { get; set; } = 2;

    /// <summary>
    /// The kind the first starting herd always is (empty: any). Game a grown dragon can take, so a
    /// fresh map never starts with nothing but boars and hares.
    /// </summary>
    public string StartingKind { get; set; } = "";

    [JsonIgnore] public CreatureDef? StartingKindDef { get; private set; }

    internal override void Resolve(DefDatabase db)
    {
        if (string.IsNullOrEmpty(StartingKind))
            return;
        StartingKindDef = db.Creature(StartingKind);
        if (StartingKindDef.Animal == null)
            throw new Exception($"Wildlife {Name}: startingKind {StartingKind} isn't an animal");
    }
}
