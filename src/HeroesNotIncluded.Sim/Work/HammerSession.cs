using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// One heat on the anvil (<see cref="Smithing"/>): carry the hammer over the mark (A/D, or where
/// the mouse points) and strike (F, or a click: on the move, it falls once it's there). It lands
/// near where it's held, nearer the steadier the hand, and wide if it's struck before the hand's
/// settled (<see cref="Aim"/>): with a new mark after every blow and the piece cooling, there's no
/// time to dawdle and no use hurrying. On the mark is a good blow, and the piece's quality is the
/// average of them all, so a smith's hands decide the blade in place of a skill roll. The piece
/// cools all the while: hot, a blow moves the metal; fading, less; black, barely, and it may crack.
/// The heat ends when it's shaped or too cold to work, and it goes back to the fire.
/// </summary>
public sealed class HammerSession : WorkSession
{
    /// <summary>How wide a settled blow strays from where it's held, before the hand's tremor.</summary>
    public const float Stray = 0.03f;

    /// <summary>How much wider the hand's tremor sends a blow (aimed, the tremor is most of what's to master).</summary>
    public const float TremorStray = 0.5f;

    /// <summary>Half the width of a perfect blow's mark, before skill widens it.</summary>
    public const float MarkHalfWidth = 0.07f;

    /// <summary>Steps the arm takes to come back up: a press before then does nothing.</summary>
    public const int RecoverySteps = 15;

    /// <summary>Chance a blow on black iron opens a crack.</summary>
    public const float CrackChance = 0.15f;

    /// <summary>Below this share of the working heat the piece has gone black: the heat's over.</summary>
    public const float TooCold = 0.6f;

    private int _recovery, _dir;
    private Aim _aim = new(0.5f);

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

    /// <summary>Where the hammer's held over the bar, 0..1.</summary>
    public float Marker => _aim.At;

    /// <summary>The hand's at rest: a blow now lands where it's held, as near as the hand's steady.</summary>
    public bool Settled => _aim.Settled(StepsDone);

    /// <summary>Where the last blow landed, 0..1; -1 before the first.</summary>
    public float LastAt { get; private set; } = -1f;

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

    protected override void Axis(int value) => _dir = value;

    /// <summary>A click strikes where it points: at once if the hammer's there, else as soon as it's carried there.</summary>
    protected override void Clicked(float? u, float? v)
    {
        if (u is { } x && Toward(_aim.At, x, Aim.PerStep) != 0)
            _aim.Queued = true;
        else
            Press();
    }

    protected override void Step()
    {
        if (_recovery > 0)
            _recovery--;
        if (_aim.Step(_dir, PointU, StepsDone))
        {
            if (Ready)
                Press();
            else
                _aim.Queued = true; // the arm's still coming up: the blow waits
        }
        Temp = Air + (Temp - Air) * Smithing.AirCooling;
        if (Temp < Lo * TooCold)
            Done = true;
    }

    protected override void Press()
    {
        if (!Ready)
            return;
        // It lands near where it's held: a little off for unsteady hands, wide if struck on the move.
        var landed = LastAt = Marker + (Rand.NextFloat() * 2f - 1f) * (Stray + Hands.Tremor * TremorStray) * _aim.Unsteadiness(StepsDone);
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
        if (p.IsBloom)
            Smithing.CompleteBloom(sim, Worker, p);
        else if (p.IsMend)
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
        _aim.Write(w);
        w.Int(_dir);
        w.Float(LastAt);
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
        if (r.Version >= 43) // the hammer carried to the mark, not swept past it (format 43)
        {
            _aim = Aim.Read(r);
            _dir = Math.Sign(r.Int());
            LastAt = r.Float();
        }
    }
}
