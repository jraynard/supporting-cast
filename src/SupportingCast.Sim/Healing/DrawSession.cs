using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Systems;
using SupportingCast.Sim.Work;

namespace SupportingCast.Sim.Healing;

/// <summary>
/// Laying on hands: drawing one of a patient's wounds into yourself. Hold F to draw. The wound's
/// pain jolts your composure (the needle) away from the calm band, harder the worse the wound;
/// A/D bring it back. Out of the band too long and the link snaps: you keep what you'd drawn so
/// far, and a backlash on top. Let go and the drawing pauses, which is allowed. The result is
/// applied by <see cref="Empathy.Draw"/>.
/// </summary>
public sealed class DrawSession : WorkSession
{
    /// <summary>Half the width of the calm band, before skill widens it.</summary>
    public const float BandHalfWidth = 0.14f;

    /// <summary>Steps of drawing a slight wound takes, and how many more a grave one.</summary>
    public const int BaseSteps = 180, StepsPerSeverity = 420;

    /// <summary>How far one step of steering moves composure.</summary>
    public const float SteerPerStep = 0.01f;

    /// <summary>Steps out of the band before the link snaps.</summary>
    public const int SnapSteps = 75;

    public Entity Patient { get; set; } = Entity.Null;
    public int Wound { get; set; }
    public string PatientName { get; set; } = "";

    /// <summary>How grave the wound was at the start, 0..1 of the part: sets the jolts and the time.</summary>
    public float Gravity { get; set; }

    /// <summary>Composure, 0..1; the band is centred on 0.5.</summary>
    public float Composure { get; private set; } = 0.5f;

    /// <summary>How much of the wound has come across, 0..1.</summary>
    public float Drawn { get; private set; }

    /// <summary>How close the link is to snapping, 0..1.</summary>
    public float Strain { get; private set; }

    public bool Snapped { get; private set; }

    private float _push;
    private int _steer;

    public float HalfWidth => BandHalfWidth * Hands.Tolerance;
    public bool Calm => MathF.Abs(Composure - 0.5f) <= HalfWidth;
    private int StepsNeeded => BaseSteps + (int)(StepsPerSeverity * Gravity);

    public override Skill Skill => Skill.Medicine;
    public override float Xp => 40f;
    public override string Title => $"Laying hands on {PatientName}";

    /// <summary>Why this empath can't lay hands on this wound now, or null.</summary>
    public static string? Problem(Simulation sim, Entity healer, Entity patient, int wound)
    {
        var w = sim.World;
        if (!Empathy.Gifted(sim, healer))
            return "you haven't the gift";
        if (healer == patient)
            return "you can't draw your own wounds";
        if (!w.IsAlive(patient) || !w.TryGet<Creature>(patient, out var c) || c.Dead)
            return "there's no one to heal";
        if (!Pathfinder.Touches(w.Get<Position>(healer).Cell, w.Get<Position>(patient).Cell))
            return "you need to be able to touch them";
        if (Empathy.Find(sim, patient, wound) == null)
            return "that wound is gone";
        return null;
    }

    public static void TryStart(Simulation sim, Entity healer, Entity patient, int wound)
    {
        if (Problem(sim, healer, patient, wound) is { } why)
        {
            sim.Messages.Add(MessageKind.Info, $"Can't lay on hands: {why}.", sim.LocationOf(healer));
            return;
        }
        var h = sim.World.Get<Health>(patient);
        var inj = Empathy.Find(sim, patient, wound)!;
        WorkSystem.Start(sim, healer, new DrawSession
        {
            Patient = patient, Wound = wound, PatientName = sim.World.Get<Creature>(patient).Name,
            Gravity = Math.Clamp(inj.Damage / Math.Max(1f, h.PartMaxHp(inj.Part)), 0f, 1f),
        });
        // Someone the AI runs keeps still for it; walking off would break the touch.
        var w = sim.World;
        if (!w.Has<Avatar>(patient) && w.Has<Mind>(patient) && w.Get<Creature>(patient).Able)
            AiSystem.Order(sim, patient, new HoldStillJob(healer));
    }

