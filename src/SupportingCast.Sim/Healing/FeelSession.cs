using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Systems;
using SupportingCast.Sim.Work;

namespace SupportingCast.Sim.Healing;

/// <summary>
/// Feeling a patient over: the touch that's an empath's diagnosis (docs/DESIGN.md, "Healer").
/// A/D run the hands along the body from the head to the feet. Near a wound the feel rises, and
/// lingering there makes it out: then it's known to this empath, by its feel
/// (<see cref="Injury.FeltBy"/>), and can be drawn. Holding F presses in, slower: only a pressed
/// pass finds festering under a dressing. Reaching the feet ends it, as does Esc; either way what
/// was found stays found.
/// </summary>
public sealed class FeelSession : WorkSession
{
    /// <summary>How far the hands move along the body per step (half that pressing in).</summary>
    public const float HandsPerStep = 0.004f;

    /// <summary>How near (body fraction) a wound is felt, and how near to make it out.</summary>
    public const float FeelRange = 0.12f, FindRange = 0.05f;

    /// <summary>
    /// Steps lingering near a wound to make it out pressing in (a third more skimming); festering
    /// under a dressing takes twice as long, pressing in. A skim at full speed just finds a wound;
    /// a pressed pass, at half speed, finds the festering too.
    /// </summary>
    public const int FindSteps = 16;

    /// <summary>A wound under the hands, as the session's own copy (so the client can run it ahead).</summary>
    public sealed class Spot
    {
        public int Key;
        public float At;
        public float Gravity;

        /// <summary>Festering under a dressing: only a pressed pass finds it.</summary>
        public bool Hidden;

        public float Found, Deep;
    }

    public Entity Patient { get; set; } = Entity.Null;
    public string PatientName { get; set; } = "";
    public List<Spot> Spots { get; set; } = [];

    /// <summary>The body's parts, head to feet, for the scene's labels.</summary>
    public List<string> Parts { get; set; } = [];

    /// <summary>Where the hands are, 0 (the head) to 1 (the feet).</summary>
    public float Hands01 { get; private set; }

    /// <summary>What the hands feel now, 0..1: rising near a wound, the worse the stronger.</summary>
    public float Feel { get; private set; }

    private int _move;

    public override Skill Skill => Skill.Medicine;
    public override float Xp => 10f;
    public override string Title => $"Feeling {PatientName} over";

    public bool Found(Spot s) => s.Found >= 1f;
    public bool FoundFestering(Spot s) => s.Hidden && s.Deep >= 1f;

    /// <summary>Where a part lies along the body, head to feet: its place in the body's list.</summary>
    public static float PartAt(int part, int parts) => (part + 0.5f) / Math.Max(1, parts);

    public static string? Problem(Simulation sim, Entity healer, Entity patient)
    {
        var w = sim.World;
        if (!Empathy.Gifted(sim, healer))
            return "you haven't the gift";
        if (healer == patient)
            return "you already know your own hurts";
        if (!w.IsAlive(patient) || !w.TryGet<Creature>(patient, out var c) || c.Dead || !w.Has<Health>(patient))
            return "there's no one to feel";
        if (!Pathfinder.Touches(w.Get<Position>(healer).Cell, w.Get<Position>(patient).Cell))
            return "you need to be able to touch them";
        return null;
    }

    public static void TryStart(Simulation sim, Entity healer, Entity patient)
    {
        if (Problem(sim, healer, patient) is { } why)
        {
            sim.Messages.Add(MessageKind.Info, $"Can't lay on hands: {why}.", sim.LocationOf(healer));
            return;
        }
        var h = sim.World.Get<Health>(patient);
        var n = h.Body.Parts.Count;
        var spots = new List<Spot>();
        foreach (var inj in h.Injuries)
        {
            // Wounds on one part lie side by side along it.
            var onPart = spots.Count(s => MathF.Abs(s.At - PartAt(inj.Part, n)) < 0.5f / n);
            spots.Add(new Spot
            {
                Key = Empathy.KeyOf(inj), At = PartAt(inj.Part, n) + (onPart % 2 == 0 ? 1 : -1) * 0.02f * ((onPart + 1) / 2),
                Gravity = Math.Clamp(inj.Damage / Math.Max(1f, h.PartMaxHp(inj.Part)), 0f, 1f),
                Hidden = inj.Infection != null && inj.Tended,
            });
        }
        WorkSystem.Start(sim, healer, new FeelSession
        {
            Patient = patient, PatientName = sim.World.Get<Creature>(patient).Name, Spots = spots,
            Parts = h.Body.Parts.Select(p => p.Label).ToList(),
        });
        var w = sim.World;
        if (!w.Has<Avatar>(patient) && w.Has<Mind>(patient) && w.Get<Creature>(patient).Able)
            AiSystem.Order(sim, patient, new HoldStillJob(healer));
    }

