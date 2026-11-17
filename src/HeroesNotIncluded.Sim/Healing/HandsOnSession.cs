using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Healing;

/// <summary>
/// Laying on hands, from the first touch to the last wound drawn (docs/DESIGN.md, "Healer"), in
/// one window so a patient with many hurts is one sitting, not a session each:
/// <list type="bullet">
/// <item><b>Skim to find.</b> A/D run the hands head to feet. Near a wound there's a warmth: it's
/// sensed, no more. W/S jump to the next or last warmth.</item>
/// <item><b>Press to read.</b> Hold F with the hands still on a warmth and it's made out: its kind
/// comes through the hand as it'll pull when drawn (<see cref="Pulse"/>), and how bad it is. A
/// dressed wound takes a longer press, to find festering under it. Pressing while the hands move
/// over a wound is rough: enough of it and the patient flinches, throwing the hands off and
/// losing what was half read.</item>
/// <item><b>Keep pressing to draw.</b> On a wound read, the link opens: its pain pushes your
/// composure about (<see cref="DrawLink"/>) and A/D steer it, while the wound comes across. Let go
/// and the link rests. Composure and strain carry from wound to wound; a snap ends it all.</item>
/// </list>
/// What's read stays read by this empath (<see cref="Injury.FeltBy"/>). A wound drawn whole lands
/// as it's drawn (<see cref="Settle"/>); a part drawn lands when the hands come off.
/// </summary>
public sealed class HandsOnSession : WorkSession
{
    /// <summary>How far the hands move along the body per step (half that pressing in).</summary>
    public const float HandsPerStep = 0.004f;

    /// <summary>How near (body fraction) a wound is felt as a warmth, and how near the hands must be to sense or read it.</summary>
    public const float FeelRange = 0.12f, FindRange = 0.05f;

    /// <summary>Steps near a wound to sense it: a skim at full speed does.</summary>
    public const int SenseSteps = 12;

    /// <summary>Steps of a still press to read a wound, and how many more to find festering under a dressing.</summary>
    public const int ReadSteps = 45, DeepSteps = 75;

    /// <summary>Steps of pressing while moving over a slight wound before the patient flinches (fewer for a grave one).</summary>
    public const int RoughSteps = 40;

    /// <summary>How far a flinch throws the hands back, and how much it jars the link.</summary>
    public const float FlinchThrow = 0.05f, FlinchStrain = 0.25f;

    /// <summary>A wound under the hands, as the session's own copy (so the client can run it ahead).</summary>
    public sealed class Spot
    {
        public int Key;
        public float At;
        public float Gravity;

        /// <summary>How it pulls when drawn, and so how it feels pressed on.</summary>
        public DrawPattern Pattern;
        public float Rate = 1f, Strength = 1f;

        /// <summary>Festering under a dressing: only a long press finds it.</summary>
        public bool Hidden;

        /// <summary>Sensed (a warmth), read (made out), festering found, and drawn across, each 0..1.</summary>
        public float Sensed, Found, Deep, Drawn;

        /// <summary>Sim-only: its draw has landed on the bodies (<see cref="Settle"/>).</summary>
        public bool Applied;

        public Spot Copy() => (Spot)MemberwiseClone();
    }

    public Entity Patient { get; set; } = Entity.Null;
    public string PatientName { get; set; } = "";
    public List<Spot> Spots { get; set; } = [];

    /// <summary>The body's parts, head to feet, for the scene's labels.</summary>
    public List<string> Parts { get; set; } = [];

    /// <summary>The draw's composure and strain, carried across every wound in the sitting.</summary>
    public DrawLink Link { get; set; } = new();

    /// <summary>Where the hands are, 0 (the head) to 1 (the feet).</summary>
    public float Hands01 { get; set; }

    /// <summary>What the hands feel now, 0..1: a warmth rising near a wound, the worse the stronger, and its pulse pressed on.</summary>
    public float Feel { get; private set; }

    /// <summary>The wound pressed on, coming through the hand in its own way (0..1), while it's being read or drawn; 0 otherwise.</summary>
    public float Pulse { get; private set; }

    /// <summary>The wound under the hands (index into <see cref="Spots"/>), or -1.</summary>
    public int Under { get; private set; } = -1;

