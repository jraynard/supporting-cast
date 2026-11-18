using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Tinkering;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Carpentry;

/// <summary>What a <see cref="JoinSession"/> is joining.</summary>
public enum JoinWork : byte
{
    /// <summary>A piece of furniture to set back where one burned (<see cref="Refurnishing"/>), at the bench: a mortise cut and a tenon driven for each joint.</summary>
    Furniture,

    /// <summary>A new axle for a party's wagon, where it stands: each arm trimmed to its hub, and the hub driven on.</summary>
    Axle,

    /// <summary>A split board on a party's wagon: a plank across it, pegged (driven only).</summary>
    Board,
}

/// <summary>
/// Joinery with chisel and mallet (docs/DESIGN.md, "Carpenter"). Each joint goes in two stages,
/// both struck as the hewing is: F held raises the mallet, higher the longer it's held, and let go
/// it strikes as hard as it was raised.
/// <list type="bullet">
/// <item><b>Cut</b> (<see cref="Cutting"/>): the chisel takes the mortise down toward its chalk
/// mark a blow at a time; Space when it's deep enough. Short of the mark the tenon won't seat
/// home; past it, the joint is loose. How near to take it is the joiner's to judge.</item>
/// <item><b>Drive</b>: the tenon (or the peg, the hub) is driven in, tightening as it goes home.
/// A blow harder than the joint will take (<see cref="SafeAt"/>, the gauge's mark, lower as it
/// tightens) splits it: forced home, cracked, and worth little. Light blows near the end.</item>
/// </list>
/// The joints' mean is the work's quality.
/// </summary>
public sealed class JoinSession : WorkSession
{
    /// <summary>How fast holding F raises the mallet, per step (raised all the way at 1).</summary>
    public const float RaisePerStep = 0.016f;

    /// <summary>How deep a blow raised all the way cuts (the mark is at 1).</summary>
    public const float DeepestCut = 0.32f;

    /// <summary>Within this short of the mark (before skill widens it) a mortise is right; past the mark, each bit deeper costs this much more.</summary>
    public const float Snug = 0.08f, LooseCost = 2.5f;

    /// <summary>How far a blow raised all the way drives the tenon in while it's free (home at 1).</summary>
    public const float DeepestDrive = 0.4f;

    /// <summary>The hardest blow a joint takes as it starts in, and once it's nearly home: past it, it splits.</summary>
    public const float SafeAtStart = 1f, SafeAtHome = 0.35f;

    /// <summary>A split joint's worth against a sound one.</summary>
    public const float SplitWorth = 0.25f;

    /// <summary>Steps a finished joint sits before the next comes up, so it's plain one's done.</summary>
    public const int SettleSteps = 45;

    /// <summary>The bench (furniture); Entity.Null at a wagon.</summary>
    public Entity Station { get; set; } = Entity.Null;

    /// <summary>The wagon (an axle, a board); Entity.Null at the bench.</summary>
    public Entity Wagon { get; set; } = Entity.Null;

    public JoinWork Work { get; set; }

    /// <summary>The furniture being made (its def's name), for <see cref="JoinWork.Furniture"/>.</summary>
    public string Piece { get; set; } = "";

    /// <summary>The burned piece it's to replace (<see cref="Refurnishing"/>), 0 if none.</summary>
    public int Loss { get; set; }

    public int Joints { get; set; } = 2;

    /// <summary>The joint in hand (all done at <see cref="Joints"/>).</summary>
    public int Joint { get; private set; }

    /// <summary>Cutting the mortise (true), or driving the joint home.</summary>
    public bool Cutting { get; private set; }

    /// <summary>How deep the mortise is: 1 at the mark, more is past it.</summary>
    public float Depth { get; private set; }

    /// <summary>How far home the tenon is driven, 0..1.</summary>
    public float Seat { get; private set; }

    /// <summary>How high the mallet's raised now (0 down, 1 as high as it goes).</summary>
    public float Raised { get; private set; }

    /// <summary>Blows struck: for the sound.</summary>
    public int Blows { get; private set; }

    /// <summary>Joints split so far, and whether the last one did (for the window to say so).</summary>
    public int Splits { get; private set; }

    public bool JustSplit { get; private set; }

    /// <summary>Steps left of a finished joint sitting before the next (0: working).</summary>
    public int Settling { get; private set; }

    private bool _raising;
    private int _accept;
    private float _cutScore = 1f;

    // ---------------- An axle: the lifter's work too (Work.Pairs) ----------------

    /// <summary>On an axle, the share of a blow's drive that goes home with the wagon's weight on the ground.</summary>
    public const float GroundShare = 0.2f;

    /// <summary>What a blow struck with the wagon dropped or tilted does: knocks the hub back off its seat.</summary>
    public const float JoltBack = 0.15f;

