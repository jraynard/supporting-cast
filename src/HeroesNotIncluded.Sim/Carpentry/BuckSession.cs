using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Carpentry;

/// <summary>
/// A felled trunk sawn into logs where it lies (<see cref="Trunk"/>), crown end first, a length at
/// a time (docs/DESIGN.md, "Carpenter").
/// <list type="bullet">
/// <item><b>Saw</b>: W/S (or the mouse up and down) work the crosscut saw, and only the pull cuts.
/// Each cut frees a log.</item>
/// <item><b>The pinch</b>: past halfway through, the trunk's own weight closes the kerf on the blade
/// (<see cref="Pinch"/>), faster for a rushed stroke. Let it close and the saw binds fast. Space
/// drives a wedge in behind the blade (<see cref="Wedging"/>): it holds the kerf open for the rest of
/// the cut, and frees a bound saw.</item>
/// <item><b>A great trunk</b> (an oak's, <see cref="GreatButt"/>): too thick for one, sawn with
/// someone on the other end (<see cref="CrosscutSession"/>), each lead stroke their cue to pull back.
/// Alone, it's slow. With the butt's two lengths left, Space keeps them whole as a great log, for
/// the pit saw, instead of cutting them.</item>
/// </list>
/// The logs are laid by the trunk as they're cut; stopped early, the cut ones are kept and the rest
/// of the trunk waits.
/// </summary>
public sealed class BuckSession : WorkSession
{
    public const string SawKind = "Saw";

    /// <summary>How far through the trunk a cut goes per length of pull (0..1; a full stroke is 1).</summary>
    public const float CutPerStroke = 0.1f;

    /// <summary>How far the saw moves a step on the keys, and the most it can on the mouse.</summary>
    public const float KeyPace = 0.03f, MostPace = 0.045f;

    /// <summary>How much of the way to the mouse the saw goes a step.</summary>
    public const float MouseEase = 0.25f;

    /// <summary>How far through a cut the kerf starts to close.</summary>
    public const float PinchFrom = 0.45f;

    /// <summary>How fast it closes per length of pull at the keys' pace (faster, by the square of the pace).</summary>
    public const float PinchPerStroke = 0.12f;

    /// <summary>Steps to tap a wedge home.</summary>
    public const int WedgeSteps = 36;

    /// <summary>On a great trunk, the share of a cut the lead's own pull makes alone; and how far one of the helper's pulls takes it at best.</summary>
    public const float GuideShare = 0.35f, PullCut = 0.12f;

    /// <summary>A pull against the lead: it jerks the blade, closing the kerf a little.</summary>
    public const float BindJolt = 0.3f;

    /// <summary>The trunk.</summary>
    public Entity Trunk { get; set; } = Entity.Null;

    /// <summary>Logs' lengths in the trunk as the work began.</summary>
    public int Lengths { get; set; }

    /// <summary>The trunk's butt may be kept whole as a great log (and it takes two to saw).</summary>
    public bool GreatButt { get; set; }

    /// <summary>Cuts finished (each a log off the crown end).</summary>
    public int CutsDone { get; private set; }

    /// <summary>The butt kept whole as a great log.</summary>
    public bool KeptGreat { get; private set; }

    /// <summary>How far through the trunk the cut in hand is, 0..1.</summary>
    public float Progress { get; private set; }

    /// <summary>Where the saw is in its stroke: 0 pushed away, 1 pulled right back.</summary>
    public float Stroke { get; private set; }

    /// <summary>How far the saw moved this step, and whether it's cutting.</summary>
    public float Pace { get; private set; }
    public bool Cutting { get; private set; }

    /// <summary>How closed the kerf is on the blade: at 1, the saw's bound.</summary>
    public float Pinch { get; private set; }

    /// <summary>A wedge is in this cut: the kerf's held open.</summary>
    public bool Wedged { get; private set; }

    /// <summary>A wedge being tapped in: 1 just begun, falling to 0 (0: not).</summary>
    public float Wedging => _wedging / (float)WedgeSteps;

    /// <summary>Times the saw bound.</summary>
    public int Binds { get; private set; }

    /// <summary>The helper's pulls with it, against it, and let go by; and the last (-1 against it) and when.</summary>
    public int Pulls { get; private set; }
    public int HelperBinds { get; private set; }
    public int MissedPulls { get; private set; }
    public float LastPull { get; private set; }
    public int LastPullStep { get; private set; } = -1;