    public override bool StillValid(Simulation sim) => Problem(sim, Worker, Patient, Wound) == null;

    protected override void Axis(int value) => _steer = value;

    protected override void Step()
    {
        // The wound's pain comes in jolts, the worse the wound the harder; they ease off between.
        // A jolt about once a second at worst; each would carry the needle 0.1 to 0.3 off if not
        // steered against (a push decays over half a second).
        if (Held && Rand.Chance(0.008f + 0.016f * Gravity))
            _push += (Rand.NextFloat() < 0.5f ? -1f : 1f) * (0.003f + 0.006f * Gravity) * (1f + Hands.Tremor * 2f);
        _push *= 0.97f;
        Composure = Math.Clamp(Composure + _push + _steer * SteerPerStep, 0f, 1f);
        var calm = Calm;
        Strain = calm ? Math.Max(0f, Strain - 1f / (SnapSteps * 2)) : Strain + 1f / SnapSteps;
        if (Strain >= 1f)
        {
            Snapped = true;
            Done = true;
            return;
        }
        if (!Held)
            return;
        Drawn += (calm ? 1f : 0.3f) / StepsNeeded;
        if (StepsDone % 10 == 0)
            Sample(calm ? 1f : 0.2f);
        Done = Drawn >= 1f;
    }

    public override void Finish(Simulation sim)
    {
        var w = sim.World;
        var at = sim.LocationOf(Worker);
        var drew = Empathy.Draw(sim, Worker, Patient, Wound, Math.Min(Drawn, 1f), Snapped);
        if (Snapped)
            sim.Messages.Add(MessageKind.Warning, drew > 0
                ? $"Your focus breaks and the link snaps back at you. Part of {PatientName}'s wound came with it."
                : "Your focus breaks and the link snaps back at you.", at);
        else if (drew <= 0)
            sim.Messages.Add(MessageKind.Info, $"You can't take more of that into yourself and live.", at);
        else
            sim.Messages.Add(MessageKind.Good, $"You draw {PatientName}'s wound into yourself. It's yours now.", at);
        if (drew > 0 && w.Has<Mood>(Patient))
            MoodSystem.Remember(sim, Patient, sim.Defs.Thoughts.TendedMe, Worker);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Patient);
        w.Int(Wound);
        w.String(PatientName);
        w.Float(Gravity);
        w.Float(Composure);
        w.Float(Drawn);
        w.Float(Strain);
        w.Bool(Snapped);
        w.Float(_push);
        w.Int(_steer);
    }

    protected override void ReadState(SaveReader r)
    {
        Patient = r.Entity();
        Wound = r.Int();
        PatientName = r.String();
        Gravity = r.Float();
        Composure = r.Float();
        Drawn = r.Float();
        Strain = r.Float();
        Snapped = r.Bool();
        _push = r.Float();
        _steer = Math.Sign(r.Int());
    }
}

/// <summary>
/// Held still under an empath's hands: a patient the AI runs stays where they are while someone is
/// drawing one of their wounds, then gets on with their day. (A player's character is theirs to
/// keep still.)
/// </summary>
public sealed class HoldStillJob(Entity healer) : Ai.Job
{
    /// <summary>The longest anyone waits, should the healer leave them hanging.</summary>
    public const int MaxTicks = SimClock.TicksPerSecond * 120;

    protected override IEnumerable<Ai.JobStatus> Run()
    {
        Report = $"Being healed by {Get<Creature>(healer).Name}";
        for (var t = 0; t < MaxTicks && Drawing(); t++)
            yield return Ai.JobStatus.Running;
    }

    private bool Drawing() =>
        Alive(healer) && Sim.World.TryGet<WorkSession>(healer, out var s) && s is DrawSession d && d.Patient == Pawn;
}
