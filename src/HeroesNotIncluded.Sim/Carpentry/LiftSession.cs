using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Carpentry;

/// <summary>
/// The lifter at a party's wagon (<see cref="Pairs"/>): taking its weight while the carpenter drives
/// a new axle's hubs on (<see cref="JoinSession"/>, <see cref="JoinWork.Axle"/>). Hold F to keep it up;
/// it sways as it hangs, and A/D lean against the sway to hold it level. Each blow on the hub is the
/// call: level and up, it goes home; dropped, or tilted past the band, the hub's jolted back off its
/// seat. The arms tire while it's held and rest while it's down, and held too long they give, and it
/// drops till they've rested. Steadier hands widen the band.
/// </summary>
public sealed class LiftSession : HelperSession
{
    /// <summary>Within this of level (before skill widens it) a blow goes home.</summary>
    public const float Band = 0.25f;

    /// <summary>How fast the sway gathers a step, how hard a lean pushes it back, and how it's damped.</summary>
    public const float SwayPerStep = 0.0035f, LeanPerStep = 0.0045f, Damping = 0.97f;

    /// <summary>How fast the arms tire a step holding it up, rest a step with it down, and the rest they need to lift again once they've given.</summary>
    public const float TirePerStep = 0.0018f, RestPerStep = 0.006f, LiftAgainBelow = 0.5f;

    /// <summary>Steps a word on the last blow stays up (the panel reads it).</summary>
    public const int WordSteps = 60;

    public Entity Wagon { get; set; } = Entity.Null;

    /// <summary>Up off the ground now.</summary>
    public bool Up { get; private set; }

    /// <summary>How far it leans off level, −1..1, and how fast it's going.</summary>
    public float Tilt { get; private set; }
    public float Sway { get; private set; }

    /// <summary>How tired the arms are, 0..1; at 1 they give.</summary>
    public float Strain { get; private set; }

    /// <summary>The arms gave: it's down till they've rested.</summary>
    public bool Spent { get; private set; }

    public int Holds { get; private set; }
    public int Jolts { get; private set; }

    /// <summary>Whether the last blow found it level, and when (-1 before any).</summary>
    public bool LastLevel { get; private set; }
    public int LastStep { get; private set; } = -1;

    private int _lean;

    public float BandWidth => Band * Hands.Tolerance;
    public bool Level => Up && MathF.Abs(Tilt) <= BandWidth;

    public override Skill Skill => Skill.Construction;
    public override float Xp => Math.Min(30f, 3f * Holds);
    public override string Title => $"Taking the wagon's weight for {LeadName}";

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Wagon) && sim.World.TryGet<Thing>(Wagon, out var t) ? t.Cell : null;

    public override bool Follows(WorkSession lead) => lead is JoinSession { Work: JoinWork.Axle } j && j.Wagon == Wagon;

    protected override bool LeadAtIt(Simulation sim) =>
        sim.World.TryGet<WorkSession>(Partner, out var s) && s is JoinSession { Work: JoinWork.Axle } j && j.Wagon == Wagon;

    protected override bool InPlace(Simulation sim) =>
        sim.World.IsAlive(Wagon) && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Wagon);

    /// <summary>A/D lean against the sway.</summary>
    protected override void Axis(int value) => _lean = value;

    /// <summary>A blow on the hub: answered at once with how the wagon's held.</summary>
    protected override void Received(Beat beat)
    {
        if (beat.Kind != BeatKind.Call)
            return;
        LastStep = StepsDone;
        if (Level)
        {
            var level = 1f - 0.5f * MathF.Abs(Tilt) / Math.Max(0.01f, BandWidth);
            Holds++;
            LastLevel = true;
            Sample(level);
            Send(BeatKind.Strike, level);
        }
        else
        {
            Jolts++;
            LastLevel = false;
            Sample(0f);
            Send(BeatKind.Offbeat, 1f);
        }
    }

    protected override void Step()
    {
        if (Spent && Strain <= LiftAgainBelow)
            Spent = false;
        Up = Held && !Spent;
        if (!Up)
        {
            // On the ground it sits level, and the arms rest.
            Strain = Math.Max(0f, Strain - RestPerStep);
            Tilt *= 0.9f;
            Sway = 0f;
            return;
        }
        // It sways as it hangs, more as the arms tire and the hands shake; a lean pushes it back.
        Sway = Sway * Damping + (Rand.NextFloat() * 2f - 1f) * SwayPerStep * (1f + Strain + Hands.Tremor * 3f) - _lean * LeanPerStep;
        Tilt = Math.Clamp(Tilt + Sway, -1f, 1f);
        if (MathF.Abs(Tilt) >= 1f)
            Sway = 0f;
        Strain = Math.Min(1f, Strain + TirePerStep);
        if (Strain >= 1f)
            (Spent, Up) = (true, false);
    }

    protected override void WriteState(SaveWriter w)
    {
        base.WriteState(w);
        w.Entity(Wagon);
        w.Bool(Up);
        w.Float(Tilt);
        w.Float(Sway);
        w.Float(Strain);
        w.Bool(Spent);
        w.Int(Holds);
        w.Int(Jolts);
        w.Bool(LastLevel);
        w.Int(LastStep);
        w.Int(_lean);
    }

    protected override void ReadState(SaveReader r)
    {
        base.ReadState(r);
        Wagon = r.Entity();
        Up = r.Bool();
        Tilt = Math.Clamp(r.Float(), -1f, 1f);
        Sway = Math.Clamp(r.Float(), -1f, 1f);
        Strain = Math.Clamp(r.Float(), 0f, 1f);
        Spent = r.Bool();
        Holds = r.Int();
        Jolts = r.Int();
        LastLevel = r.Bool();
        LastStep = r.Int();
        _lean = Math.Sign(r.Int());
    }
}