    public bool Bound => Pinch >= 1f;

    /// <summary>Cuts the trunk takes in all: one less than its lengths.</summary>
    public int CutsNeeded => Math.Max(0, Lengths - 1);

    /// <summary>The butt's two lengths are all that's left, uncut: Space may keep them whole.</summary>
    public bool CanKeepGreat => GreatButt && !KeptGreat && Lengths - CutsDone == 2 && Progress <= 0f && _wedging == 0;

    private int _work;
    private int _act;
    private int _wedging;
    private bool _wasDown;
    private float _sum, _weight;

    public override Skill Skill => Skill.Crafting;
    public override float Xp => 8f * Math.Max(1, CutsDone);
    public override string Title => $"Bucking the trunk (cut {Math.Min(CutsDone + 1, Math.Max(1, CutsNeeded))} of {Math.Max(1, CutsNeeded)})";

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Trunk) ? sim.World.Get<Thing>(Trunk).Cell : null;

    public override bool TakesHelper => GreatButt && !Done && CutsDone < CutsNeeded && !KeptGreat;

    public override string AskForHand => "Someone take the other end of this saw! It's a big one.";

    public override string HelpLine => "Coming! I've got the other end.";

    public override HelperSession? HelperFor(Simulation sim, Entity helper) => TakesHelper ? new CrosscutSession { Trunk = Trunk } : null;

    public override bool StillValid(Simulation sim)
    {
        var w = sim.World;
        return Felling.IsTrunk(sim, Trunk) && AvatarSystem.InReach(sim, w.Get<Position>(Worker).Cell, Trunk)
            && Crafting.ToolSpeed(sim, Worker, SawKind) > 0f;
    }

    protected internal override void Begin()
    {
        if (CutsNeeded == 0)
            Done = true; // a short length: nothing to cut, it's a log as it lies
    }

    protected override void Axis2(int value) => _work = value;

    /// <summary>Space: a wedge into the cut (or, with only the butt left, keep it whole).</summary>
    protected override void Act() => _act = 1;

    /// <summary>The helper's pull, between steps: with the stroke it cuts; against it, it closes the kerf.</summary>
    protected override void Received(Beat beat)
    {
        if (Done || _wedging > 0)
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
                HelperBinds++;
                (LastPull, LastPullStep) = (-1f, StepsDone);
                if (!Wedged)
                    Pinch = Math.Min(1f, Pinch + BindJolt * Math.Max(0.3f, beat.Value));
                break;
            case BeatKind.Missed:
                MissedPulls++;
                break;
        }
    }

    protected override void Step()
    {
        Pace = 0f;
        Cutting = false;
        if (Done)
            return;
        if (_wedging > 0)
        {
            if (--_wedging == 0)
                (Wedged, Pinch) = (true, 0f);
            _act = 0;
            return;
        }
        if (_act > 0)
        {
            _act = 0;
            if (CanKeepGreat)
            {
                KeptGreat = true;
                Done = true;
                return;
            }
            if (Progress > 0f && !Wedged)
            {
                _wedging = WedgeSteps;
                return;
            }
        }
        var move = PointV is { } v
            ? Math.Clamp((Math.Clamp(v, 0f, 1f) - Stroke) * MouseEase, -MostPace, MostPace)
            : Math.Clamp(Math.Clamp(Stroke + _work * KeyPace, 0f, 1f) - Stroke, -KeyPace, KeyPace);
        if (MathF.Abs(move) < 0.001f)
            move = 0f;
        Stroke += move;
        Pace = MathF.Abs(move);
        if (move <= 0f)
        {
            _wasDown = false;
            return; // pushed back: the crosscut's teeth cut on the pull
        }
        Cutting = !Bound;
        if (GreatButt && !_wasDown && !Bound)
            Send(BeatKind.Call, 1f - Pinch); // the other end's cue: pull back on this one
        _wasDown = true;
        if (Bound)
            return;
        var rush = Pace / KeyPace;
        if (Progress >= PinchFrom && !Wedged)
        {
            Pinch = Math.Min(1f, Pinch + PinchPerStroke * rush * rush * Pace * (1f + (Rand.NextFloat() * 2f - 1f) * Hands.Tremor));
            if (Bound)
            {
                Binds++;
                Sample(0f);
                return;
            }
        }
        Advance(Pace * CutPerStroke * (GreatButt ? GuideShare : 1f));
    }

    private void Advance(float by)
    {
        if (by <= 0f || Done || Bound)
            return;
        _sum += (1f - Pinch) * by;
        _weight += by;
        Progress = Math.Min(1f, Progress + by);
        if (Progress < 1f)
            return;
        Sample(_weight <= 0f ? 0f : _sum / _weight);
        (_sum, _weight) = (0f, 0f);
        CutsDone++;
        (Progress, Pinch, Wedged) = (0f, 0f, false);
        if (CutsDone >= CutsNeeded)
            Done = true;
    }

    public override void Finish(Simulation sim) => Lay(sim, finished: true);

    public override void Interrupted(Simulation sim) => Lay(sim, finished: false);

    /// <summary>The logs cut are laid by the trunk, and what's left of it waits (or, finished, it's gone).</summary>
    private void Lay(Simulation sim, bool finished)
    {
        var w = sim.World;
        if (!Felling.IsTrunk(sim, Trunk))
            return;
        ref var trunk = ref w.Get<Trunk>(Trunk);
        var logs = 0;
        for (var i = 0; i < CutsDone && trunk.Lengths > 0; i++, logs++)
        {
            Felling.LayLog(sim, Trunk, great: false);
            trunk.Lengths--;
        }
        var great = false;
        if (finished && trunk.Lengths > 0)
        {
            great = KeptGreat && trunk.Lengths >= 2;
            Felling.LayLog(sim, Trunk, great);
            if (!great)
                logs++;
            trunk.Lengths = 0;
        }
        if (trunk.GreatButt && trunk.Lengths < 2)
            trunk.GreatButt = false;
        var at = w.Get<Thing>(Trunk).Cell;
        if (trunk.Lengths <= 0)
            sim.DestroyThing(Trunk);
        var what = great ? (logs > 0 ? $"{Count(logs)} and a great log" : "a great log") : Count(logs);
        if (logs > 0 || great)
            sim.Messages.Tell(Worker, MessageKind.Info, $"You've bucked {what}.{(Binds > 0 ? " The saw bound on you." : "")}", at);

        static string Count(int n) => n == 1 ? "a log" : $"{n} logs";
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Trunk);
        w.Int(Lengths);
        w.Bool(GreatButt);
        w.Int(CutsDone);
        w.Bool(KeptGreat);
        w.Float(Progress);
        w.Float(Stroke);
        w.Float(Pace);
        w.Bool(Cutting);
        w.Float(Pinch);
        w.Bool(Wedged);
        w.Int(Binds);
        w.Int(Pulls);
        w.Int(HelperBinds);
        w.Int(MissedPulls);
        w.Float(LastPull);
        w.Int(LastPullStep);
        w.Int(_work);
        w.Int(_act);
        w.Int(_wedging);
        w.Bool(_wasDown);
        w.Float(_sum);
        w.Float(_weight);
    }

    protected override void ReadState(SaveReader r)
    {
        Trunk = r.Entity();
        Lengths = Math.Clamp(r.Int(), 0, 16);
        GreatButt = r.Bool();
        CutsDone = Math.Clamp(r.Int(), 0, 16);
        KeptGreat = r.Bool();
        Progress = Math.Clamp(r.Float(), 0f, 1f);
        Stroke = Math.Clamp(r.Float(), 0f, 1f);
        Pace = Math.Clamp(r.Float(), 0f, MostPace);
        Cutting = r.Bool();
        Pinch = Math.Clamp(r.Float(), 0f, 1f);
        Wedged = r.Bool();
        Binds = Math.Max(0, r.Int());
        Pulls = Math.Max(0, r.Int());
        HelperBinds = Math.Max(0, r.Int());
        MissedPulls = Math.Max(0, r.Int());
        LastPull = Math.Clamp(r.Float(), -1f, 1f);
        LastPullStep = r.Int();
        _work = Math.Sign(r.Int());
        _act = Math.Sign(r.Int());
        _wedging = Math.Clamp(r.Int(), 0, WedgeSteps);
        _wasDown = r.Bool();
        _sum = Math.Max(0f, r.Float());
        _weight = Math.Max(0f, r.Float());
    }
}
