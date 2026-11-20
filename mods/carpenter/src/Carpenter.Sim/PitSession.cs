using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Carpentry;

/// <summary>How the pitman's last pull went, for the panel.</summary>
public enum PitResult : byte
{
    None,

    /// <summary>Pulled with the top sawyer's downstroke.</summary>
    WithIt,

    /// <summary>Pulled against the saw (no stroke to pull with): it binds.</summary>
    Bound,

    /// <summary>The stroke came and went without a pull.</summary>
    Missed,
}

/// <summary>
/// The pitman under a great log (<see cref="Pairs"/>, <see cref="SawSession.Great"/>): down in the
/// pit, pulling the long saw down on each stroke the top sawyer starts. Hold F to pull: the saw comes
/// down as long as it's held, and a full pull, begun soon after the call, cuts best. Pull with no
/// stroke to pull with and you're fighting the top sawyer: the saw binds, the blade's knocked off its
/// line and the cut goes back a little. Let go, and the saw goes back up for the next.
/// </summary>
public sealed class PitSession : HelperSession
{
    /// <summary>How far the saw comes down a step while pulled: a full pull in about half a second.</summary>
    public const float PullPerStep = 0.035f;

    /// <summary>Steps a stroke stands to be pulled with, before skill widens it.</summary>
    public const int CallSteps = 36;

    /// <summary>A pull begun this late in the call still counts, at down to half.</summary>
    public const float LateWorth = 0.5f;

    /// <summary>Steps pulling with no call before it binds.</summary>
    public const int BindAfter = 6;

    /// <summary>Steps the saw may be held at the bottom of a full pull, before hanging on fights the lift.</summary>
    public const int HoldGrace = 24;

    /// <summary>Steps a word on the last pull stays up (the panel reads it).</summary>
    public const int WordSteps = 60;

    public Entity Station { get; set; } = Entity.Null;

    /// <summary>How far the saw's pulled down this stroke, 0..1.</summary>
    public float Down { get; private set; }

    /// <summary>Pulling with a stroke.</summary>
    public override bool HandsMoving => Held || Down > 0f && Down < 1f;

    /// <summary>A stroke's begun above: pull now. Steps since it began.</summary>
    public bool CallOpen { get; private set; }
    public int CallAge { get; private set; }

    /// <summary>The step the pull began in the call (for the timing), -1 if not begun.</summary>
    public int PulledAt { get; private set; } = -1;

    public int Pulls { get; private set; }
    public int Binds { get; private set; }
    public int Missed { get; private set; }

    public PitResult LastWas { get; private set; }
    public float LastGrade { get; private set; }
    public int LastStep { get; private set; } = -1;

    private int _against;
    private bool _letGo = true;

    public int Window => (int)(CallSteps * Hands.Tolerance);

    public override Skill Skill => Skill.Crafting;
    public override float Xp => Math.Min(40f, 3f * Pulls);
    public override string Title => $"In the pit under {LeadName}'s saw";

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Station) && sim.World.TryGet<Thing>(Station, out var t) ? t.Cell : null;

    public override bool Follows(WorkSession lead) => lead is SawSession { Great: true } s && s.Station == Station;

    protected override bool LeadAtIt(Simulation sim) =>
        sim.World.TryGet<WorkSession>(Partner, out var s) && s is SawSession { Great: true } saw && saw.Station == Station;

    protected override bool InPlace(Simulation sim) =>
        sim.World.IsAlive(Station) && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Station);

    protected override void Received(Beat beat)
    {
        if (beat.Kind != BeatKind.Call)
            return;
        if (CallOpen)
            Miss(); // a new stroke before the last was pulled with
        (CallOpen, CallAge, PulledAt) = (true, 0, -1);
    }

    private void Miss()
    {
        CallOpen = false;
        Missed++;
        (LastWas, LastGrade, LastStep) = (PitResult.Missed, 0f, StepsDone);
        Send(BeatKind.Missed);
    }

    protected override void Step()
    {
        if (CallOpen && ++CallAge > Window && PulledAt < 0)
            Miss();
        if (!Held)
        {
            // Let go: the saw goes back up with the top sawyer.
            (Down, _against, _letGo) = (0f, 0, true);
            if (CallOpen && PulledAt >= 0)
                Land(); // a short pull, let go before it was full
            return;
        }
        if (!CallOpen)
        {
            // Holding on with no stroke to pull with (or still, after a pull's done, while the top sawyer draws
            // it back up): fighting the saw. It binds, and again if they keep at it. Hanging on at the bottom of a
            // full pull is let off a moment (HoldGrace): it read as "With the stroke!" then "it binds!" at once.
            if (++_against < (Down >= 1f ? BindAfter + HoldGrace : BindAfter))
                return;
            Binds++;
            Sample(0f);
            (LastWas, LastGrade, LastStep, _letGo) = (PitResult.Bound, 0f, StepsDone, false);
            _against = -3 * BindAfter;
            Send(BeatKind.Offbeat, 0.6f);
            return;
        }
        if (!_letGo)
            return; // held from before the stroke began: let go, then pull with it
        if (PulledAt < 0)
            PulledAt = CallAge;
        Down = Math.Min(1f, Down + PullPerStep + (Rand.NextFloat() * 2f - 1f) * Hands.Tremor * 0.01f);
        if (Down >= 1f)
        {
            Land();
            _letGo = false;
        }
    }

    /// <summary>The pull's done: as good as it was full, and as soon as it began after the call.</summary>
    private void Land()
    {
        var onTime = Window / 3;
        var timing = PulledAt <= onTime ? 1f : 1f - (1f - LateWorth) * (PulledAt - onTime) / (float)Math.Max(1, Window - onTime);
        var grade = Math.Clamp(Down * timing * (0.85f + 0.15f * Hands.Precision), 0f, 1f);
        CallOpen = false;
        Pulls++;
        Sample(grade);
        (LastWas, LastGrade, LastStep) = (PitResult.WithIt, grade, StepsDone);
        Send(BeatKind.Strike, grade);
        PulledAt = -1;
    }

    protected override void WriteState(SaveWriter w)
    {
        base.WriteState(w);
        w.Entity(Station);
        w.Float(Down);
        w.Bool(CallOpen);
        w.Int(CallAge);
        w.Int(PulledAt);
        w.Int(Pulls);
        w.Int(Binds);
        w.Int(Missed);
        w.Byte((byte)LastWas);
        w.Float(LastGrade);
        w.Int(LastStep);
        w.Int(_against);
        w.Bool(_letGo);
    }

    protected override void ReadState(SaveReader r)
    {
        base.ReadState(r);
        Station = r.Entity();
        Down = Math.Clamp(r.Float(), 0f, 1f);
        CallOpen = r.Bool();
        CallAge = Math.Max(0, r.Int());
        PulledAt = r.Int();
        Pulls = r.Int();
        Binds = r.Int();
        Missed = r.Int();
        var was = r.Byte();
        LastWas = Enum.IsDefined((PitResult)was) ? (PitResult)was : PitResult.None;
        LastGrade = r.Float();
        LastStep = r.Int();
        _against = Math.Max(0, r.Int());
        _letGo = r.Bool();
    }
}
