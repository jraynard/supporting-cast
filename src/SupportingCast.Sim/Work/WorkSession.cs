using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Work;

/// <summary>
/// How steady and practised the worker's hands are when the job starts. Fixed for the session, so
/// a client can run the same session ahead of the sim: a wound mid-job shows up as an interruption
/// from the sim, not as a change only one side has seen yet.
/// </summary>
public readonly record struct Hands(float Precision, float Tolerance, int Level)
{
    /// <summary>Jitter amplitude on a 0..1 control: none with perfect hands.</summary>
    public float Tremor => (1f - Precision) * 0.35f;

    public static Hands Of(Simulation sim, Entity e, Skill skill)
    {
        var precision = sim.World.TryGet<Health>(e, out var h) ? Math.Clamp(h.Capacity(Capacity.Manipulation), 0.2f, 1f) : 1f;
        var level = SkillRules.LevelOf(sim, e, skill);
        return new Hands(precision, Math.Clamp(0.75f + 0.05f * level, 0.75f, 1.75f), level);
    }
}

public enum WorkInputKind : byte
{
    Press,
    Hold,
    Axis,
}

/// <summary>One input to a session, stamped with the step it happened on.</summary>
public readonly record struct WorkInput(int Step, WorkInputKind Kind, int Value = 0);

/// <summary>
/// One hands-on job in progress, as a component on the worker. A timing game can't wait on a
/// network round trip, so a session is a pure state machine: <see cref="Advance"/> touches nothing
/// but the session and its own random stream. The client runs a copy ahead for instant feedback
/// (<see cref="WorkPredictor"/>) and sends its inputs in timestamped batches, which the sim replays
/// to the same result. Anything that reads the world belongs in <see cref="StillValid"/> or
/// <see cref="Finish"/>, which only the sim calls. Always stored under this base type.
/// </summary>
public abstract class WorkSession
{
    private float _qualitySum;
    private int _qualitySamples;

    public Entity Worker { get; internal set; }

    /// <summary>When the job began; also names it, so stale inputs can't reach a later one.</summary>
    public long StartTick { get; internal set; }

    /// <summary>How far play has got. In the sim this trails real time by however late the inputs are.</summary>
    public int StepsDone { get; private set; }

    /// <summary>When the sim last heard from the worker. Sim-only, for spotting a player who's gone.</summary>
    internal long LastInputTick { get; set; }

    public Hands Hands { get; internal set; }

    protected internal SimRandom Rand { get; internal set; } = new(1);

    /// <summary>The main input is held down.</summary>
    public bool Held { get; private set; }

    public bool Done { get; protected set; }

    /// <summary>Ended without success, with the reason for the log.</summary>
    public string? Failed { get; protected set; }

    public float Quality => _qualitySamples == 0 ? 0f : _qualitySum / _qualitySamples;

    public abstract Skill Skill { get; }

    /// <summary>Experience for finishing.</summary>
    public abstract float Xp { get; }

    public abstract string Title { get; }

    /// <summary>The target is still there and in reach. Sim-only; checked before every tick.</summary>
    public abstract bool StillValid(Simulation sim);

    /// <summary>Apply the result, once <see cref="Done"/>. Sim-only.</summary>
    public abstract void Finish(Simulation sim);

    /// <summary>Stopped before it was done (walked off, Esc, gone quiet): keep what lasts, such as a piece's heat. Sim-only.</summary>
    public virtual void Interrupted(Simulation sim) { }

    /// <summary>Set up from <see cref="Rand"/> and <see cref="Hands"/>, once they're assigned.</summary>
    protected internal virtual void Begin() { }

    /// <summary>One step of play. Must read only this session's state.</summary>
    protected abstract void Step();

    /// <summary>The main input was pressed (not just held).</summary>
    protected virtual void Press() { }

    /// <summary>A direction held: -1, +1, or 0 when let go.</summary>
    protected virtual void Axis(int value) { }

    /// <summary>Count one moment or blow of work at quality 0..1.</summary>
    protected void Sample(float quality)
    {
        _qualitySum += Math.Clamp(quality, 0f, 1f);
        _qualitySamples++;
    }

