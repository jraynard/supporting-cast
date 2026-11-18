using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Carpentry;

/// <summary>
/// Planks from logs as they were made before the sawmill: the round log hewn square, then the beam
/// pit-sawn along its length (docs/DESIGN.md, "Carpenter").
/// <list type="bullet">
/// <item><b>Hew</b> (<see cref="Hewing"/>): the log end on, a square chalked on it. Each of its
/// <see cref="Sides"/> is hewn flat to the line in a few blows: F held raises the axe, higher the
/// longer it's held, and let go it strikes, taking off as deep as it was raised. Short of the line
/// leaves the side rough; past it bites into the beam, which is waste. When the hewer's happy with a
/// side, Space (or a right-click) turns the log a quarter round to the next (<see cref="Turning"/>, a
/// moment with the axe at rest): how near the line to take it is theirs to judge.</item>
/// <item><b>Saw</b>: the beam end on as the top sawyer sees it, the planks' lines chalked across
/// it. W and S (or the mouse up and down) work the long saw, and only the downstroke cuts, taking the
/// cut further along the beam. The blade wanders off the line, more on a rushed stroke, and turns
/// with the grain now and then; A and D (or the mouse side to side) steer it back.</item>
/// </list>
/// How square the hewing came and how true the cuts ran decide how many planks the logs give (<see cref="Yield"/>).
/// </summary>
public sealed class SawSession : WorkSession
{
    // ---------------- Hew ----------------

    /// <summary>Sides hewn to square the log.</summary>
    public const int Sides = 4;

    /// <summary>How fast holding F raises the axe, per step (raised all the way at 1).</summary>
    public const float RaisePerStep = 0.014f;

    /// <summary>How deep a blow raised all the way takes off (a side's wood to the line is 1).</summary>
    public const float DeepestBlow = 0.45f;

    /// <summary>Steps the log takes to be turned to its next side, the axe resting meanwhile.</summary>
    public const int TurnSteps = 40;

    /// <summary>Within this of the line (before skill widens it) a side's flat; past the line, it's bitten into the beam.</summary>
    public const float Flat = 0.07f;

    /// <summary>How much a bite past the line costs, against its depth.</summary>
    public const float BiteCost = 3f;

    // ---------------- Saw ----------------

    /// <summary>Cuts along the beam for one batch.</summary>
    public const int Cuts = 2;

    /// <summary>How far along the beam the cut goes per length of downstroke (the beam is 0..1; a full stroke is 1).</summary>
    public const float CutPerStroke = 0.12f;

    /// <summary>How far the saw moves a step on the keys, and the most it can on the mouse (a rushed stroke).</summary>
    public const float KeyPace = 0.03f, MostPace = 0.045f;

    /// <summary>How much of the way to the mouse the saw goes a step: it follows the hand smoothly, not every twitch of it.</summary>
    public const float MouseEase = 0.25f;

    /// <summary>How near the middle the mouse can be across the beam and not steer at all.</summary>
    public const float SteerDeadZone = 0.08f;

    /// <summary>How far the steering turns the blade back a step.</summary>
    public const float SteerPerStep = 0.008f;

    /// <summary>How far the blade wanders per length of downstroke at the keys' steady pace (more, faster: by the square of the pace).</summary>
    public const float DriftPerStroke = 0.07f;

    /// <summary>On the line (before skill widens it), and as far off it as a cut can be and still count for something.</summary>
    public const float OnLine = 0.04f, OffLine = 0.3f;

    /// <summary>The share of the planks a perfect batch gives, and the least a ruined one does.</summary>
    public const float FullYield = 1f, LeastYield = 0.5f;

    /// <summary>Points along a cut its path is kept at, for the kerf drawn.</summary>
    public const int Marks = 80;

    /// <summary>The sawhorse.</summary>
    public Entity Station { get; set; } = Entity.Null;

    // ---------------- A great log: the pitman's work too (Work.Pairs) ----------------