    /// <summary>The rest of the last blow's drive, waiting on the lifter's answer.</summary>
    private float _pending;

    /// <summary>Blows the lifter held level for, and blows that jolted; how the last went (-1 a jolt) and when.</summary>
    public int Level { get; private set; }
    public int Jolts { get; private set; }
    public float LastLift { get; private set; }
    public int LastLiftStep { get; private set; } = -1;

    /// <summary>
    /// A new axle wants the wagon lifted while the hub's driven on: someone takes the weight and
    /// holds it level (<see cref="LiftSession"/>). Alone, with it on the ground, a blow barely moves the hub.
    /// </summary>
    public override bool TakesHelper => Work == JoinWork.Axle;

    public override string AskForHand => "Someone lift the wagon! I can't drive the hub on with its weight on the ground.";

    public override string HelpLine => "Coming! I'll take the weight.";

    public override HelperSession? HelperFor(Simulation sim, Entity helper) => TakesHelper ? new LiftSession { Wagon = Wagon } : null;

    /// <summary>The lifter's answer to a blow on the hub: held level, the rest of it goes home; dropped or tilted, it jolts back.</summary>
    protected override void Received(Beat beat)
    {
        if (Work != JoinWork.Axle || Cutting || Settling > 0)
            return;
        switch (beat.Kind)
        {
            case BeatKind.Strike:
            {
                var v = Math.Clamp(beat.Value, 0f, 1f);
                Level++;
                (LastLift, LastLiftStep) = (v, StepsDone);
                Seat = Math.Min(1f, Seat + _pending * (0.5f + 0.5f * v));
                _pending = 0f;
                if (Seat >= 1f)
                    Seated();
                break;
            }
            case BeatKind.Offbeat:
                Jolts++;
                (LastLift, LastLiftStep) = (-1f, StepsDone);
                Seat = Math.Max(0f, Seat - JoltBack);
                _pending = 0f;
                _cutScore = Math.Max(0f, _cutScore - 0.1f); // the arm's knocked about in the hub
                break;
        }
    }

    public float SnugWidth => Snug * Hands.Tolerance;

    /// <summary>Does this work cut a mortise first, or only drive?</summary>
    public bool Cuts => Work != JoinWork.Board;

    public override Skill Skill => Skill.Crafting;
    public override float Xp => 12f + 8f * Joints;

    public override string Title => Work switch
    {
        JoinWork.Board => $"Pegging the board (peg {Math.Min(Joint + 1, Joints)} of {Joints})",
        JoinWork.Axle => Cutting ? $"Trimming the axle's arm (end {Math.Min(Joint + 1, Joints)} of {Joints})" : "Driving the hub on",
        _ => Cutting ? $"Chopping a mortise (joint {Math.Min(Joint + 1, Joints)} of {Joints})" : "Driving the tenon home",
    };

    public override int3? Focus(Simulation sim) =>
        sim.World.IsAlive(Station) ? sim.World.Get<Thing>(Station).Cell
        : sim.World.IsAlive(Wagon) && sim.World.TryGet<Thing>(Wagon, out var t) ? t.Cell : null;

    /// <summary>The hardest blow the joint takes now, as far home as it is: the gauge's mark.</summary>
    public static float SafeAt(float seat) => SafeAtStart - (SafeAtStart - SafeAtHome) * Math.Clamp(seat, 0f, 1f);

    /// <summary>How deep a blow raised to <paramref name="raised"/> would cut (without the hand's give): the dashed line.</summary>
    public static float CutDepth(float raised) => raised * DeepestCut;

    /// <summary>How a mortise cut to this depth scores, 0..1: right near the mark, short of it rough, past it loose (worse, faster).</summary>
    public float CutScore(float depth) =>
        depth > 1f ? Math.Max(0f, 1f - (depth - 1f) * LooseCost)
        : depth >= 1f - SnugWidth ? 1f
        : Math.Max(0f, 1f - (1f - SnugWidth - depth) * 1.5f);

    public override bool StillValid(Simulation sim)
    {
        var w = sim.World;
        return Work switch
        {
            JoinWork.Axle => Wagons.AxleProblem(sim, Worker, Wagon) == null,
            JoinWork.Board => Wagons.BoardProblem(sim, Worker, Wagon) == null,
            _ => Carpenters.IsBench(sim, Station) && AvatarSystem.InReach(sim, w.Get<Position>(Worker).Cell, Station)
                && Refurnishing.MakeProblem(sim, Worker, Piece) == null,
        };
    }

    protected internal override void Begin()
    {
        Cutting = Cuts;
        Depth = 0f;
        Seat = 0f;
    }

    /// <summary>Space: the mortise is deep enough.</summary>
    protected override void Act()
    {
        if (Cutting && Settling == 0)
            _accept = 1;
    }

