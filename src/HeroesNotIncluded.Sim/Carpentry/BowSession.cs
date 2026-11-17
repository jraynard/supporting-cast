using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Carpentry;

/// <summary>
/// Tillering a bow at the carpenter's bench: the stave's on the tillering stick, drawn part way, and
/// each limb bends wherever it's thinnest. Each limb is in <see cref="SectionsPerLimb"/> sections,
/// every one too stiff at first. A and D (or the mouse) pick a section; holding F scrapes it, the
/// scraper biting harder the longer a stroke goes on. Scraped, a section bends more; it can't be put
/// back. The aim is an even arc, both limbs alike: a section scraped past it is a hinge, which costs
/// more than one left stiff. Space draws it to full: the tillering's done, as it is.
/// Then it's strung (<see cref="Stringing"/>): F held bends the bow against the floor, and Space slips
/// the string's loop into the nock, which only catches once it's bent far enough (<see cref="CatchLow"/>);
/// bent past <see cref="Strained"/> on the way, the limbs are strained, and that stays in the bow.
/// </summary>
public sealed class BowSession : WorkSession
{
    /// <summary>Sections in each limb (the upper's first, grip outward, then the lower's).</summary>
    public const int SectionsPerLimb = 4, Sections = SectionsPerLimb * 2;

    /// <summary>The stiffness every section should come down to (an even arc).</summary>
    public const float Even = 1f;

    /// <summary>How much a stroke scrapes per step at its start, and the most a long stroke gets to (times that).</summary>
    public const float ScrapePerStep = 0.0012f, MostBite = 3f;

    /// <summary>Steps of a stroke for the scraper to bite one more time as hard.</summary>
    public const int BiteSteps = 60;

    /// <summary>Within this of even (before skill widens it) a section's right; this far off, it's no good. A hinge counts double.</summary>
    public const float Right = 0.03f, Wrong = 0.3f;

    /// <summary>The thinnest a section can be scraped (a hinge that'll fret and break).</summary>
    public const float Thinnest = 0.5f;

    /// <summary>How fast holding F bends the bow for stringing, and how fast it springs back when let go, per step.</summary>
    public const float BendUp = 0.008f, BendBack = 0.02f;

    /// <summary>Bent this far (less with skill) the loop slips into the nock; past <see cref="Strained"/> the limbs are strained.</summary>
    public const float CatchFrom = 0.7f, Strained = 0.9f;

    /// <summary>The bench.</summary>
    public Entity Station { get; set; } = Entity.Null;

    /// <summary>How stiff each section is (1 is even; more is stiff, less a hinge).</summary>
    public float[] Stiffness { get; private set; } = new float[Sections];

    /// <summary>The section the scraper's on.</summary>
    public int Selected { get; private set; }

    private int _axis;
    private int _stroke;
    private int _drawn;
    private float _tillered;

    /// <summary>Tillered and being strung.</summary>
    public bool Stringing { get; private set; }

    /// <summary>How far it's bent for stringing, 0..1.</summary>
    public float Flex { get; private set; }

    /// <summary>The most it's been bent, and the loops that missed the nock.</summary>
    public float MostBend { get; private set; }
    public int Missed { get; private set; }

    /// <summary>Scraping now (held on a section that still has wood to give): for the shavings drawn and heard.</summary>
    public bool Scraping { get; private set; }

    public float CatchLow => CatchFrom - 0.06f * (Hands.Tolerance - 1f);

    public float RightWidth => Right * Hands.Tolerance;