    /// <summary>The link is open on the wound under the hands: it's being drawn.</summary>
    public bool Linked { get; private set; }

    /// <summary>How near a rough pass is to making the patient flinch, 0..1.</summary>
    public float Rough { get; private set; }

    /// <summary>Times the patient has flinched, and the step of the last.</summary>
    public int Flinches { get; private set; }
    public int LastFlinch { get; private set; } = -1000;

    /// <summary>The last wound drawn whole (index), and the step it came across.</summary>
    public int LastDrawn { get; private set; } = -1;
    public int LastDrawnAt { get; private set; } = -1000;

    private int _move, _jump, _flinchesFelt;

    // How the hands went last step, keys or mouse (Going): a still press reads.
    private int _going;
    private bool _mustRelease;
    private int _linkKey = -1;

    public override Skill Skill => Skill.Medicine;
    public override float Xp => 10f + 40f * Spots.Count(s => s.Drawn >= 1f);
    public override string Title => $"Laying hands on {PatientName}";

    public bool Read(Spot s) => s.Found >= 1f && (!s.Hidden || s.Deep >= 1f);
    public bool FoundFestering(Spot s) => s.Hidden && s.Deep >= 1f;

    /// <summary>Read and not yet drawn whole: what the link can open on.</summary>
    public bool Drawable(Spot s) => Read(s) && s.Drawn < 1f;

    /// <summary>Pressing still on a wound not yet read.</summary>
    public bool Reading => Held && !Linked && !_mustRelease && _going == 0 && Under >= 0 && !Read(Spots[Under]);

    /// <summary>Where a part lies along the body, head to feet: its place in the body's list.</summary>
    public static float PartAt(int part, int parts) => (part + 0.5f) / Math.Max(1, parts);

    /// <summary>
    /// How a wound of this kind comes through a pressing hand, 0..1 at this step: a cut's sharp
    /// jolt, a bite's double tear, a bruise's swell, a burn's steady pull, frost's dull numbness,
    /// venom's ache growing as you read it. The same shapes the draw pulls in.
    /// </summary>
    public static float PulseShape(DrawPattern pattern, int step, float progress)
    {
        switch (pattern)
        {
            case DrawPattern.Double:
            {
                var t = step % 70;
                return Math.Max(Math.Max(0f, 1f - t / 7f), t >= 12 ? Math.Max(0f, 1f - (t - 12) / 7f) * 0.8f : 0f);
            }
            case DrawPattern.Wave:
                return DrawLink.Triangle(step, 2.6f);
            case DrawPattern.Drift:
                return 0.6f + 0.05f * DrawLink.Triangle(step, 0.5f);
            case DrawPattern.Numb:
                return 0.2f + 0.1f * DrawLink.Triangle(step, 3f);
            case DrawPattern.Grow:
                return Math.Clamp(0.15f + 0.85f * progress, 0f, 1f) * (0.8f + 0.2f * DrawLink.Triangle(step, 0.8f));
            default: // a cut, a stab, a shock: a jolt now and then
                return Math.Max(0f, 1f - step % 55 / 8f);
        }
    }

    public static string? Problem(Simulation sim, Entity healer, Entity patient)
    {
        var w = sim.World;
        if (!Empathy.Gifted(sim, healer))
            return "you haven't the gift";
        if (healer == patient)
            return "you already know your own hurts";
        if (!w.IsAlive(patient) || !w.TryGet<Creature>(patient, out var c) || c.Dead || !w.Has<Health>(patient))
            return "there's no one to lay hands on";
        if (!Pathfinder.Touches(w.Get<Position>(healer).Cell, w.Get<Position>(patient).Cell))
            return "you need to be able to touch them";
        return null;
    }

