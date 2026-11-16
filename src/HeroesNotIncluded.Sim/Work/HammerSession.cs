using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// One heat on the anvil (<see cref="Smithing"/>): wind the hammer back (W, or the mouse raised
/// over the anvil) and swing it down onto the piece (S, or the mouse brought down). The blow is
/// as good as the wind: back to the mark is a full one; short of it moves the metal less; past it,
/// the blow comes down too hard and knocks the work out of true, undoing what was done. It must
/// come down quickly, too: a hammer lowered slowly onto the bar hardly moves it. An unsteady arm
/// wavers as it winds and carries on (or sags) as it stops, so the top of the wind isn't quite where it was meant. A new mark after
/// every blow, and the piece's quality is the average of them all, so a smith's hands decide the
/// blade in place of a skill roll. The piece cools all the while: hot, a blow moves the metal;
/// fading, less; black, barely, and it may crack. The heat ends when it's shaped or too cold to
/// work, and it goes back to the fire.
/// </summary>
public sealed class HammerSession : WorkSession
{
    /// <summary>How far the hammer winds back a step with W held (all the way in about two-thirds of a second).</summary>
    public const float WindPerStep = 0.025f;

    /// <summary>How far it comes down a step at full swing: from the top in under a quarter of a second.</summary>
    public const float SwingPerStep = 0.075f;

    /// <summary>How much faster than W the mouse may wind it.</summary>
    public const float MouseWind = 1.6f;

    /// <summary>A wind under this is a tap, not a blow: lowering it from there strikes nothing.</summary>
    public const float MinWind = 0.12f;

    /// <summary>Steps from the top of the wind to the piece that still make a full blow; slower loses force over <see cref="SlowSteps"/> more.</summary>
    public const int QuickSteps = 16, SlowSteps = 30;

    /// <summary>The least a slow blow keeps of its force.</summary>
    public const float SlowestBlow = 0.45f;

    /// <summary>How much a wind wavers a step for an unsteady hand, per point of tremor.</summary>
    public const float WindWaver = 0.012f;

    /// <summary>How far an unsteady arm carries on past (or falls short of) where the wind's stopped, per point of tremor: once a wind.</summary>
    public const float TopWaver = 0.4f;

    /// <summary>Half the width of a perfect wind's mark, before skill widens it.</summary>
    public const float MarkHalfWidth = 0.07f;

    /// <summary>Steps the hammer rests on the bar after a blow before it can be wound again.</summary>
    public const int RecoverySteps = 10;

    /// <summary>Chance a blow on black iron opens a crack.</summary>
    public const float CrackChance = 0.15f;

    /// <summary>Below this share of the working heat the piece has gone black: the heat's over.</summary>
    public const float TooCold = 0.6f;

    private int _recovery, _wind, _down;
    private bool _rising, _jolted;

    /// <summary>The anvil.</summary>
    public Entity Station { get; set; }

    public RecipeDef Recipe { get; set; } = null!;

    /// <summary>The piece, as named when the heat began (for the title).</summary>
    public string ItemLabel { get; set; } = "";

    /// <summary>A mend: hammering out chips rather than making.</summary>
    public bool Repairing { get; set; }

    /// <summary>How far back to wind for the next blow, 0..1 (resting on the bar to straight up).</summary>
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

    /// <summary>How far the hammer's wound back now, 0 (on the bar) to 1.</summary>
    public float Raise { get; private set; }

    /// <summary>The top of this wind so far: what the blow will be judged by.</summary>
    public float Peak { get; private set; }

    /// <summary>How the last wind sat against its mark: -1 short, 0 on it, 1 past it; and how high it went (-1 before the first).</summary>
    public int LastWindWas { get; private set; }
    public float LastWind { get; private set; } = -1f;

    /// <summary>The last blow came down slowly.</summary>
    public bool LastSlow { get; private set; }

    /// <summary>Coming down: the hammer's fallen from the top of its wind.</summary>
    public bool Swinging => Raise < Peak - 0.001f;

    public float HalfWidth => MarkHalfWidth * Hands.Tolerance;

    /// <summary>The hammer's off the bar after the last blow and can be wound again.</summary>
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

    private void NewMark() => Mark = Rand.Range(0.35f, 0.9f);

    protected internal override void Begin() => NewMark();

    /// <summary>W winds it back (-1), S swings it down (+1).</summary>
    protected override void Axis2(int value) => _wind = value;