    /// <summary>Play up to step <paramref name="upTo"/>, applying each input at the step it was made.</summary>
    public void Advance(IReadOnlyList<WorkInput> inputs, int upTo)
    {
        var next = 0;
        while (StepsDone < upTo && !Done && Failed == null)
        {
            while (next < inputs.Count && inputs[next].Step <= StepsDone)
                Apply(inputs[next++]);
            // Even an input that ends the job uses up its step: the batch carrying it must be
            // able to name a step past it, or the sim would never get the last blow.
            Step();
            StepsDone++;
        }
    }

    private void Apply(WorkInput input)
    {
        switch (input.Kind)
        {
            case WorkInputKind.Press:
                Press();
                break;
            case WorkInputKind.Hold:
                Held = input.Value != 0;
                break;
            case WorkInputKind.Axis:
                Axis(Math.Sign(input.Value));
                break;
        }
    }

    /// <summary>A copy to run ahead on. Subclasses hold only values, so a shallow copy plus the random stream is a full one.</summary>
    public WorkSession Clone()
    {
        var copy = (WorkSession)MemberwiseClone();
        copy.Rand = new SimRandom(1) { State = Rand.State };
        return copy;
    }

    /// <summary>A triangle wave 0..1..0 over <paramref name="period"/> seconds: a sweep with no trig, so no DetMath needed.</summary>
    protected static float Triangle(int steps, float period)
    {
        var t = steps / (float)SimClock.TicksPerSecond / period;
        var f = t - MathF.Floor(t);
        return f < 0.5f ? f * 2f : 2f - f * 2f;
    }

    protected abstract void WriteState(SaveWriter w);
    protected abstract void ReadState(SaveReader r);

    internal static void Write(SaveWriter w, WorkSession s)
    {
        w.String(s.GetType().Name);
        w.Entity(s.Worker);
        w.Long(s.StartTick);
        w.Int(s.StepsDone);
        w.Long(s.LastInputTick);
        w.Float(s.Hands.Precision);
        w.Float(s.Hands.Tolerance);
        w.Int(s.Hands.Level);
        var (a, b, c, d) = s.Rand.State;
        w.UInt(a); w.UInt(b); w.UInt(c); w.UInt(d);
        w.Bool(s.Held);
        w.Bool(s.Done);
        w.NullableString(s.Failed);
        w.Float(s._qualitySum);
        w.Int(s._qualitySamples);
        s.WriteState(w);
    }

    internal static WorkSession Read(SaveReader r)
    {
        var name = r.String();
        WorkSession s = name switch
        {
            nameof(HammerSession) => new HammerSession(),
            nameof(HeatSession) => new HeatSession(),
            nameof(QuenchSession) => new QuenchSession(),
            nameof(SharpenSession) => new SharpenSession(),
            nameof(PourSession) => new PourSession(),
            nameof(StewSession) => new StewSession(),
            nameof(SeasonSession) => new SeasonSession(),
            nameof(WashSession) => new WashSession(),
            nameof(TapSession) => new TapSession(),
            nameof(Hospitality.ScrubSession) => new Hospitality.ScrubSession(),
            nameof(Hospitality.MakeBedSession) => new Hospitality.MakeBedSession(),
            nameof(Healing.DrawSession) => new Healing.DrawSession(),
            nameof(Healing.CastSession) => new Healing.CastSession(),
            nameof(Healing.MeditateSession) => new Healing.MeditateSession(),
            nameof(Locks.ProbeSession) => new Locks.ProbeSession(),
            nameof(Locks.PickSession) => new Locks.PickSession(),
            _ => throw new SaveException($"Corrupt save: unknown work session {name}"),
        };
        s.Worker = r.Entity();
        s.StartTick = r.Long();
        s.StepsDone = r.Int();
        s.LastInputTick = r.Long();
        s.Hands = new Hands(r.Float(), r.Float(), r.Int());
        s.Rand = new SimRandom(1) { State = (r.UInt(), r.UInt(), r.UInt(), r.UInt()) };
        s.Held = r.Bool();
        s.Done = r.Bool();
        s.Failed = r.NullableString();
        s._qualitySum = r.Float();
        s._qualitySamples = r.Int();
        s.ReadState(r);
        return s;
    }
}