    /// <summary>Lay hands on someone: from the head, or with the hands on the wound <paramref name="at"/> (a key; 0 for none).</summary>
    public static void TryStart(Simulation sim, Entity healer, Entity patient, int at = 0)
    {
        if (Problem(sim, healer, patient) is { } why)
        {
            sim.Messages.Tell(healer, MessageKind.Info, $"Can't lay on hands: {why}.", sim.LocationOf(healer));
            return;
        }
        var h = sim.World.Get<Health>(patient);
        var n = h.Body.Parts.Count;
        var spots = new List<Spot>();
        foreach (var inj in h.Injuries)
        {
            // Wounds on one part lie side by side along it.
            var onPart = spots.Count(s => MathF.Abs(s.At - PartAt(inj.Part, n)) < 0.5f / n);
            var feel = sim.Defs.DrawFeel(inj.Type);
            var known = inj.FeltBy == healer;
            spots.Add(new Spot
            {
                Key = Empathy.KeyOf(inj), At = PartAt(inj.Part, n) + (onPart % 2 == 0 ? 1 : -1) * 0.02f * ((onPart + 1) / 2),
                Gravity = Math.Clamp(inj.Damage / Math.Max(1f, h.PartMaxHp(inj.Part)), 0f, 1f),
                Pattern = feel?.Pattern ?? DrawPattern.Spike, Rate = feel?.Rate ?? 1f, Strength = feel?.Strength ?? 1f,
                Hidden = inj.Infection != null && inj.Tended,
                // What these hands have read before is still known.
                Sensed = known ? 1f : 0f, Found = known ? 1f : 0f, Deep = known && inj.FeltFestering ? 1f : 0f,
            });
        }
        var session = new HandsOnSession
        {
            Patient = patient, PatientName = sim.World.Get<Creature>(patient).Name, Spots = spots,
            Parts = h.Body.Parts.Select(p => p.Label).ToList(),
            Link = new DrawLink { Bedside = HealthSystem.InBed(sim, patient), Steady = Empathy.SteadiedBand(sim, healer) },
        };
        if (spots.Find(s => s.Key == at) is { } start)
            session.Hands01 = start.At;
        WorkSystem.Start(sim, healer, session);
        // Someone the AI runs keeps still for it; walking off would break the touch.
        var w = sim.World;
        if (!w.Has<Avatar>(patient) && w.Has<Mind>(patient) && w.Get<Creature>(patient).Able)
            AiSystem.Order(sim, patient, new HoldStillJob(healer));
    }

    public override bool StillValid(Simulation sim) => Problem(sim, Worker, Patient) == null;

    protected internal override void Begin() => Link.Tolerance = Hands.Tolerance;

    protected override void Cloned()
    {
        Spots = Spots.Select(s => s.Copy()).ToList();
        Link = Link.Copy();
    }

    protected override void Axis(int value) => _move = value;

    protected override void Axis2(int value)
    {
        if (value != 0)
            _jump = value;
    }

    /// <summary>
    /// How the hands go this step: A/D, else toward where the mouse points along the body at the
    /// hands' own pace (so a press as they slide over a hurt is still rough).
    /// </summary>
    private int Going()
    {
        if (_move != 0 || PointU is not { } u)
            return _move;
        return Toward(Hands01, u, HandsPerStep * (Held ? 0.5f : 1f));
    }

    /// <summary>
    /// While the link's open the hands stay put, and composure is steered: A/D push it at the full
    /// pace; the mouse points where it should be (across the composure gauge, which the game marks as
    /// the pointer's while drawing) and steers it there, gentler as it closes in, so it settles on the
    /// spot instead of swinging past it.
    /// </summary>
    private float Lean()
    {
        if (_move != 0 || PointU is not { } u)
            return _move;
        var off = u - Link.Composure;
        return MathF.Abs(off) < LeanDeadZone ? 0f : Math.Clamp(off / LeanFull, -1f, 1f) * MouseLean;
    }

    /// <summary>
    /// The mouse's steer while drawing: none this near where it points, full (times <see cref="MouseLean"/>)
    /// this far off, in between in proportion.
    /// </summary>
    public const float LeanDeadZone = 0.01f, LeanFull = 0.15f, MouseLean = 0.85f;

