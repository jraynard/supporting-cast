using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// One heat on the anvil (<see cref="Smithing"/>): hold W (or F, or the mouse's button) to raise
/// the hammer, and let go to bring it down. The blow is as good as the wind: back to the mark is a
/// full one; short of it moves the metal less; past it, the blow comes down too hard and knocks
/// the work out of true and does it little good. The arm slows as it nears the mark (a hammer's
/// weight at the top of its reach), and a hair past still counts: a hand lets go a moment after it
/// sees the mark, never before. Raised, it wanders across the piece,
/// the more for an unsteady arm: A/D (or the mouse moved side to side) keep it over the spot to
/// strike, which moves after every blow, and a blow off the spot glances. A new mark after every blow, and the piece's quality is the average of them all, so a
/// smith's hands decide the blade in place of a skill roll. The piece cools all the while: hot, a
/// blow moves the metal; fading, less; black, barely, and it may crack. The heat ends when it's
/// shaped or too cold to work, and it goes back to the fire.
/// </summary>
public sealed class HammerSession : WorkSession
{
    /// <summary>How far the hammer's raised a step while held (all the way in about two-thirds of a second).</summary>
    public const float WindPerStep = 0.025f;

    /// <summary>How far it comes down a step once let go: from the top in under a quarter of a second.</summary>
    public const float SwingPerStep = 0.075f;

    /// <summary>
    /// Within this of the mark (and past it) the arm slows to <see cref="NearSlow"/> of its pace: the
    /// window to let go in is long enough for a hand and an eye, not only a frame.
    /// </summary>
    public const float Approach = 0.12f, NearSlow = 0.4f;

    /// <summary>Steps of the slowed wind past the mark that still count as on it: the eye and the screen lag the hand.</summary>
    public const int OverGraceSteps = 2;

    /// <summary>A wind under this is a tap, not a blow: let go from there, it strikes nothing.</summary>
    public const float MinWind = 0.12f;

    /// <summary>How much a wind wavers a step for an unsteady hand, per point of tremor.</summary>
    public const float WindWaver = 0.012f;

    /// <summary>How far across the spot to strike may move between blows once the piece is drawn out (the piece is -1 to 1 across).</summary>
    public const float SpotRange = 0.55f;

    /// <summary>A good blow's shaping, in shares of the blows the piece needs: a smith's blow moves the metal.</summary>
    public const float GoodBlow = 1.6f;

    /// <summary>What an overwound blow still does, of a good one's shaping: a little (it never undoes any).</summary>
    public const float OverBlow = 0.15f;

    /// <summary>The least a short blow does, of a good one's shaping (well short; nearer the mark, more).</summary>
    public const float ShortBlow = 0.45f;

    /// <summary>How far A/D move the hammer across a step (the piece is -1 to 1 across, centred on 0).</summary>
    public const float SteerPerStep = 0.012f;

    /// <summary>How fast a raised hammer's wander can grow a step, and how fast it can go, before tremor.</summary>
    public const float Wander = 0.0008f, MaxWander = 0.006f;

    /// <summary>The most one move of the mouse shifts it across.</summary>
    public const float MouseShift = 0.1f;

    /// <summary>Half the width of dead centre, before skill widens it; a blow further off glances, down to <see cref="Glancing"/> of its worth.</summary>
    public const float CentreHalfWidth = 0.12f, Glancing = 0.3f;

    /// <summary>Half the width of a perfect wind's mark, before skill widens it.</summary>
    public const float MarkHalfWidth = 0.07f;

    /// <summary>Steps the hammer rests on the bar after a blow before it can be wound again.</summary>
    public const int RecoverySteps = 10;

    /// <summary>Chance a blow on black iron opens a crack.</summary>
    public const float CrackChance = 0.15f;

    /// <summary>Below this share of the working heat the piece has gone black: the heat's over.</summary>
    public const float TooCold = 0.6f;

    /// <summary>
    /// On heavy work (<see cref="Heavy"/>), what the smith's own hammer does of a blow's shaping: it
    /// taps where the striker's sledge is to fall. Alone, a great piece barely moves.
    /// </summary>
    public const float TapShare = 0.1f;

    /// <summary>A striker's blow on the call, in shares of a good blow's shaping at its best.</summary>
    public const float StrikeShaping = 1.1f;

    /// <summary>Chance a sledge brought down off the beat knocks a crack into the piece.</summary>
    public const float OffbeatCrack = 0.3f;

