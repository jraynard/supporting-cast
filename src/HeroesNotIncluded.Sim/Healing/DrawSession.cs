using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Healing;

/// <summary>
/// Laying on hands: drawing one of a patient's wounds into yourself. Hold F to draw. The wound's
/// pain pushes your composure (the needle) away from the calm band, harder the worse the wound,
/// and each kind of wound its own way (<see cref="DrawPattern"/>): a cut jolts, a bruise swells,
/// a burn pulls, frost numbs your hands. A/D bring it back. Out of the band too long and the link
/// snaps: you keep what you'd drawn so far, and a backlash on top. Let go and the drawing pauses,
/// which is allowed. A patient lying in bed, or a steadying mending, widens the band. The result
/// is applied by <see cref="Empathy.Draw"/>.
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

    /// <summary>How much wider the band is with the patient lying in bed.</summary>
    public const float BedsideBand = 0.25f;

    public Entity Patient { get; set; } = Entity.Null;
    public int Wound { get; set; }
    public string PatientName { get; set; } = "";

    /// <summary>How grave the wound was at the start, 0..1 of the part: sets the jolts and the time.</summary>
    public float Gravity { get; set; }

    /// <summary>How the wound's pain moves the needle (from its kind's <see cref="DrawFeelDef"/>).</summary>
    public DrawPattern Pattern { get; set; }
    public float Rate { get; set; } = 1f;
    public float Strength { get; set; } = 1f;

    /// <summary>The patient lies in bed (a calmer draw).</summary>
    public bool Bedside { get; set; }

    /// <summary>How much a steadying mending widens the band (<see cref="Empathy.SteadiedBand"/>).</summary>
    public float Steady { get; set; }

    /// <summary>Composure, 0..1; the band is centred on 0.5.</summary>
    public float Composure { get; private set; } = 0.5f;

    /// <summary>How much of the wound has come across, 0..1.</summary>
    public float Drawn { get; private set; }

    /// <summary>How close the link is to snapping, 0..1.</summary>
    public float Strain { get; private set; }

    public bool Snapped { get; private set; }

    private float _push;
    private int _steer;

    // Numb hands' steering, easing toward what's pressed; a burn's pull's way; a bite's second jolt.
    private float _steerFelt;
    private int _driftWay = 1, _echoIn, _echoWay;

    public float HalfWidth => BandHalfWidth * Hands.Tolerance * (1f + Steady) * (Bedside ? 1f + BedsideBand : 1f);
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
        if (Empathy.Find(sim, patient, wound) is not { } inj)
            return "that wound is gone";
        if (inj.FeltBy != healer)
            return "you haven't felt that hurt under your hands; feel them over first";
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
        var feel = sim.Defs.DrawFeel(inj.Type);
        WorkSystem.Start(sim, healer, new DrawSession
        {
            Patient = patient, Wound = wound, PatientName = sim.World.Get<Creature>(patient).Name,
            Gravity = Math.Clamp(inj.Damage / Math.Max(1f, h.PartMaxHp(inj.Part)), 0f, 1f),
            Pattern = feel?.Pattern ?? DrawPattern.Spike, Rate = feel?.Rate ?? 1f, Strength = feel?.Strength ?? 1f,
            Bedside = HealthSystem.InBed(sim, patient), Steady = Empathy.SteadiedBand(sim, healer),
        });
        // Someone the AI runs keeps still for it; walking off would break the touch.
        var w = sim.World;
        if (!w.Has<Avatar>(patient) && w.Has<Mind>(patient) && w.Get<Creature>(patient).Able)
            AiSystem.Order(sim, patient, new HoldStillJob(healer));
    }

    public override bool StillValid(Simulation sim) => Problem(sim, Worker, Patient, Wound) == null;

    protected override void Axis(int value) => _steer = value;

    protected internal override void Begin() => _driftWay = Rand.NextFloat() < 0.5f ? -1 : 1;

    protected override void Step()
    {
        // The wound's pain comes in jolts, the worse the wound the harder; they ease off between.
        // A jolt about once a second at worst; each would carry the needle 0.1 to 0.3 off if not
        // steered against (a push decays over half a second). Each kind of wound has its own way.
        var size = (0.003f + 0.006f * Gravity) * (1f + Hands.Tremor * 2f) * Strength;
        var jolt = Held && Rand.Chance((0.008f + 0.016f * Gravity) * Rate * (Pattern is DrawPattern.Drift or DrawPattern.Wave ? 0.4f : 1f));
        var way = jolt && Rand.NextFloat() < 0.5f ? -1 : 1;
        var force = 0f;
        switch (Pattern)
        {
            case DrawPattern.Double:
                if (jolt)
                {
                    _push += way * size * 0.75f;
                    (_echoIn, _echoWay) = (8 + Rand.NextInt(6), way);
                }
                if (_echoIn > 0 && --_echoIn == 0)
                    _push += _echoWay * size * 0.75f;
                break;
            case DrawPattern.Wave:
                // A slow swell, a couple of seconds each way: ridden with the keys, not caught.
                if (jolt)
                    _push += way * size * 0.5f;
                if (Held)
                    force = (Triangle(StepsDone, 2.6f) * 2f - 1f) * size * 0.6f;
                break;
            case DrawPattern.Drift:
                if (jolt)
                    _push += way * size * 0.5f;
                if (Held)
                    force = _driftWay * size * 0.2f;
                break;
            case DrawPattern.Grow:
                if (jolt)
                    _push += way * size * (0.5f + 2f * Drawn);
                break;
            default: // Spike and Numb
                if (jolt)
                    _push += way * size;
                break;
        }
        _push *= 0.97f;
        // Numb hands answer the keys late, and keep going after.
        _steerFelt = Pattern == DrawPattern.Numb ? _steerFelt + (_steer - _steerFelt) * 0.04f : _steer;
        Composure = Math.Clamp(Composure + _push + force + _steerFelt * SteerPerStep, 0f, 1f);
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
        w.Int((int)Pattern);
        w.Float(Rate);
        w.Float(Strength);
        w.Bool(Bedside);
        w.Float(Steady);
        w.Float(_steerFelt);
        w.Int(_driftWay);
        w.Int(_echoIn);
        w.Int(_echoWay);
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
        if (r.Version < 4)
            return; // before draw feels: a plain jolting draw
        Pattern = (DrawPattern)r.Int();
        if (!Enum.IsDefined(Pattern))
            throw new SaveException("Corrupt save: a draw's pattern is unknown");
        Rate = r.Float();
        Strength = r.Float();
        Bedside = r.Bool();
        Steady = r.Float();
        _steerFelt = r.Float();
        _driftWay = Math.Sign(r.Int());
        _echoIn = r.Int();
        _echoWay = Math.Sign(r.Int());
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
        Alive(healer) && Sim.World.TryGet<WorkSession>(healer, out var s) && Empathy.HandsOn(s, Pawn);
}