    protected override void Step()
    {
        if (!Held)
            _mustRelease = false;
        Under = UnderHands();
        var under = Under >= 0 ? Spots[Under] : null;

        // The link opens on a wound read, pressed on; it rests the moment the press does.
        Linked = Held && !_mustRelease && under != null && Drawable(under);
        var move = Linked ? 0 : Going();
        var lean = Linked ? Lean() : 0f;
        _going = Linked ? Math.Sign(lean) : move;
        if (Linked && under!.Key != _linkKey)
        {
            Link.Take(under.Pattern, under.Rate, under.Strength, under.Gravity, Rand);
            _linkKey = under.Key;
        }
        var calm = Link.Step(Rand, Linked, lean, under?.Drawn ?? 0f, Hands.Tremor, StepsDone);
        if (Link.Snapped)
        {
            Done = true;
            return;
        }
        if (Linked)
        {
            under!.Drawn = Math.Min(1f, under.Drawn + (calm ? 1f : 0.3f) / DrawLink.StepsFor(under.Gravity));
            Pulse = PulseShape(under.Pattern, StepsDone, under.Drawn) * (0.35f + 0.65f * under.Gravity);
            Feel = Math.Max(Feel * 0.9f, Pulse);
            if (StepsDone % 10 == 0)
                Sample(calm ? 1f : 0.2f);
            if (under.Drawn >= 1f)
            {
                (LastDrawn, LastDrawnAt) = (Under, StepsDone);
                _mustRelease = true;
                Linked = false;
            }
            _jump = 0;
            Done = Spots.All(s => s.Drawn >= 1f);
            return;
        }

        // Unlinked, the hands are free: jump to the next warmth, or run along the body.
        if (_jump != 0)
        {
            var way = _jump;
            _jump = 0;
            var next = Spots.Where(s => s.Sensed > 0f && s.Drawn < 1f && (s.At - Hands01) * way > 0.01f)
                .OrderBy(s => MathF.Abs(s.At - Hands01)).FirstOrDefault();
            if (next != null)
                Hands01 = next.At;
        }
        Hands01 = Math.Clamp(Hands01 + move * HandsPerStep * (Held ? 0.5f : 1f), 0f, 1f);

        var feel = 0f;
        var rough = false;
        foreach (var s in Spots)
        {
            if (s.Drawn >= 1f)
                continue;
            var d = MathF.Abs(s.At - Hands01);
            if (d < FeelRange)
                feel = Math.Max(feel, (1f - d / FeelRange) * (0.35f + 0.65f * s.Gravity));
            if (d >= FindRange * Hands.Tolerance)
                continue;
            s.Sensed = Math.Min(1f, s.Sensed + 1f / SenseSteps);
            if (Held && move != 0)
            {
                // Pressing while the hands slide over it: rough on the hurt, the graver the more.
                Rough += (0.4f + s.Gravity) / RoughSteps;
                rough = true;
            }
        }
        if (!rough)
            Rough = Math.Max(0f, Rough - 1f / 60);
        if (Rough >= 1f)
        {
            Flinch(move);
            return;
        }

        Pulse = 0f;
        Under = UnderHands();
        if (Held && move == 0 && !_mustRelease && Under >= 0)
        {
            // A still press reads it: what it is comes through the hand, then (dressed) what's under the dressing.
            var s = Spots[Under];
            if (s.Found < 1f)
                s.Found = Math.Min(1f, s.Found + 1f / ReadSteps);
            else if (s.Hidden && s.Deep < 1f)
                s.Deep = Math.Min(1f, s.Deep + 1f / DeepSteps);
            Pulse = PulseShape(s.Pattern, StepsDone, s.Found) * (0.35f + 0.65f * s.Gravity) * (0.4f + 0.6f * s.Found);
            feel = Math.Max(feel, Pulse);
            if (StepsDone % 10 == 0)
                Sample(1f);
        }
        // A trembling hand muddies it a little.
        Feel = Math.Clamp(feel + (Rand.NextFloat() - 0.5f) * Hands.Tremor * 0.3f, 0f, 1f);
        Done = Spots.Count > 0 && Spots.All(s => s.Drawn >= 1f)
            || Hands01 >= 1f && !Held && !Spots.Exists(s => s.Sensed > 0f && !Read(s));
    }