    /// <summary>
    /// A great log (a <c>twoHanded</c> recipe): too long for one. Hewn as any, but sawn with a
    /// pitman below (<see cref="PitSession"/>): each downstroke the top sawyer starts is the call, and
    /// it's the pitman's pull that cuts. Alone, the top sawyer's own stroke barely moves it.
    /// </summary>
    public bool Great { get; set; }

    /// <summary>On a great log, the share of a cut the top sawyer's own downstroke makes.</summary>
    public const float GuideShare = 0.12f;

    /// <summary>How far one of the pitman's pulls takes the cut along, at its best (about seven to a cut).</summary>
    public const float PullCut = 0.15f;

    /// <summary>A pull against the top sawyer (no call): it binds the saw, knocking the blade off its line and the cut back.</summary>
    public const float BindJolt = 0.2f, BindLoss = 0.03f;

    /// <summary>The pitman's pulls on the call, off it, and calls let go by; and how the last went (-1 off the beat) and when.</summary>
    public int Pulls { get; private set; }
    public int Binds { get; private set; }
    public int MissedPulls { get; private set; }
    public float LastPull { get; private set; }
    public int LastPullStep { get; private set; } = -1;

    private bool _wasDown;

    public override bool TakesHelper => Great && !Hewing;

    public override string AskForHand => "A pitman, someone! This log's too long for one saw.";

    public override string HelpLine => "Coming! I'll get in the pit.";

    public override HelperSession? HelperFor(Simulation sim, Entity helper) => TakesHelper ? new PitSession { Station = Station } : null;

    /// <summary>The pitman's pull, sent in by the sim between steps: on the call it cuts; against the saw it binds.</summary>
    protected override void Received(Beat beat)
    {
        if (Hewing || Cut >= Cuts)
            return;
        switch (beat.Kind)
        {
            case BeatKind.Strike:
            {
                var v = Math.Clamp(beat.Value, 0f, 1f);
                Pulls++;
                (LastPull, LastPullStep) = (v, StepsDone);
                Advance(PullCut * (0.3f + 0.7f * v));
                break;
            }
            case BeatKind.Offbeat:
                Binds++;
                (LastPull, LastPullStep) = (-1f, StepsDone);
                Offset = Math.Clamp(Offset + Drift * BindJolt * Math.Max(0.3f, beat.Value), -1f, 1f);
                Progress = Math.Max(0f, Progress - BindLoss);
                break;
            case BeatKind.Missed:
                MissedPulls++;
                break;
        }
    }

    /// <summary>Still squaring the log (true), or sawing the beam.</summary>
    public bool Hewing { get; private set; } = true;

    /// <summary>The side being hewn (all done at <see cref="Sides"/>).</summary>
    public int Side { get; private set; }

    /// <summary>The wood left proud of the line on each side (1 the round log's, 0 flat to the line; negative: bitten past it).</summary>
    public float[] Proud { get; private set; } = new float[Sides];

    /// <summary>How high the axe is raised now (0 down, 1 as high as it goes).</summary>
    public float Raised { get; private set; }

    /// <summary>Blows struck: for the sound.</summary>
    public int Blows { get; private set; }

    private bool _raising;
    private float _hewSum;
    private int _turning;
    private int _turnNow;

    /// <summary>The log being turned to its next side: 1 just begun, falling to 0 as it comes round (0: not turning).</summary>
    public float Turning => _turning / (float)TurnSteps;

    /// <summary>The cut being sawn (all done when it reaches <see cref="Cuts"/>).</summary>
    public int Cut { get; private set; }

    /// <summary>How far along the beam the cut in hand has gone, 0..1.</summary>
    public float Progress { get; private set; }

    /// <summary>Where the saw is in its stroke: 0 drawn up, 1 pulled right down.</summary>
    public float Stroke { get; private set; }

    /// <summary>How far the saw moved this step: what the game shows as sawdust and hears as the rasp.</summary>
    public float Pace { get; private set; }

    /// <summary>Going down (cutting) this step.</summary>
    public bool Cutting { get; private set; }