    protected override void Step()
    {
        if (_recovery > 0)
            _recovery--;
        else
        {
            var before = Raise;
            if (_wind < 0)
                Raise += WindPerStep;
            else if (_wind > 0)
                Raise -= SwingPerStep;
            else if (PointV is { } v) // the mouse: up the anvil is wound back, at the arm's own pace
                Raise += Math.Clamp(1f - v - Raise, -SwingPerStep, WindPerStep * MouseWind);
            if (Raise > before) // winding back, an unsteady hand wavers
                Raise += (Rand.NextFloat() * 2f - 1f) * Hands.Tremor * WindWaver;
            else if (_rising && !_jolted && Hands.Tremor > 0f)
            {
                // Stopped at the top: an unsteady arm carries on a little, or sags, as it will.
                _jolted = true;
                Raise += (Rand.NextFloat() * 2f - 1f) * Hands.Tremor * TopWaver;
            }
            _rising = Raise > before;
            Raise = Math.Clamp(Raise, 0f, 1f);
            if (Raise >= Peak)
                (Peak, _down) = (Raise, 0);
            else
                _down++;
            if (Raise <= 0f)
            {
                if (Peak >= MinWind)
                    Blow();
                Peak = 0f;
            }
        }
        Temp = Air + (Temp - Air) * Smithing.AirCooling;
        if (Temp < Lo * TooCold)
            Done = true;
    }

    /// <summary>The hammer's come down on the piece: as good as the wind was, and as quick.</summary>
    private void Blow()
    {
        var off = Peak - Mark;
        var heat = Workable;
        LastCold = heat < 0.5f;
        LastWind = Peak;
        var quick = Math.Clamp(1f - (_down - QuickSteps) / (float)SlowSteps, SlowestBlow, 1f);
        LastSlow = quick < 1f;
        float grade;
        if (off > HalfWidth)
        {
            // Wound past it: it comes down too hard and knocks the work out of true.
            LastWindWas = 1;
            var over = Math.Clamp((off - HalfWidth) / (HalfWidth * 3f), 0f, 1f);
            Shape = Math.Max(0f, Shape - (0.6f + 0.9f * over) / BlowsNeeded);
            grade = 0.1f * (1f - over);
        }
        else
        {
            // On the mark, a full blow; short of it, less, the shorter the less.
            LastWindWas = off < -HalfWidth ? -1 : 0;
            var aim = off >= -HalfWidth ? 1f : Math.Clamp(1f - (-off - HalfWidth) / (HalfWidth * 5f), 0.25f, 1f);
            // As a smith's blow it's judged harder: well short is no blow worth the name, though it shapes it a little.
            var judged = off >= -HalfWidth ? 1f : Math.Clamp(1f - (-off - HalfWidth) / (HalfWidth * 2.5f), 0f, 1f);
            // A good blow on hot metal moves it most; on fading metal it moves less and does it no good.
            Shape = Math.Min(1f, Shape + (0.35f + 0.65f * aim) * (0.1f + 0.9f * heat) * quick / BlowsNeeded);
            grade = judged * judged * quick * (0.4f + 0.6f * heat);
        }
        Sample(grade);
        BlowSum += grade;
        BlowCount++;
        if (LastCold && Flaw == PieceFlaw.None && Rand.NextFloat() < CrackChance)
            Flaw = PieceFlaw.Crack;
        _recovery = RecoverySteps;
        (_down, _rising, _jolted) = (0, false, false);
        Blows++;
        NewMark();
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
        w.Int(_wind);
        w.Int(_down);
        w.Float(Raise);
        w.Float(Peak);
        w.Int(LastWindWas);
        w.Float(LastWind);
        w.Bool(LastSlow);
        w.Bool(_rising);
        w.Bool(_jolted);
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
        if (r.Version == 43) // format 43 carried the hammer to the mark: put down as it was, wound from the bar
        {
            Aim.Read(r);
            r.Int();
            r.Float();
        }
        else if (r.Version >= 44) // the hammer wound back and swung (format 44)
        {
            _wind = Math.Sign(r.Int());
            _down = Math.Clamp(r.Int(), 0, 100_000);
            Raise = Math.Clamp(r.Float(), 0f, 1f);
            Peak = Math.Clamp(r.Float(), 0f, 1f);
            LastWindWas = Math.Clamp(r.Int(), -1, 1);
            LastWind = r.Float();
            LastSlow = r.Bool();
            _rising = r.Bool();
            _jolted = r.Bool();
        }
    }
}