    private int _recovery, _wind, _steer;
    private bool _falling;
    private float _drift;
    private float _lastTap;

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

    /// <summary>How the last wind sat against its mark: -1 short, 0 on it, 1 a touch past it, 2 well past (too hard); and how high it went (-1 before the first).</summary>
    public int LastWindWas { get; private set; }
    public float LastWind { get; private set; } = -1f;

    /// <summary>The last blow glanced off centre.</summary>
    public bool LastOffCentre { get; private set; }

    /// <summary>Where the hammer is across the piece: 0 dead centre, -1 and 1 off either side.</summary>
    public float Offset { get; private set; }

    /// <summary>Let go: it's coming down, and can't be raised again till it lands.</summary>
    public bool Swinging => _falling;

    /// <summary>Raising the hammer or bringing it down.</summary>
    public override bool HandsMoving => Raise > 0f || _falling || _recovery > 0;

    public float CentreWidth => CentreHalfWidth * Hands.Tolerance;

    /// <summary>Where across the piece to strike this blow (-1 to 1); it moves after every blow.</summary>
    public float Spot { get; private set; }

    /// <summary>How far either side the spot to strike may go now: the bar's drawn out as it's shaped, from 0.6 of <see cref="SpotRange"/> to all of it.</summary>
    public float SpotReach => SpotRange * (0.6f + 0.4f * Math.Clamp(Shape, 0f, 1f));

    /// <summary>Over the spot near enough for a true blow.</summary>
    public bool Centred => MathF.Abs(Offset - Spot) <= CentreWidth;

    public float HalfWidth => MarkHalfWidth * Hands.Tolerance;

    /// <summary>How far past the mark still counts as on it (<see cref="OverGraceSteps"/> of the slowed wind).</summary>
    public static float OverGrace => OverGraceSteps * WindPerStep * NearSlow;

    /// <summary>The band a wind counts as on the mark: a little longer past it than short of it.</summary>
    public float BandLow => Mark - HalfWidth;
    public float BandHigh => Mark + HalfWidth + OverGrace;

    /// <summary>Raised into the band now: let go and it's a full blow.</summary>
    public bool InBand => Raise >= BandLow && Raise <= BandHigh;

    /// <summary>How far the hammer rises a step held now: slower near the mark and past it.</summary>
    public float WindRate => Raise >= Mark - Approach ? WindPerStep * NearSlow : WindPerStep;

    /// <summary>The hammer's off the bar after the last blow and can be wound again.</summary>
    public bool Ready => _recovery <= 0;

    /// <summary>How well a blow now would move the metal, 0..1, from its heat.</summary>
    public float Workable => Temp >= Lo || Smithing.GlowOf(Temp) >= Smithing.GlowOf(Lo) ? 1f : Math.Clamp((Temp - Lo * TooCold) / (Lo * (1f - TooCold)), 0f, 1f); // the colour shows it workable: so it is

    public bool Shaped => Shape >= 1f;

    /// <summary>A great piece (<see cref="RecipeDef.TwoHanded"/>): the smith taps, a striker's sledge moves the metal.</summary>
    public bool Heavy => Recipe.TwoHanded && !Repairing;

    /// <summary>The striker's blows this heat, those off the beat, and the smith's calls nobody answered.</summary>
    public int StrikerBlows { get; private set; }
    public int Offbeats { get; private set; }
    public int MissedCalls { get; private set; }

    /// <summary>How the striker's last blow went, 0..1 on the call, -1 off the beat; and the step it came (-1 before any).</summary>
    public float LastStrike { get; private set; }
    public int LastStrikeStep { get; private set; } = -1;

    public override bool TakesHelper => Heavy;

    public override string AskForHand => "A striker at the anvil, someone! I need a hand on the sledge.";

    public override HelperSession? HelperFor(Simulation sim, Entity helper) => Heavy ? SledgeSession.For(sim, this) : null;

    public override string HelpLine => "Coming! I'll take the sledge.";

