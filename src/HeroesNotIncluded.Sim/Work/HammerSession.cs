using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// One heat on the anvil (<see cref="Smithing"/>): a marker sweeps back and forth and each blow
/// lands where it is when you press. On the mark is a good blow, and the piece's quality is the
/// average of them all, so a smith's hands decide the blade in place of a skill roll. The piece
/// cools all the while: hot, a blow moves the metal; fading, less; black, barely, and it may crack.
/// The heat ends when it's shaped or too cold to work, and it goes back to the fire.
/// </summary>
public sealed class HammerSession : WorkSession
{
    /// <summary>Seconds for the marker to go across and back.</summary>
    public const float SweepSeconds = 1.4f;

    /// <summary>Half the width of a perfect blow's mark, before skill widens it.</summary>
    public const float MarkHalfWidth = 0.07f;

    /// <summary>Steps the arm takes to come back up: a press before then does nothing.</summary>
    public const int RecoverySteps = 15;

    /// <summary>Chance a blow on black iron opens a crack.</summary>
    public const float CrackChance = 0.15f;

    /// <summary>Below this share of the working heat the piece has gone black: the heat's over.</summary>
    public const float TooCold = 0.6f;

    private int _recovery;

    /// <summary>The anvil.</summary>
    public Entity Station { get; set; }

    public RecipeDef Recipe { get; set; } = null!;

    /// <summary>The piece, as named when the heat began (for the title).</summary>
    public string ItemLabel { get; set; } = "";

    /// <summary>A mend: hammering out chips rather than making.</summary>
    public bool Repairing { get; set; }

    /// <summary>Where the next blow should land, 0..1.</summary>
    public float Mark { get; private set; }

    /// <summary>Blows struck this heat.</summary>
    public int Blows { get; private set; }

    /// <summary>The piece's heat, falling as you work.</summary>
    public float Temp { get; private set; }

    /// <summary>How far it's shaped, 0..1, across every heat.</summary>
    public float Shape { get; private set; }

    public int BlowsNeeded { get; private set; }

    /// <summary>The working heat (it moves) and the top of it.</summary>
    public float Lo { get; private set; }
    public float Hi { get; private set; }

    /// <summary>The air round the anvil, which the piece cools toward.</summary>
    public float Air { get; private set; }

    public PieceFlaw Flaw { get; private set; }

    /// <summary>Running totals for the piece's quality, carried over from earlier heats.</summary>
    public float BlowSum { get; private set; }
    public int BlowCount { get; private set; }

    /// <summary>The last blow landed on cold metal.</summary>
    public bool LastCold { get; private set; }

    /// <summary>The sweeping marker, 0..1.</summary>
    public float Marker => Triangle(StepsDone, SweepSeconds);

    public float HalfWidth => MarkHalfWidth * Hands.Tolerance;

    /// <summary>The arm is up and a press would land.</summary>
    public bool Ready => _recovery <= 0;

    /// <summary>How well a blow now would move the metal, 0..1, from its heat.</summary>
    public float Workable => Temp >= Lo ? 1f : Math.Clamp((Temp - Lo * TooCold) / (Lo * (1f - TooCold)), 0f, 1f);