    /// <summary>The patient flinches from a rough hand: the hands are thrown back, the link jarred, a half-read wound lost.</summary>
    private void Flinch(int move)
    {
        Flinches++;
        LastFlinch = StepsDone;
        Rough = 0f;
        Hands01 = Math.Clamp(Hands01 - move * FlinchThrow, 0f, 1f);
        Link.Jar(FlinchStrain, (Rand.NextFloat() < 0.5f ? -1 : 1) * 0.02f);
        foreach (var s in Spots)
            if (MathF.Abs(s.At - Hands01) < FeelRange)
            {
                if (s.Found < 1f)
                    s.Found = 0f;
                if (s.Deep < 1f)
                    s.Deep = 0f;
            }
        Pulse = 0f;
        Feel = 1f;
    }

    /// <summary>The nearest wound not yet drawn within reach of the hands, or -1.</summary>
    private int UnderHands()
    {
        var best = -1;
        var bestD = FindRange * Hands.Tolerance;
        for (var i = 0; i < Spots.Count; i++)
        {
            var d = MathF.Abs(Spots[i].At - Hands01);
            if (Spots[i].Drawn < 1f && d < bestD)
                (best, bestD) = (i, d);
        }
        return best;
    }

    /// <summary>Each wound drawn whole lands as it's drawn; each flinch is remembered. Sim-only.</summary>
    public override void Settle(Simulation sim)
    {
        var at = sim.LocationOf(Worker);
        // A wound read is known at once (the window lists it), not only when the hands come off.
        var learned = false;
        foreach (var s in Spots)
            if (s.Found >= 1f && Empathy.Find(sim, Patient, s.Key) is { } inj && (inj.FeltBy != Worker || FoundFestering(s) && !inj.FeltFestering))
                learned |= Know(sim, s);
        if (learned)
            sim.World.Get<Health>(Patient).Changed();
        foreach (var s in Spots)
            if (s.Drawn >= 1f && !s.Applied)
            {
                s.Applied = true;
                Know(sim, s);
                var drew = Empathy.Draw(sim, Worker, Patient, s.Key, 1f, snapped: false);
                sim.Messages.Tell(Worker, drew > 0 ? MessageKind.Good : MessageKind.Info, drew > 0
                    ? $"You draw {PatientName}'s wound into yourself. It's yours now."
                    : "You can't take more of that into yourself and live.", at);
                if (drew > 0 && sim.World.Has<Mood>(Patient))
                    MoodSystem.Remember(sim, Patient, sim.Defs.Thoughts.TendedMe, Worker);
            }
        for (; _flinchesFelt < Flinches; _flinchesFelt++)
            if (sim.World.Has<Mood>(Patient))
                MoodSystem.Remember(sim, Patient, sim.Defs.Thoughts.RoughHands, Worker);
    }

    public override void Finish(Simulation sim) => Lift(sim, true);

    public override void Interrupted(Simulation sim) => Lift(sim, false);

    /// <summary>The hands come off: whole draws land, then what was read stays read, then any part drawn comes across (with a backlash if it snapped).</summary>
    private void Lift(Simulation sim, bool whole)
    {
        if (!sim.World.IsAlive(Patient) || !sim.World.Has<Health>(Patient))
            return;
        Settle(sim);
        var read = 0;
        foreach (var s in Spots)
            if (s.Drawn < 1f && Know(sim, s))
                read++;
        sim.World.Get<Health>(Patient).Changed();
        var at = sim.LocationOf(Worker);
        foreach (var s in Spots)
        {
            if (s.Drawn <= 0f || s.Applied)
                continue;
            s.Applied = true;
            var snapped = Link.Snapped && s.Key == _linkKey;
            var drew = Empathy.Draw(sim, Worker, Patient, s.Key, s.Drawn, snapped);
            if (snapped)
                sim.Messages.Tell(Worker, MessageKind.Warning, drew > 0
                    ? $"Your focus breaks and the link snaps back at you. Part of {PatientName}'s wound came with it."
                    : "Your focus breaks and the link snaps back at you.", at);
            if (drew > 0 && sim.World.Has<Mood>(Patient))
                MoodSystem.Remember(sim, Patient, sim.Defs.Thoughts.TendedMe, Worker);
        }
        if (whole && !Link.Snapped && Spots.All(s => s.Drawn <= 0f))
            sim.Messages.Tell(Worker, MessageKind.Info, read == 0
                ? $"You run your hands over {PatientName} and feel nothing that needs you."
                : $"You run your hands over {PatientName}: {read} {(read == 1 ? "hurt" : "hurts")} under them.", at);
    }

