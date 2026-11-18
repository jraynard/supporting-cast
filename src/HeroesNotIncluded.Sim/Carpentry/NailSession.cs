using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Tinkering;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Carpentry;

/// <summary>How the last blow on a nail went, for the panel and the sound.</summary>
public enum NailBlow : byte
{
    None,

    /// <summary>A light tap that stood the nail up in the wood.</summary>
    Set,

    /// <summary>Square on the head: it went in.</summary>
    Drove,

    /// <summary>Struck off the head, or hard before it was set: it's bent over.</summary>
    Bent,

    /// <summary>Struck off the head, lightly: a miss, a dent in the plank.</summary>
    Missed,

    /// <summary>Driven past flush: the hammer's dented the board.</summary>
    Bruised,
}

/// <summary>
/// A plank nailed across a wagon's sprung board with the carpenter's claw hammer (docs/DESIGN.md,
/// "Carpenter"), a nail at a time.
/// <list type="bullet">
/// <item>F held raises the hammer, higher the longer it's held, and let go it strikes as hard as it
/// was raised. Raised, the head wanders off the nail (<see cref="Offset"/>, more the higher it
/// goes): A/D bring it back over it.</item>
/// <item>The first blow is a tap, to set the nail (<see cref="SetMax"/>): any harder and it bends.
/// Set, it's driven in square blows; off the head it bends (or, lightly, dents the plank).</item>
/// <item>A bent nail is drawn with the claw: Space a few times, and start it again.</item>
/// <item>Driven flush, it's in. A blow much harder than the last bit needed bruises the board.</item>
/// </list>
/// Bent nails and bruises count against the work.
/// </summary>
public sealed class NailSession : WorkSession
{
    public const string HammerKind = "Hammer", ClawKind = "ClawHammer";

    /// <summary>How fast holding F raises the hammer, per step.</summary>
    public const float RaisePerStep = 0.02f;

    /// <summary>The hardest tap that sets a nail rather than bending it.</summary>
    public const float SetMax = 0.35f;

    /// <summary>How far a set nail stands in once tapped, and how far a full blow drives it (flush at 1).</summary>
    public const float SetDepth = 0.15f, DeepestDrive = 0.45f;

    /// <summary>Within this of the nail (across its head, −1..1, before skill widens it) a blow's square.</summary>
    public const float OnHead = 0.12f;

    /// <summary>How far the raised hammer's head drifts off the nail a step, at the top of its raise; and how far A/D bring it back.</summary>
    public const float DriftPerStep = 0.012f, SteerPerStep = 0.02f;

    /// <summary>Space-presses of the claw to draw a bent nail.</summary>
    public const int ClawPulls = 3;

    /// <summary>A blow this much harder than flushing it took bruises the board.</summary>
    public const float BruiseOver = 0.35f;

    /// <summary>Steps a driven nail sits before the next is up.</summary>
    public const int SettleSteps = 30;

    /// <summary>The wagon.</summary>
    public Entity Wagon { get; set; } = Entity.Null;

    /// <summary>Nails to drive in all, and the one in hand.</summary>
    public int Nails { get; set; } = 3;
    public int Nail { get; private set; }

    /// <summary>How far in the nail in hand is (flush at 1), and whether it's been set yet.</summary>
    public float Depth { get; private set; }
    public bool Set { get; private set; }

    /// <summary>It's bent over: claw it out (<see cref="ClawLeft"/> pulls to go).</summary>
    public bool Bent { get; private set; }
    public int ClawLeft { get; private set; }

    /// <summary>How high the hammer's raised (0 down, 1 as high as it goes), and where its head is across the nail (0 over it).</summary>
    public float Raised { get; private set; }
    public float Offset { get; private set; }

    /// <summary>Which way the raised head's drifting (−1, +1).</summary>
    public int Drift { get; private set; } = 1;

    public int Blows { get; private set; }
    public int BentNails { get; private set; }
    public int Bruises { get; private set; }
    public NailBlow LastBlow { get; private set; }

    /// <summary>Steps left of a driven nail sitting before the next (0: working).</summary>
    public int Settling { get; private set; }

    public float OnHeadWidth => OnHead * Hands.Tolerance;

    private bool _raising;
    private int _steer;
    private int _claw;
    private float _nailScore = 1f;