    public bool Shaped => Shape >= 1f;

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Station) && sim.World.TryGet<Thing>(Station, out var t) ? t.Cell : null;

    public override Skill Skill => Recipe.Skill;
    public override float Xp => Shaped ? 60f : 15f;
    public override string Title => Repairing ? $"Hammering out the {ItemLabel}" : $"Hammering the {ItemLabel}";

    /// <summary>A heat on the anvil for the piece in the smith's tongs.</summary>
    internal static HammerSession For(Simulation sim, Entity smith, Entity anvil, WorkPiece p)
    {
        var (lo, hi, _) = Smithing.Window(p.Recipe);
        return new HammerSession
        {
            Station = anvil, Recipe = p.Recipe, ItemLabel = Smithing.PieceLabel(sim, p), Repairing = p.IsMend,
            Temp = p.Temp, Shape = p.Shape, BlowsNeeded = p.BlowsNeeded, Lo = lo, Hi = hi, Flaw = p.Flaw,
            BlowSum = p.BlowSum, BlowCount = p.BlowCount, Air = Smithing.AirAt(sim, smith),
        };
    }

    public override bool StillValid(Simulation sim)
    {
        var w = sim.World;
        return w.IsAlive(Station) && AvatarSystem.InReach(sim, w.Get<Position>(Worker).Cell, Station) && Smithing.PieceOf(sim, Worker) != null;
    }

    protected internal override void Begin() => Mark = Rand.Range(0.15f, 0.85f);

    protected override void Step()
    {
        if (_recovery > 0)
            _recovery--;
        Temp = Air + (Temp - Air) * Smithing.AirCooling;
        if (Temp < Lo * TooCold)
            Done = true;
    }

    protected override void Press()
    {
        if (!Ready)
            return;
        // Unsteady hands land a little off from where the marker was.
        var landed = Marker + (Rand.NextFloat() * 2f - 1f) * Hands.Tremor * 0.2f;
        var off = MathF.Abs(landed - Mark);
        var aim = off <= HalfWidth ? 1f : Math.Clamp(1f - (off - HalfWidth) / (HalfWidth * 3f), 0f, 1f);
        var heat = Workable;
        LastCold = heat < 0.5f;
        // A good blow on hot metal moves it most; on fading metal it moves less and does it no good.
        Shape = Math.Min(1f, Shape + (0.35f + 0.65f * aim) * (0.1f + 0.9f * heat) / BlowsNeeded);
        var grade = aim * (0.4f + 0.6f * heat);
        Sample(grade);
        BlowSum += grade;
        BlowCount++;
        if (LastCold && Flaw == PieceFlaw.None && Rand.NextFloat() < CrackChance)
            Flaw = PieceFlaw.Crack;
        _recovery = RecoverySteps;
        Blows++;
        Mark = Rand.Range(0.15f, 0.85f);
        if (Shaped)
            Done = true;
    }

    /// <summary>The heat's over: shaped, or gone too cold. Put the piece back in the tongs, and finish it if that's all it needs.</summary>
    public override void Finish(Simulation sim)
    {
        if (WriteBack(sim) is not { } p)
            return;
        var cell = sim.World.Get<Position>(Worker).Cell;
        if (!Shaped)
        {
            sim.Messages.Add(MessageKind.Info, $"The {ItemLabel} has gone {Smithing.GlowLabel(Smithing.GlowOf(Temp))}: back to the fire.", cell);
            return;
        }
        if (p.IsMend)
            Smithing.Mend(sim, Worker, p);
        else if (!Smithing.NeedsQuench(p.Recipe))
            Smithing.Complete(sim, Worker, p, null);
        else
            sim.Messages.Add(MessageKind.Info, $"The {ItemLabel} is shaped: {Smithing.NextStep(sim, Worker, p)}.", cell);
    }

    public override void Interrupted(Simulation sim) => WriteBack(sim);

    private WorkPiece? WriteBack(Simulation sim)
    {
        if (Smithing.PieceOf(sim, Worker) is not { } p)
            return null;
        p.Temp = Temp;
        p.Shape = Shape;
        p.BlowSum = BlowSum;
        p.BlowCount = BlowCount;
        p.Flaw = Flaw;
        return p;
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Station);
        w.Def(Recipe);
        w.String(ItemLabel);
        w.Bool(Repairing);
        w.Float(Mark);
        w.Int(Blows);
        w.Int(_recovery);
        w.Float(Temp);
        w.Float(Shape);
        w.Int(BlowsNeeded);
        w.Float(Lo);
        w.Float(Hi);
        w.Float(Air);
        w.Int((int)Flaw);
        w.Float(BlowSum);
        w.Int(BlowCount);
        w.Bool(LastCold);
    }

    protected override void ReadState(SaveReader r)
    {
        Station = r.Entity();
        Recipe = r.Def<RecipeDef>();
        if (r.Version < 3)
        {
            // The one-heat game of format 2: put down on load, as if the smith had stepped away.
            r.Entity();
            r.String();
            r.Float();
            r.Int();
            r.Int();
            Failed = "The hammering was put down.";
            return;
        }
        ItemLabel = r.String();
        Repairing = r.Bool();
        Mark = r.Float();
        Blows = r.Int();
        _recovery = r.Int();
        Temp = r.Float();
        Shape = r.Float();
        BlowsNeeded = r.Int();
        Lo = r.Float();
        Hi = r.Float();
        Air = r.Float();
        Flaw = (PieceFlaw)r.Int();
        BlowSum = r.Float();
        BlowCount = r.Int();
        LastCold = r.Bool();
    }
}