    protected override void Step()
    {
        JustSplit = Settling > 0 && JustSplit;
        if (Settling > 0)
        {
            Raised = 0f;
            _raising = false;
            if (--Settling == 0)
                NextJoint();
            return;
        }
        if (_accept > 0 && !_raising)
        {
            _accept = 0;
            _cutScore = CutScore(Depth);
            Cutting = false;
            return;
        }
        _accept = 0;
        if (Held)
        {
            Raised = Math.Min(1f, Raised + RaisePerStep);
            _raising = true;
            return;
        }
        if (!_raising)
            return;
        // Let go: the blow, as hard as it was raised, give or take the hand's.
        _raising = false;
        var power = Raised * (1f + (Rand.NextFloat() * 2f - 1f) * (0.06f + Hands.Tremor * 0.5f));
        Raised = 0f;
        Blows++;
        if (Cutting)
        {
            Depth += power * DeepestCut;
            return;
        }
        if (power > SafeAt(Seat))
        {
            // Too hard while it's tight: it splits, and goes home cracked.
            Splits++;
            JustSplit = true;
            Sample(_cutScore * SplitWorth);
            Seat = 1f;
            Settling = SettleSteps;
            return;
        }
        // A loose mortise takes the tenon easier; it tightens as it goes home.
        var loose = Depth > 1f ? 1f + (Depth - 1f) : 1f;
        var drive = power * DeepestDrive * (1f - 0.5f * Seat) * loose;
        if (TakesHelper)
        {
            // The hub on an axle: only a little goes home with the wagon on the ground; the rest waits
            // on whoever's lifting it (and their answer to this blow is how level they held it).
            Seat = Math.Min(1f, Seat + drive * GroundShare);
            _pending = drive * (1f - GroundShare);
            Send(BeatKind.Call, power);
        }
        else
            Seat = Math.Min(1f, Seat + drive);
        if (Seat >= 1f)
            Seated();
    }

    /// <summary>The joint's home: scored as its mortise was cut, and the next comes up after a moment.</summary>
    private void Seated()
    {
        JustSplit = false;
        _pending = 0f;
        Sample(_cutScore);
        Settling = SettleSteps;
    }

    private void NextJoint()
    {
        Joint++;
        if (Joint >= Joints)
        {
            Done = true;
            return;
        }
        Cutting = Cuts;
        Depth = 0f;
        Seat = 0f;
        _cutScore = 1f;
    }

    public override void Finish(Simulation sim)
    {
        switch (Work)
        {
            case JoinWork.Axle:
                Wagons.AxleFitted(sim, Worker, Wagon, Quality, Partner);
                break;
            case JoinWork.Board:
                Wagons.BoardPegged(sim, Worker, Wagon, Quality);
                break;
            default:
                Refurnishing.Made(sim, Worker, Piece, Loss, Quality);
                break;
        }
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Station);
        w.Entity(Wagon);
        w.Byte((byte)Work);
        w.String(Piece);
        w.Int(Loss);
        w.Int(Joints);
        w.Int(Joint);
        w.Bool(Cutting);
        w.Float(Depth);
        w.Float(Seat);
        w.Float(Raised);
        w.Int(Blows);
        w.Int(Splits);
        w.Bool(JustSplit);
        w.Int(Settling);
        w.Bool(_raising);
        w.Int(_accept);
        w.Float(_cutScore);
        w.Float(_pending);
        w.Int(Level);
        w.Int(Jolts);
        w.Float(LastLift);
        w.Int(LastLiftStep);
    }

    protected override void ReadState(SaveReader r)
    {
        Station = r.Entity();
        Wagon = r.Entity();
        Work = (JoinWork)Math.Min(r.Byte(), (byte)JoinWork.Board);
        Piece = r.String();
        Loss = r.Int();
        Joints = r.Int();
        if (Joints is < 1 or > 8)
            throw new SaveException($"Corrupt save: a join of {Joints} joints");
        Joint = Math.Clamp(r.Int(), 0, Joints);
        Cutting = r.Bool();
        Depth = Math.Clamp(r.Float(), 0f, 3f);
        Seat = Math.Clamp(r.Float(), 0f, 1f);
        Raised = Math.Clamp(r.Float(), 0f, 1f);
        Blows = Math.Max(0, r.Int());
        Splits = Math.Max(0, r.Int());
        JustSplit = r.Bool();
        Settling = Math.Clamp(r.Int(), 0, SettleSteps);
        _raising = r.Bool();
        _accept = Math.Clamp(r.Int(), 0, 1);
        _cutScore = Math.Clamp(r.Float(), 0f, 1f);
        if (r.Version >= 58) // an axle's lifter (format 58)
        {
            _pending = Math.Clamp(r.Float(), 0f, 1f);
            Level = r.Int();
            Jolts = r.Int();
            LastLift = r.Float();
            LastLiftStep = r.Int();
        }
    }
}
