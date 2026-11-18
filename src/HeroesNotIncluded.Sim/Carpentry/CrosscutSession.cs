using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Carpentry;

/// <summary>
/// The other end of a two-man crosscut on a great trunk (<see cref="Pairs"/>,
/// <see cref="BuckSession.GreatButt"/>): each pull the lead starts is the cue to pull back on yours.
/// Hold F to pull: the saw comes your way as long as it's held, and a full pull begun soon after
/// the cue cuts best. Pull with nothing to pull with and you're fighting the lead: the blade jerks
/// and the kerf closes on it. As the pitman's work under a great log (<see cref="PitSession"/>),
/// across a trunk on the ground.
/// </summary>
public sealed class CrosscutSession : HelperSession
{
    /// <summary>How far the saw comes back a step while pulled: a full pull in about half a second.</summary>
    public const float PullPerStep = 0.035f;

    /// <summary>Steps a cue stands to be pulled with, before skill widens it.</summary>
    public const int CallSteps = 36;

    /// <summary>A pull begun this late in the cue still counts, at down to half.</summary>
    public const float LateWorth = 0.5f;

    /// <summary>Steps pulling with no cue before it jerks the blade.</summary>
    public const int BindAfter = 6;

    /// <summary>Steps a word on the last pull stays up.</summary>
    public const int WordSteps = 60;

    public Entity Trunk { get; set; } = Entity.Null;

    /// <summary>How far the saw's pulled this stroke, 0..1.</summary>
    public float Drawn { get; private set; }

    /// <summary>The lead's pulled: pull back now. Steps since the cue.</summary>
    public bool CallOpen { get; private set; }
    public int CallAge { get; private set; }

    /// <summary>The step the pull began in the cue, -1 if not begun.</summary>
    public int PulledAt { get; private set; } = -1;

    public int Pulls { get; private set; }
    public int Binds { get; private set; }
    public int Missed { get; private set; }

    /// <summary>How the last pull went (as the pitman's), its grade and when.</summary>
    public PitResult LastWas { get; private set; }
    public float LastGrade { get; private set; }
    public int LastStep { get; private set; } = -1;

    private int _against;
    private bool _letGo = true;

    public int Window => (int)(CallSteps * Hands.Tolerance);

    public override Skill Skill => Skill.Crafting;
    public override float Xp => Math.Min(30f, 3f * Pulls);
    public override string Title => $"On the other end of {LeadName}'s saw";

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Trunk) && sim.World.TryGet<Thing>(Trunk, out var t) ? t.Cell : null;

    public override bool Follows(WorkSession lead) => lead is BuckSession { GreatButt: true } b && b.Trunk == Trunk;

    protected override bool LeadAtIt(Simulation sim) =>
        sim.World.TryGet<WorkSession>(Partner, out var s) && s is BuckSession { GreatButt: true } b && b.Trunk == Trunk && b.TakesHelper;

    protected override bool InPlace(Simulation sim) =>
        Felling.IsTrunk(sim, Trunk) && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Trunk);

    protected override void Received(Beat beat)
    {
        if (beat.Kind != BeatKind.Call)
            return;
        if (CallOpen)
            Miss();
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
            (Drawn, _against, _letGo) = (0f, 0, true);
            if (CallOpen && PulledAt >= 0)
                Land();
            return;
        }
        if (!CallOpen)
        {
            if (++_against < BindAfter)
                return;
            Binds++;
            Sample(0f);
            (LastWas, LastGrade, LastStep, _letGo) = (PitResult.Bound, 0f, StepsDone, false);
            _against = -3 * BindAfter;
            Send(BeatKind.Offbeat, 0.6f);
            return;
        }
        if (!_letGo)
            return;
        if (PulledAt < 0)
            PulledAt = CallAge;
        Drawn = Math.Min(1f, Drawn + PullPerStep + (Rand.NextFloat() * 2f - 1f) * Hands.Tremor * 0.01f);
        if (Drawn >= 1f)
        {
            Land();
            _letGo = false;
        }
    }

    private void Land()
    {
        var onTime = Window / 3;
        var timing = PulledAt <= onTime ? 1f : 1f - (1f - LateWorth) * (PulledAt - onTime) / (float)Math.Max(1, Window - onTime);
        var grade = Math.Clamp(Drawn * timing * (0.85f + 0.15f * Hands.Precision), 0f, 1f);
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
        w.Entity(Trunk);
        w.Float(Drawn);
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
        Trunk = r.Entity();
        Drawn = Math.Clamp(r.Float(), 0f, 1f);
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
        _against = Math.Clamp(r.Int(), -3 * BindAfter, BindAfter);
        _letGo = r.Bool();
    }
}