    /// <summary>Where the blade is across the beam: 0 on the chalk line, negative left, positive right (−1..1).</summary>
    public float Offset { get; private set; }

    /// <summary>Which way the blade's drifting now (−1 or +1): it turns now and then, so it can't be leaned against for good.</summary>
    public int Drift { get; private set; } = 1;

    /// <summary>The kerf so far in the cut in hand: where the blade was at each point along.</summary>
    public float[] Path { get; private set; } = new float[Marks];

    /// <summary>Where each finished cut ended up off its line, for drawing them on the beam's end.</summary>
    public float[] CutOffsets { get; private set; } = new float[Cuts];

    private int _steer;
    private int _work;
    private float _sum, _weight;

    /// <summary>The hand's shake on the blade, smoothed: a slow waver, not a fresh jolt every step.</summary>
    private float _wobble;

    public float OnLineWidth => OnLine * Hands.Tolerance;
    public float FlatWidth => Flat * Hands.Tolerance;

    public override Skill Skill => Skill.Crafting;
    public override float Xp => 40f;
    public override string Title => Hewing
        ? $"Hewing the log square (side {Math.Min(Side + 1, Sides)} of {Sides})"
        : $"Sawing planks (cut {Math.Min(Cut + 1, Cuts)} of {Cuts})";

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Station) ? sim.World.Get<Thing>(Station).Cell : null;

    /// <summary>How many of the recipe's <paramref name="full"/> planks a batch of this <paramref name="quality"/> gives.</summary>
    public static int Yield(int full, float quality) =>
        Math.Max(1, (int)MathF.Round(full * (LeastYield + (FullYield - LeastYield) * Math.Clamp(quality, 0f, 1f))));

    public override bool StillValid(Simulation sim)
    {
        var w = sim.World;
        return Carpenters.IsSawhorse(sim, Station) && AvatarSystem.InReach(sim, w.Get<Position>(Worker).Cell, Station)
            && w.Get<Workbench>(Station) is { Active: { } r, Stocked: true } bench && bench.By == Worker && Carpenters.Saws(r);
    }

    protected override void Begin()
    {
        Array.Fill(Proud, 1f);
        Drift = Rand.Chance(0.5f) ? 1 : -1;
    }

    protected override void Cloned()
    {
        Proud = (float[])Proud.Clone();
        Path = (float[])Path.Clone();
        CutOffsets = (float[])CutOffsets.Clone();
    }

    protected override void Axis(int value) => _steer = value;

    /// <summary>Space (or a right-click): this side's done, turn the log to the next.</summary>
    protected override void Act()
    {
        if (Hewing)
            _turnNow = 1;
    }

    protected override void Axis2(int value) => _work = value;

    protected override void Step()
    {
        if (Hewing)
            StepHew();
        else
            StepSaw();
    }

    // ---------------- Hew ----------------

    private void StepHew()
    {
        if (_turning > 0)
        {
            // Being turned: the axe rests, and a hold carried over from the last blow raises nothing yet.
            _turning--;
            _raising = false;
            _turnNow = 0; // a press while it's coming round doesn't turn it again
            Raised = 0f;
            return;
        }
        if (_turnNow > 0 && !_raising)
        {
            _turnNow = 0;
            TurnToNextSide();
            return;
        }
        _turnNow = 0;
        if (Held)
        {
            Raised = Math.Min(1f, Raised + RaisePerStep);
            _raising = true;
            return;
        }
        if (!_raising)
            return;
        // Let go: the blow, as deep as it was raised, give or take the hand's.
        _raising = false;
        var depth = Raised * DeepestBlow * (1f + (Rand.NextFloat() * 2f - 1f) * (0.08f + Hands.Tremor * 0.5f));
        Raised = 0f;
        Blows++;
        Proud[Side] -= depth;
    }

    /// <summary>The side in hand's done as it is: scored, and the log turned to the next (or, the last, it's a beam for the saw).</summary>
    private void TurnToNextSide()
    {
        _hewSum += SideScore(Proud[Side]);
        Side++;
        if (Side < Sides)
        {
            _turning = TurnSteps; // a quarter turn to the next side
            return;
        }
        Sample(_hewSum / Sides);
        Hewing = false;
    }

    /// <summary>
    /// How well a side came out with this much left proud (negative: bitten past the line), 0..1:
    /// near the line, nearly full marks; rougher, less, down to none with the bark still on; past
    /// the line, less again, faster.
    /// </summary>
    public float SideScore(float proud) =>
        proud < 0f ? Math.Max(0f, 1f + proud * BiteCost)
        : proud <= FlatWidth ? 1f - proud / FlatWidth * 0.1f
        : Math.Max(0f, 0.9f - (proud - FlatWidth) * 1.5f);

    /// <summary>How deep a blow raised to <paramref name="raised"/> would go (without the hand's give): what the axe's gauge shows.</summary>
    public static float BlowDepth(float raised) => raised * DeepestBlow;

    // ---------------- Saw ----------------

    private void StepSaw()
    {
        Pace = 0f;
        Cutting = false;
        if (Cut >= Cuts)
            return;
        // The saw: W/S at a steady pace; the mouse eased toward where it points (down the panel), quick as an arm can, no quicker.
        var move = PointV is { } v
            ? Math.Clamp((Math.Clamp(v, 0f, 1f) - Stroke) * MouseEase, -MostPace, MostPace)
            : Math.Clamp(Math.Clamp(Stroke + _work * KeyPace, 0f, 1f) - Stroke, -KeyPace, KeyPace);
        if (MathF.Abs(move) < 0.001f)
            move = 0f; // a still hand is still
        Stroke += move;
        Pace = MathF.Abs(move);
        // Steering: A/D, or the mouse across the beam (u 0..1, −1..1), nothing near the middle.
        var steer = _steer;
        if (PointU is { } u)
        {
            var across = u * 2f - 1f;
            steer = MathF.Abs(across) < SteerDeadZone ? 0 : Toward(Offset, across, SteerPerStep);
        }
        Offset = Math.Clamp(Offset + steer * SteerPerStep, -1f, 1f);
        // Only the downstroke cuts; the upstroke just draws it back.
        if (move <= 0f)
        {
            _wasDown = false;
            return;
        }
        Cutting = true;
        if (Great && !_wasDown)
            Send(BeatKind.Call, Accuracy(Offset)); // the pitman's cue: pull now
        _wasDown = true;
        if (Rand.Chance(0.006f))
            Drift = -Drift;
        var rush = Pace / KeyPace;
        _wobble = _wobble * 0.92f + (Rand.NextFloat() * 2f - 1f) * Hands.Tremor * 0.4f * 0.08f;
        Offset = Math.Clamp(Offset + (Drift * DriftPerStroke * rush + _wobble * DriftPerStroke) * Pace, -1f, 1f);
        Advance(Pace * CutPerStroke * (Great ? GuideShare : 1f));
    }

    /// <summary>The cut goes along by <paramref name="by"/> with the blade where it is: the kerf drawn, and the cut finished at the end.</summary>
    private void Advance(float by)
    {
        if (by <= 0f || Cut >= Cuts)
            return;
        _sum += Accuracy(Offset) * by;
        _weight += by;
        var before = Progress;
        Progress = Math.Min(1f, Progress + by);
        for (var i = (int)(before * Marks); i < Math.Min(Marks, (int)(Progress * Marks) + 1); i++)
            Path[i] = Offset;
        if (Progress < 1f)
            return;
        Sample(_weight <= 0f ? 0f : _sum / _weight);
        (_sum, _weight) = (0f, 0f);
        CutOffsets[Cut] = Offset;
        Cut++;
        Progress = 0f;
        Offset = 0f; // the next cut starts on its own line
        Array.Clear(Path);
        if (Cut >= Cuts)
            Done = true;
    }

    /// <summary>How well the cut's going with the blade this far off the line, 0..1.</summary>
    public float Accuracy(float offset)
    {
        var off = MathF.Abs(offset);
        return off <= OnLineWidth ? 1f : Math.Max(0f, 1f - (off - OnLineWidth) / (OffLine - OnLineWidth));
    }

    public override void Finish(Simulation sim)
    {
        var w = sim.World;
        var at = sim.LocationOf(Worker);
        var quality = Quality;
        var made = Crafting.Finish(sim, Station, Worker, Crafting.QualityFrom(quality));
        var total = 0;
        var full = 0;
        foreach (var item in made)
        {
            ref var stack = ref w.Get<Stack>(item);
            full += stack.Count;
            stack.Count = Yield(stack.Count, quality);
            total += stack.Count;
            Inventory.Stow(sim, Worker, item);
        }
        var note = total >= full ? " Square and true." : quality < 0.5f ? " There's a lot of waste in it." : "";
        sim.Messages.Tell(Worker, MessageKind.Info, $"You saw {total} planks.{note}", at);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Station);
        w.Bool(Hewing);
        w.Int(Side);
        foreach (var p in Proud)
            w.Float(p);
        w.Float(Raised);
        w.Int(Blows);
        w.Bool(_raising);
        w.Float(_hewSum);
        w.Int(_turning);
        w.Int(_turnNow);
        w.Int(Cut);
        w.Float(Progress);
        w.Float(Stroke);
        w.Float(Pace);
        w.Bool(Cutting);
        w.Float(Offset);
        w.Int(Drift);
        foreach (var p in Path)
            w.Float(p);
        foreach (var c in CutOffsets)
            w.Float(c);
        w.Int(_steer);
        w.Int(_work);
        w.Float(_sum);
        w.Float(_weight);
        w.Float(_wobble);
        w.Bool(Great);
        w.Int(Pulls);
        w.Int(Binds);
        w.Int(MissedPulls);
        w.Float(LastPull);
        w.Int(LastPullStep);
        w.Bool(_wasDown);
    }

    protected override void ReadState(SaveReader r)
    {
        Station = r.Entity();
        Hewing = r.Bool();
        Side = Math.Clamp(r.Int(), 0, Sides);
        Proud = new float[Sides];
        for (var i = 0; i < Sides; i++)
            Proud[i] = Math.Clamp(r.Float(), -1f, 1f);
        Raised = Math.Clamp(r.Float(), 0f, 1f);
        Blows = Math.Max(0, r.Int());
        _raising = r.Bool();
        _hewSum = Math.Max(0f, r.Float());
        _turning = Math.Clamp(r.Int(), 0, TurnSteps);
        _turnNow = Math.Sign(r.Int());
        Cut = Math.Clamp(r.Int(), 0, Cuts);
        Progress = Math.Clamp(r.Float(), 0f, 1f);
        Stroke = Math.Clamp(r.Float(), 0f, 1f);
        Pace = Math.Clamp(r.Float(), 0f, MostPace);
        Cutting = r.Bool();
        Offset = Math.Clamp(r.Float(), -1f, 1f);
        Drift = r.Int() < 0 ? -1 : 1;
        Path = new float[Marks];
        for (var i = 0; i < Marks; i++)
            Path[i] = Math.Clamp(r.Float(), -1f, 1f);
        CutOffsets = new float[Cuts];
        for (var i = 0; i < Cuts; i++)
            CutOffsets[i] = Math.Clamp(r.Float(), -1f, 1f);
        _steer = Math.Sign(r.Int());
        _work = Math.Sign(r.Int());
        _sum = Math.Max(0f, r.Float());
        _weight = Math.Max(0f, r.Float());
        _wobble = Math.Clamp(r.Float(), -1f, 1f);
        if (r.Version >= 58) // a great log, sawn with a pitman (format 58)
        {
            Great = r.Bool();
            Pulls = r.Int();
            Binds = r.Int();
            MissedPulls = r.Int();
            LastPull = r.Float();
            LastPullStep = r.Int();
            _wasDown = r.Bool();
        }
    }
}