    public override Skill Skill => Skill.Crafting;
    public override float Xp => 8f + 6f * Nails;
    public override string Title => $"Nailing a plank across the board (nail {Math.Min(Nail + 1, Nails)} of {Nails})";

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Wagon) && sim.World.TryGet<Thing>(Wagon, out var t) ? t.Cell : null;

    /// <summary>A claw hammer, or any hammer, to hand.</summary>
    public static bool HasHammer(Simulation sim, Entity who) =>
        Crafting.ToolSpeed(sim, who, ClawKind) > 0f || Crafting.ToolSpeed(sim, who, HammerKind) > 0f;

    public override bool StillValid(Simulation sim) => WagonJoinery.BoardProblem(sim, Worker, Wagon) == null;

    protected internal override void Begin() => Drift = Rand.Chance(0.5f) ? 1 : -1;

    protected override void Axis(int value) => _steer = value;

    /// <summary>Space: a pull on the claw, drawing a bent nail.</summary>
    protected override void Act() => _claw = 1;

    protected override void Step()
    {
        if (Done)
            return;
        if (Settling > 0)
        {
            (Raised, _raising, _claw) = (0f, false, 0);
            if (--Settling == 0)
                NextNail();
            return;
        }
        if (Bent)
        {
            if (_claw > 0 && --ClawLeft <= 0)
                (Bent, Depth, Set, Offset) = (false, 0f, false, 0f); // drawn: a fresh nail in the same hole
            (_claw, Raised, _raising) = (0, 0f, false);
            return;
        }
        _claw = 0;
        // A/D over the nail; raised, the head wanders, more the higher it's held.
        Offset = Math.Clamp(Offset + _steer * SteerPerStep, -1f, 1f);
        if (Held)
        {
            Raised = Math.Min(1f, Raised + RaisePerStep);
            _raising = true;
            if (Rand.Chance(0.01f))
                Drift = -Drift;
            Offset = Math.Clamp(Offset + Drift * DriftPerStep * Raised + (Rand.NextFloat() * 2f - 1f) * Hands.Tremor * 0.01f, -1f, 1f);
            return;
        }
        if (!_raising)
            return;
        _raising = false;
        var power = Raised * (1f + (Rand.NextFloat() * 2f - 1f) * (0.05f + Hands.Tremor * 0.4f));
        Raised = 0f;
        Blows++;
        var square = MathF.Abs(Offset) <= OnHeadWidth;
        if (!Set)
        {
            if (square && power <= SetMax)
            {
                (Set, Depth, LastBlow) = (true, SetDepth, NailBlow.Set);
                return;
            }
            if (power > SetMax * 0.5f)
                BendIt();
            else
                LastBlow = NailBlow.Missed;
            return;
        }
        if (!square)
        {
            if (power > 0.3f)
                BendIt();
            else
            {
                LastBlow = NailBlow.Missed;
                _nailScore = Math.Max(0f, _nailScore - 0.05f);
            }
            return;
        }
        var need = (1f - Depth) / DeepestDrive;
        Depth = Math.Min(1f, Depth + power * DeepestDrive);
        LastBlow = NailBlow.Drove;
        if (Depth < 1f)
            return;
        if (power > need + BruiseOver)
        {
            Bruises++;
            LastBlow = NailBlow.Bruised;
            _nailScore = Math.Max(0f, _nailScore - 0.25f);
        }
        Sample(_nailScore);
        Settling = SettleSteps;
    }

    private void BendIt()
    {
        (Bent, ClawLeft, LastBlow) = (true, ClawPulls, NailBlow.Bent);
        BentNails++;
        _nailScore = Math.Max(0f, _nailScore - 0.3f);
    }

    private void NextNail()
    {
        Nail++;
        if (Nail >= Nails)
        {
            Done = true;
            return;
        }
        (Depth, Set, Offset, _nailScore, LastBlow) = (0f, false, 0f, 1f, NailBlow.None);
    }

    public override void Finish(Simulation sim) => WagonJoinery.BoardPegged(sim, Worker, Wagon, Quality);

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Wagon);
        w.Int(Nails);
        w.Int(Nail);
        w.Float(Depth);
        w.Bool(Set);
        w.Bool(Bent);
        w.Int(ClawLeft);
        w.Float(Raised);
        w.Float(Offset);
        w.Int(Drift);
        w.Int(Blows);
        w.Int(BentNails);
        w.Int(Bruises);
        w.Byte((byte)LastBlow);
        w.Int(Settling);
        w.Bool(_raising);
        w.Int(_steer);
        w.Int(_claw);
        w.Float(_nailScore);
    }

    protected override void ReadState(SaveReader r)
    {
        Wagon = r.Entity();
        Nails = r.Int();
        if (Nails is < 1 or > 8)
            throw new SaveException($"Corrupt save: a plank of {Nails} nails");
        Nail = Math.Clamp(r.Int(), 0, Nails);
        Depth = Math.Clamp(r.Float(), 0f, 1f);
        Set = r.Bool();
        Bent = r.Bool();
        ClawLeft = Math.Clamp(r.Int(), 0, ClawPulls);
        Raised = Math.Clamp(r.Float(), 0f, 1f);
        Offset = Math.Clamp(r.Float(), -1f, 1f);
        Drift = r.Int() < 0 ? -1 : 1;
        Blows = Math.Max(0, r.Int());
        BentNails = Math.Max(0, r.Int());
        Bruises = Math.Max(0, r.Int());
        var blow = r.Byte();
        LastBlow = Enum.IsDefined((NailBlow)blow) ? (NailBlow)blow : NailBlow.None;
        Settling = Math.Clamp(r.Int(), 0, SettleSteps);
        _raising = r.Bool();
        _steer = Math.Sign(r.Int());
        _claw = Math.Sign(r.Int());
        _nailScore = Math.Clamp(r.Float(), 0f, 1f);
    }
}
