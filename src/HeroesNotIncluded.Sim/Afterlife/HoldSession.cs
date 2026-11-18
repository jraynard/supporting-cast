using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Afterlife;

/// <summary>How the keeper's last pull went, for the panel.</summary>
public enum HoldResult : byte
{
    None,

    /// <summary>Braced as the pull peaked: held.</summary>
    Held,

    /// <summary>Not braced when it peaked, or the grip gave out: they slipped.</summary>
    Slipped,
}

/// <summary>
/// The keeper at a Calling back (<see cref="Pairs"/>): kneeling at the body, holding it against the
/// grave's pull while the empath weaves (<see cref="Healing.CastSession"/>). Each rune the empath keys
/// is a pull that swells and peaks: hold F through the peak and the body's held. The grip tires while
/// it's held and comes back while it's eased, so bracing early (or all the time) leaves nothing for
/// the peak; too late, and the hold slips, and the rune that pulled loses its worth. Steady hands
/// widen the peak to brace through.
/// </summary>
public sealed class HoldSession : HelperSession
{
    /// <summary>Steps from a rune keyed to its pull peaking (a moment to see it come), and the peak's half-width before skill widens it.</summary>
    public const int PeakAt = 36, PeakHalf = 9;

    /// <summary>How fast the grip tires a step while bracing, and comes back while eased.</summary>
    public const float TirePerStep = 0.009f, RestPerStep = 0.012f;

    /// <summary>Below this grip, a hold gives at the peak whatever else.</summary>
    public const float WeakGrip = 0.2f;

    /// <summary>Of the peak's steps, the share that must be braced through to hold.</summary>
    public const float BracedShare = 0.6f;

    /// <summary>Steps a word on the last pull stays up (the panel reads it).</summary>
    public const int WordSteps = 70;

    /// <summary>The body they're holding.</summary>
    public Entity Body { get; set; } = Entity.Null;

    /// <summary>Whose body, for the title.</summary>
    public string BodyName { get; set; } = "";

    /// <summary>How much grip is left, 0..1.</summary>
    public float Grip { get; private set; } = 1f;

    /// <summary>A pull coming: steps since its rune was keyed (-1 with none).</summary>
    public int PullAge { get; private set; } = -1;

    /// <summary>Steps of this pull's peak braced through so far.</summary>
    public int Braced { get; private set; }

    public int Holds { get; private set; }
    public int Slips { get; private set; }

    public HoldResult LastWas { get; private set; }
    public int LastStep { get; private set; } = -1;

    public int Half => (int)(PeakHalf * Hands.Tolerance);

    /// <summary>The pull's strength now, 0..1: swelling to the peak, ebbing after.</summary>
    public float Pull => PullAge < 0 ? 0f : Math.Clamp(1f - Math.Abs(PullAge - PeakAt) / (float)PeakAt, 0f, 1f);

    /// <summary>In the peak: brace now.</summary>
    public bool AtPeak => PullAge >= 0 && Math.Abs(PullAge - PeakAt) <= Half;

    public override Skill Skill => Skill.Medicine;
    public override float Xp => Math.Min(30f, 5f * Holds);
    public override string Title => $"Holding {BodyName} for {LeadName}";

    public override int3? Focus(Simulation sim) =>
        sim.World.IsAlive(Body) && sim.World.Has<Position>(Body) ? sim.LocationOf(Body) : null;

    public override bool Follows(WorkSession lead) => lead is Healing.CastSession c && c.Body == Body;

    protected override bool LeadAtIt(Simulation sim) =>
        sim.World.TryGet<WorkSession>(Partner, out var s) && s is Healing.CastSession c && c.Body == Body;

    protected override bool InPlace(Simulation sim) => !Body.IsNull && Revival.BodyBeside(sim, Worker) == Body;

    protected override void Received(Beat beat)
    {
        if (beat.Kind != BeatKind.Call)
            return;
        if (PullAge >= 0)
            Resolve(); // a new rune before the last pull was through: settle it now
        (PullAge, Braced) = (0, 0);
    }

    protected override void Step()
    {
        Grip = Math.Clamp(Grip + (Held ? -TirePerStep : RestPerStep), 0f, 1f);
        if (PullAge < 0)
            return;
        if (AtPeak && Held && Grip > WeakGrip)
            Braced++;
        if (++PullAge > PeakAt + Half)
            Resolve();
    }

    /// <summary>The pull's peak is past: held if it was braced through enough of it, slipped if not.</summary>
    private void Resolve()
    {
        var held = Braced >= (int)MathF.Ceiling((2 * Half + 1) * BracedShare);
        if (held)
        {
            var firm = Math.Clamp(0.4f + 0.6f * Grip, 0f, 1f) * (0.85f + 0.15f * Hands.Precision);
            Holds++;
            Sample(firm);
            (LastWas, LastStep) = (HoldResult.Held, StepsDone);
            Send(BeatKind.Held, firm);
        }
        else
        {
            Slips++;
            Sample(0f);
            (LastWas, LastStep) = (HoldResult.Slipped, StepsDone);
            Send(BeatKind.Slip);
        }
        (PullAge, Braced) = (-1, 0);
    }

    protected override void WriteState(SaveWriter w)
    {
        base.WriteState(w);
        w.Entity(Body);
        w.String(BodyName);
        w.Float(Grip);
        w.Int(PullAge);
        w.Int(Braced);
        w.Int(Holds);
        w.Int(Slips);
        w.Byte((byte)LastWas);
        w.Int(LastStep);
    }

    protected override void ReadState(SaveReader r)
    {
        base.ReadState(r);
        Body = r.Entity();
        BodyName = r.String();
        Grip = Math.Clamp(r.Float(), 0f, 1f);
        PullAge = Math.Max(-1, r.Int());
        Braced = Math.Max(0, r.Int());
        Holds = r.Int();
        Slips = r.Int();
        var was = r.Byte();
        LastWas = Enum.IsDefined((HoldResult)was) ? (HoldResult)was : HoldResult.None;
        LastStep = r.Int();
    }
}