    /// <summary>A wound read stays known to these hands (<see cref="Injury.FeltBy"/>). True if it was read.</summary>
    private bool Know(Simulation sim, Spot s)
    {
        if (s.Found < 1f || Empathy.Find(sim, Patient, s.Key) is not { } inj)
            return false;
        inj.FeltBy = Worker;
        inj.FeltFestering |= FoundFestering(s);
        return true;
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Patient);
        w.String(PatientName);
        w.Int(Spots.Count);
        foreach (var s in Spots)
        {
            w.Int(s.Key);
            w.Float(s.At);
            w.Float(s.Gravity);
            w.Int((int)s.Pattern);
            w.Float(s.Rate);
            w.Float(s.Strength);
            w.Bool(s.Hidden);
            w.Float(s.Sensed);
            w.Float(s.Found);
            w.Float(s.Deep);
            w.Float(s.Drawn);
            w.Bool(s.Applied);
        }
        w.Int(Parts.Count);
        foreach (var p in Parts)
            w.String(p);
        Link.Write(w);
        w.Float(Hands01);
        w.Float(Feel);
        w.Float(Pulse);
        w.Int(Under);
        w.Bool(Linked);
        w.Float(Rough);
        w.Int(Flinches);
        w.Int(LastFlinch);
        w.Int(LastDrawn);
        w.Int(LastDrawnAt);
        w.Int(_move);
        w.Int(_jump);
        w.Int(_flinchesFelt);
        w.Bool(_mustRelease);
        w.Int(_linkKey);
    }

    protected override void ReadState(SaveReader r)
    {
        Patient = r.Entity();
        PatientName = r.String();
        var n = r.Count(256);
        for (var i = 0; i < n; i++)
        {
            var s = new Spot { Key = r.Int(), At = r.Float(), Gravity = r.Float(), Pattern = (DrawPattern)r.Int() };
            if (!Enum.IsDefined(s.Pattern))
                throw new SaveException("Corrupt save: a wound's pattern is unknown");
            (s.Rate, s.Strength, s.Hidden) = (r.Float(), r.Float(), r.Bool());
            (s.Sensed, s.Found, s.Deep, s.Drawn, s.Applied) = (r.Float(), r.Float(), r.Float(), r.Float(), r.Bool());
            Spots.Add(s);
        }
        var parts = r.Count(64);
        for (var i = 0; i < parts; i++)
            Parts.Add(r.String());
        Link = DrawLink.Read(r);
        Hands01 = r.Float();
        Feel = r.Float();
        Pulse = r.Float();
        Under = Math.Clamp(r.Int(), -1, Spots.Count - 1);
        Linked = r.Bool();
        Rough = r.Float();
        Flinches = r.Int();
        LastFlinch = r.Int();
        LastDrawn = Math.Clamp(r.Int(), -1, Spots.Count - 1);
        LastDrawnAt = r.Int();
        _move = Math.Sign(r.Int());
        _jump = Math.Sign(r.Int());
        _flinchesFelt = r.Int();
        _mustRelease = r.Bool();
        _linkKey = r.Int();
    }
}

/// <summary>
/// Held still under an empath's hands: a patient the AI runs stays where they are while someone is
/// feeling them over or drawing their wounds, then gets on with their day. (A player's character is
/// theirs to keep still.)
/// </summary>
public sealed class HoldStillJob(Entity healer) : Ai.Job
{
    /// <summary>The longest anyone waits, should the healer leave them hanging.</summary>
    public const int MaxTicks = SimClock.TicksPerSecond * 300;

    protected override IEnumerable<Ai.JobStatus> Run()
    {
        Report = $"Being healed by {Get<Creature>(healer).Name}";
        for (var t = 0; t < MaxTicks && Drawing(); t++)
            yield return Ai.JobStatus.Running;
    }

    private bool Drawing() =>
        Alive(healer) && Sim.World.TryGet<WorkSession>(healer, out var s) && Empathy.HandsOn(s, Pawn);
}