    /// <summary>
    /// The striker's sledge, sent in by the sim between steps (<see cref="Pairs"/>). On the call it
    /// moves the metal as well as it was struck and as true as the smith's tap placed it; off the
    /// beat it does no good and may crack the piece.
    /// </summary>
    protected override void Received(Beat beat)
    {
        switch (beat.Kind)
        {
            case BeatKind.Strike:
            {
                var heat = Workable;
                var v = Math.Clamp(beat.Value, 0f, 1f);
                Shape = Math.Min(1f, Shape + StrikeShaping * GoodBlow * (0.3f + 0.7f * v) * (0.1f + 0.9f * heat) / BlowsNeeded);
                var grade = v * (0.5f + 0.5f * _lastTap) * (0.4f + 0.6f * heat);
                Sample(grade);
                BlowSum += grade;
                BlowCount++;
                StrikerBlows++;
                (LastStrike, LastStrikeStep) = (v, StepsDone);
                if (heat < 0.5f && Flaw == PieceFlaw.None && Rand.NextFloat() < CrackChance)
                    Flaw = PieceFlaw.Crack;
                if (Shaped)
                    Done = true;
                break;
            }
            case BeatKind.Offbeat:
                Sample(0f);
                BlowCount++;
                Offbeats++;
                (LastStrike, LastStrikeStep) = (-1f, StepsDone);
                if (Flaw == PieceFlaw.None && Rand.NextFloat() < OffbeatCrack)
                    Flaw = PieceFlaw.Crack;
                break;
            case BeatKind.Missed:
                MissedCalls++;
                break;
        }
    }

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

    private void NewMark()
    {
        Mark = Rand.Range(0.35f, 0.9f);
        Spot = Rand.Range(-SpotReach, SpotReach);
    }

    protected internal override void Begin() => NewMark();

    /// <summary>W held raises it (-1); let go, it comes down.</summary>
    protected override void Axis2(int value) => _wind = value;

    /// <summary>A/D move it across the piece.</summary>
    protected override void Axis(int value) => _steer = value;

    /// <summary>The pointer at (or past) this near either edge of the panel keeps the hammer going that way, as A/D would.</summary>
    public const float EdgeU = 0.01f;

    /// <summary>
    /// The mouse moved side to side shifts it across as far as it moves (the panel's width is the
    /// piece's, -1 to 1). Run out of room at either edge, hold it there (or past) and it keeps going.
    /// </summary>
    protected override void Pointed(float u, float v)
    {
        if (PointU is { } was)
            Offset = Math.Clamp(Offset + Math.Clamp((u - was) * 2f, -MouseShift, MouseShift), -1f, 1f);
    }

    protected override void Step()
    {
        if (_recovery > 0)
            _recovery--;
        else
        {
            var raising = (_wind < 0 || Held) && !_falling;
            if (raising)
            {
                // Raised, an unsteady hand wavers.
                Raise = Math.Clamp(Raise + WindRate + (Rand.NextFloat() * 2f - 1f) * Hands.Tremor * WindWaver, 0f, 1f);
                Peak = Math.Max(Peak, Raise);
            }
            else if (Raise > 0f)
            {
                _falling = true; // let go: it comes down from where it was, judged by that
                Raise = Math.Max(0f, Raise - SwingPerStep);
                if (Raise <= 0f)
                {
                    if (Peak >= MinWind)
                        Blow();
                    (Peak, _falling) = (0f, false);
                }
            }
            // Off the bar it wanders across, the more for an unsteady arm; A/D bring it back.
            if (Raise > 0f)
                _drift = Math.Clamp(_drift + (Rand.NextFloat() * 2f - 1f) * Wander * (1f + Hands.Tremor * 4f), -MaxWander, MaxWander);
            else
                _drift *= 0.9f;
            // The mouse held at an edge pushes on as A/D would: the hammer's drift can leave it further out than the panel.
            var steer = _steer != 0 ? _steer : PointU is { } u ? u >= 1f - EdgeU ? 1 : u <= EdgeU ? -1 : 0 : 0;
            Offset = Math.Clamp(Offset + _drift + steer * SteerPerStep, -1f, 1f);
        }
        Temp = Air + (Temp - Air) * Smithing.AirCooling;
        if (Temp < Lo * TooCold)
            Done = true;
    }