    public override Skill Skill => Skill.Crafting;
    public override float Xp => 60f;
    public override string Title => Stringing ? "Stringing the bow" : "Tillering a bow";

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Station) ? sim.World.Get<Thing>(Station).Cell : null;

    public override bool StillValid(Simulation sim)
    {
        var w = sim.World;
        return Carpenters.IsBench(sim, Station) && AvatarSystem.InReach(sim, w.Get<Position>(Worker).Cell, Station)
            && w.Get<Workbench>(Station) is { Active: { } r, Stocked: true } bench && bench.By == Worker && Carpenters.MakesBow(r);
    }

    protected internal override void Begin()
    {
        for (var i = 0; i < Sections; i++)
            Stiffness[i] = 1.12f + Rand.NextFloat() * 0.45f;
    }

    protected override void Cloned() => Stiffness = (float[])Stiffness.Clone();

    /// <summary>
    /// Where a section sits left to right as the bow's drawn, tip to tip: the upper limb's tip at the
    /// left, its grip end in the middle, then the lower limb grip to tip. A, D and the mouse go by this.
    /// </summary>
    public static int ColumnOf(int section) => section < SectionsPerLimb ? SectionsPerLimb - 1 - section : section;

    /// <summary>The section at a column (<see cref="ColumnOf"/> backwards: it's its own inverse).</summary>
    public static int SectionAt(int column) => ColumnOf(column);

    /// <summary>A and D move the scraper a section left or right (once a press).</summary>
    protected override void Axis(int value)
    {
        if (value != 0 && _axis == 0)
            Selected = SectionAt(Math.Clamp(ColumnOf(Selected) + value, 0, Sections - 1));
        _axis = value;
        _stroke = 0;
    }

    /// <summary>The mouse across the stave picks the section under it.</summary>
    protected override void Pointed(float u, float v)
    {
        var at = SectionAt(Math.Clamp((int)(u * Sections), 0, Sections - 1));
        if (at != Selected)
            _stroke = 0;
        Selected = at;
    }

    /// <summary>Space: draw it to full, and that's the bow.</summary>
    protected override void Act() => _drawn = 1;

    protected override void Step()
    {
        Scraping = false;
        if (Stringing)
        {
            StepString();
            return;
        }
        if (_drawn > 0)
        {
            _drawn = 0;
            _tillered = Score();
            Stringing = true;
            return;
        }
        if (!Held)
        {
            _stroke = 0;
            return;
        }
        Scraping = Stiffness[Selected] > Thinnest;
        _stroke++;
        var bite = Math.Min(MostBite, 1f + _stroke / (float)BiteSteps);
        var wobble = 1f + (Rand.NextFloat() * 2f - 1f) * (0.25f + Hands.Tremor);
        Stiffness[Selected] = Math.Max(Thinnest, Stiffness[Selected] - ScrapePerStep * bite * wobble);
    }

    private void StepString()
    {
        Flex = Held ? Math.Min(1f, Flex + BendUp * (1f + (Rand.NextFloat() * 2f - 1f) * Hands.Tremor)) : Math.Max(0f, Flex - BendBack);
        MostBend = Math.Max(MostBend, Flex);
        if (_drawn == 0)
            return;
        _drawn = 0;
        if (Flex < CatchLow)
        {
            Missed++; // the loop slid past the nock: bend it again
            return;
        }
        Sample(_tillered * StringScore());
        Done = true;
    }

    /// <summary>What the stringing does to the bow, 0..1: a strain past the band stays in it, and each miss is a little more handling.</summary>
    public float StringScore()
    {
        var strain = MostBend > Strained ? (MostBend - Strained) / (1f - Strained) * 0.5f : 0f;
        return Math.Clamp(1f - strain - 0.04f * Math.Min(Missed, 5), 0f, 1f);
    }

    /// <summary>How right a section is at this stiffness, 0..1.</summary>
    public float SectionScore(float stiffness)
    {
        var off = stiffness >= Even ? stiffness - Even : (Even - stiffness) * 2f;
        return off <= RightWidth ? 1f : Math.Max(0f, 1f - (off - RightWidth) / (Wrong - RightWidth));
    }

    /// <summary>How the limbs are as they stand: each section's score, less for limbs that don't match (one pulls harder than the other).</summary>
    public float Score()
    {
        var sum = 0f;
        float upper = 0f, lower = 0f;
        for (var i = 0; i < Sections; i++)
        {
            sum += SectionScore(Stiffness[i]);
            if (i < SectionsPerLimb)
                upper += Stiffness[i];
            else
                lower += Stiffness[i];
        }
        var mismatch = MathF.Abs(upper - lower) / SectionsPerLimb;
        return sum / Sections * Math.Max(0f, 1f - mismatch * 2f);
    }

    /// <summary>How far a section bends drawn, for drawing: the stiffer, the less.</summary>
    public float Bend(int section) => 1f / Stiffness[section];

    public override void Finish(Simulation sim)
    {
        var at = sim.LocationOf(Worker);
        var quality = Quality;
        foreach (var item in Crafting.Finish(sim, Station, Worker, Crafting.QualityFrom(quality)))
            Inventory.Stow(sim, Worker, item);
        var hinge = Stiffness.Any(s => s < Even - Wrong * 0.5f);
        var note = hinge ? " A limb's scraped too thin in one place: it bends there like a hinge." : quality >= 0.85f ? " It draws sweet and even." : quality < 0.4f ? " It's stiff and uneven, but it shoots." : "";
        sim.Messages.Tell(Worker, MessageKind.Info, $"You string the bow.{note}", at);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Station);
        foreach (var s in Stiffness)
            w.Float(s);
        w.Int(Selected);
        w.Int(_axis);
        w.Int(_stroke);
        w.Int(_drawn);
        w.Float(_tillered);
        w.Bool(Stringing);
        w.Float(Flex);
        w.Float(MostBend);
        w.Int(Missed);
        w.Bool(Scraping);
    }

    protected override void ReadState(SaveReader r)
    {
        Station = r.Entity();
        Stiffness = new float[Sections];
        for (var i = 0; i < Sections; i++)
            Stiffness[i] = Math.Clamp(r.Float(), Thinnest, 2f);
        Selected = Math.Clamp(r.Int(), 0, Sections - 1);
        _axis = Math.Sign(r.Int());
        _stroke = Math.Max(0, r.Int());
        _drawn = Math.Sign(r.Int());
        _tillered = Math.Clamp(r.Float(), 0f, 1f);
        Stringing = r.Bool();
        Flex = Math.Clamp(r.Float(), 0f, 1f);
        MostBend = Math.Clamp(r.Float(), 0f, 1f);
        Missed = Math.Max(0, r.Int());
        Scraping = r.Bool();
    }
}