    public override bool StillValid(Simulation sim) => Problem(sim, Worker, Patient) == null;

    protected override void Cloned() => Spots = Spots.Select(s => new Spot { Key = s.Key, At = s.At, Gravity = s.Gravity, Hidden = s.Hidden, Found = s.Found, Deep = s.Deep }).ToList();

    protected override void Axis(int value) => _move = value;

    protected override void Step()
    {
        Hands01 = Math.Clamp(Hands01 + _move * HandsPerStep * (Held ? 0.5f : 1f), 0f, 1f);
        var feel = 0f;
        foreach (var s in Spots)
        {
            var d = MathF.Abs(s.At - Hands01);
            if (d < FeelRange)
                feel = Math.Max(feel, (1f - d / FeelRange) * (0.35f + 0.65f * s.Gravity));
            if (d >= FindRange * Hands.Tolerance)
                continue;
            s.Found = Math.Min(1f, s.Found + (Held ? 1f : 0.75f) / FindSteps);
            if (Held && s.Hidden)
                s.Deep = Math.Min(1f, s.Deep + 1f / (FindSteps * 2));
        }
        // A trembling hand muddies it a little.
        Feel = Math.Clamp(feel + (Rand.NextFloat() - 0.5f) * Hands.Tremor * 0.3f, 0f, 1f);
        if (StepsDone % 10 == 0)
            Sample(1f);
        Done = Hands01 >= 1f;
    }

    public override void Finish(Simulation sim) => Keep(sim, true);

    public override void Interrupted(Simulation sim) => Keep(sim, false);

    /// <summary>What was found stays found, by this empath.</summary>
    private void Keep(Simulation sim, bool whole)
    {
        if (!sim.World.IsAlive(Patient) || !sim.World.TryGet<Health>(Patient, out var h))
            return;
        var found = 0;
        foreach (var s in Spots)
        {
            if (!Found(s) || Empathy.Find(sim, Patient, s.Key) is not { } inj)
                continue;
            inj.FeltBy = Worker;
            inj.FeltFestering |= FoundFestering(s);
            found++;
        }
        if (found > 0)
            h.Changed();
        var at = sim.LocationOf(Worker);
        if (whole)
            sim.Messages.Add(MessageKind.Info, found == 0
                ? $"You run your hands over {PatientName} and feel nothing that needs you."
                : $"You run your hands over {PatientName}: {found} {(found == 1 ? "hurt" : "hurts")} under them.", at);
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
            w.Bool(s.Hidden);
            w.Float(s.Found);
            w.Float(s.Deep);
        }
        w.Int(Parts.Count);
        foreach (var p in Parts)
            w.String(p);
        w.Float(Hands01);
        w.Float(Feel);
        w.Int(_move);
    }

    protected override void ReadState(SaveReader r)
    {
        Patient = r.Entity();
        PatientName = r.String();
        var n = r.Count(256);
        for (var i = 0; i < n; i++)
            Spots.Add(new Spot { Key = r.Int(), At = r.Float(), Gravity = r.Float(), Hidden = r.Bool(), Found = r.Float(), Deep = r.Float() });
        var parts = r.Count(64);
        for (var i = 0; i < parts; i++)
            Parts.Add(r.String());
        Hands01 = r.Float();
        Feel = r.Float();
        _move = Math.Sign(r.Int());
    }
}