    /// <summary>The hammer's come down on the piece: as good as the wind was, and as true.</summary>
    private void Blow()
    {
        // On heavy work the smith's blow is a tap: it places the striker's sledge, and moves little itself.
        var own = Heavy ? TapShare : 1f;
        var off = Peak - Mark;
        var heat = Workable;
        LastCold = heat < 0.5f;
        LastWind = Peak;
        var wide = MathF.Abs(Offset - Spot) - CentreWidth;
        var centre = wide <= 0f ? 1f : Math.Clamp(1f - wide / 0.4f, Glancing, 1f);
        LastOffCentre = centre < 1f;
        float grade;
        var past = off - HalfWidth - OverGrace;
        if (past > HalfWidth)
        {
            // Wound well past it: it comes down too hard, out of true; the metal moves a little.
            LastWindWas = 2;
            var over = Math.Clamp((past - HalfWidth) / (HalfWidth * 3f), 0f, 1f);
            Shape = Math.Min(1f, Shape + own * GoodBlow * OverBlow * (0.1f + 0.9f * heat) * centre / BlowsNeeded);
            grade = 0.1f * (1f - over) * centre;
        }
        else if (past > 0f)
        {
            // A touch past: a bit heavy, a bit less true, fading toward too hard.
            LastWindWas = 1;
            var t = past / HalfWidth;
            Shape = Math.Min(1f, Shape + own * GoodBlow * (1f - (1f - OverBlow) * t) * (0.1f + 0.9f * heat) * centre / BlowsNeeded);
            grade = (0.7f - 0.5f * t) * centre * (0.4f + 0.6f * heat);
        }
        else
        {
            // On the mark, a full blow; short of it, less, the shorter the less.
            LastWindWas = off < -HalfWidth ? -1 : 0;
            var aim = off >= -HalfWidth ? 1f : Math.Clamp(1f - (-off - HalfWidth) / (HalfWidth * 6f), 0f, 1f);
            // As a smith's blow it's judged harder: well short is no blow worth the name, though it shapes it a little.
            var judged = off >= -HalfWidth ? 1f : Math.Clamp(1f - (-off - HalfWidth) / (HalfWidth * 2.5f), 0f, 1f);
            // A good blow on hot metal moves it most; on fading metal it moves less and does it no good. Off centre, it glances.
            Shape = Math.Min(1f, Shape + own * GoodBlow * (ShortBlow + (1f - ShortBlow) * aim) * (0.1f + 0.9f * heat) * centre / BlowsNeeded);
            grade = judged * judged * centre * (0.4f + 0.6f * heat);
        }
        Sample(grade);
        BlowSum += grade;
        BlowCount++;
        if (LastCold && Flaw == PieceFlaw.None && Rand.NextFloat() < CrackChance)
            Flaw = PieceFlaw.Crack;
        if (Heavy)
        {
            _lastTap = grade;
            Send(BeatKind.Call, grade); // the striker's cue: here, now
        }
        _recovery = RecoverySteps;
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
        // Whoever struck for it is remembered on the piece, and on what it becomes.
        if (StrikerBlows > 0 && sim.World.IsAlive(Partner))
            p.Helper = Partner;
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
        w.Int(_steer);
        w.Bool(_falling);
        w.Float(Raise);
        w.Float(Peak);
        w.Float(Offset);
        w.Float(_drift);
        w.Int(LastWindWas);
        w.Float(LastWind);
        w.Bool(LastOffCentre);
        w.Float(Spot);
        w.Float(_lastTap);
        w.Int(StrikerBlows);
        w.Int(Offbeats);
        w.Int(MissedCalls);
        w.Float(LastStrike);
        w.Int(LastStrikeStep);
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
        else if (r.Version == 44) // format 44 swung it by W and S: put down as it was, resting on the bar
        {
            r.Int(); r.Int(); r.Float(); r.Float(); r.Int(); r.Float(); r.Bool(); r.Bool(); r.Bool();
        }
        else if (r.Version >= 45) // raised while held, let go to strike, kept centred (format 45)
        {
            _wind = Math.Sign(r.Int());
            _steer = Math.Sign(r.Int());
            _falling = r.Bool();
            Raise = Math.Clamp(r.Float(), 0f, 1f);
            Peak = Math.Clamp(r.Float(), 0f, 1f);
            Offset = Math.Clamp(r.Float(), -1f, 1f);
            _drift = Math.Clamp(r.Float(), -MaxWander, MaxWander);
            LastWindWas = Math.Clamp(r.Int(), -1, 2);
            LastWind = r.Float();
            LastOffCentre = r.Bool();
            Spot = r.Version >= 46 ? Math.Clamp(r.Float(), -1f, 1f) : 0f; // the spot to strike moving came with format 46
        }
        if (r.Version >= 54) // the striker at the sledge (format 54)
        {
            _lastTap = Math.Clamp(r.Float(), 0f, 1f);
            StrikerBlows = r.Int();
            Offbeats = r.Int();
            MissedCalls = r.Int();
            LastStrike = Math.Clamp(r.Float(), -1f, 1f);
            LastStrikeStep = r.Int();
        }
    }
}
