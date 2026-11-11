using SupportingCast.Sim.Components;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Work;

namespace SupportingCast.Sim.Healing;

/// <summary>
/// Meditating mana back: breathe with the rhythm, pressing F at the top of each breath in and the
/// bottom of each breath out. Each breath caught builds calm, and calm draws mana faster. The pain
/// you carry (taken when you sat down) makes the breaths uneven and brings flinches: a jolt you
/// mustn't answer, since pressing on one breaks your calm. A healer who couldn't mend themselves
/// fully gets little from it.
/// </summary>
public sealed class MeditateSession : WorkSession
{
    /// <summary>Steps of half a breath when untroubled.</summary>
    public const int HalfBreath = 120;

    /// <summary>Steps either side of the turn of a breath that count, before skill widens it.</summary>
    public const int Window = 14;

    /// <summary>Breaths a sitting lasts.</summary>
    public const int Breaths = 12;

    /// <summary>Mana per breath caught, as a share of the most they hold, before calm adds to it.</summary>
    public const float GainPerBreath = 0.05f;

    /// <summary>Steps a flinch shows for (pressing in it breaks calm).</summary>
    public const int FlinchSteps = 25;

    /// <summary>The pain carried when the sitting began, 0..1.</summary>
    public float Pain { get; set; }

    /// <summary>
    /// How much a meditation stone in reach helps (<see cref="Defs.BuildingProps.Meditation"/>),
    /// 0 for none: pain unsettles the breath that much less, and each breath gathers that much more.
    /// </summary>
    public float Stillness { get; set; }

    /// <summary>The pain that troubles the breath, after the stone's help.</summary>
    public float FeltPain => Pain * (1f - Stillness);

    public float MaxMana { get; set; }

    /// <summary>Room for mana when the sitting began.</summary>
    public float Room { get; set; }

    /// <summary>Breaths caught in a row.</summary>
    public int Calm { get; private set; }

    public float Gained { get; private set; }

    /// <summary>Turns of breath gone by (two per breath).</summary>
    public int Turns { get; private set; }

    /// <summary>Breathing in (rising) or out.</summary>
    public bool Inhaling { get; private set; } = true;

    /// <summary>The next turn of breath: the step it comes at, and where this half began.</summary>
    public int NextTurn { get; private set; }
    public int HalfStart { get; private set; }

    public int FlinchUntil { get; private set; } = -1;
    private bool _answered;

    public int WindowSteps => (int)(Window * Hands.Tolerance);

    /// <summary>The breath's fullness now, 0 (out) .. 1 (in), for drawing.</summary>
    public float Fullness
    {
        get
        {
            var t = Math.Clamp((StepsDone - HalfStart) / (float)Math.Max(1, NextTurn - HalfStart), 0f, 1f);
            return Inhaling ? t : 1f - t;
        }
    }

    public bool Flinching => StepsDone < FlinchUntil;

    public override Skill Skill => Skill.Medicine;
    public override float Xp => 15f;
    public override string Title => "Meditating";

    public static string? Problem(Simulation sim, Entity who)
    {
        if (!sim.World.TryGet<Mana>(who, out var mana))
            return "you haven't the gift";
        if (mana.Current >= mana.Max - 0.5f)
            return "your mana is full";
        return null;
    }

    public static void TryStart(Simulation sim, Entity who)
    {
        if (Problem(sim, who) is { } why)
        {
            sim.Messages.Add(MessageKind.Info, $"Can't meditate: {why}.", sim.LocationOf(who));
            return;
        }
        var mana = sim.World.Get<Mana>(who);
        var pain = sim.World.TryGet<Health>(who, out var h) ? Math.Clamp(h.Pain, 0f, 1f) : 0f;
        WorkSystem.Start(sim, who, new MeditateSession
        {
            Pain = pain, MaxMana = mana.Max, Room = mana.Max - mana.Current, Stillness = StillnessAt(sim, who),
        });
    }

    /// <summary>The help of the best meditation stone within reach (0 if none).</summary>
    public static float StillnessAt(Simulation sim, Entity who)
    {
        var at = sim.World.Get<Position>(who).Cell;
        var best = 0f;
        foreach (var e in sim.World.Each<Thing>())
        {
            var t = sim.World.Get<Thing>(e);
            if (t.Spawned && t.Def.Building is { Meditation: > 0 } b && !sim.World.Has<Construction>(e) && Systems.AvatarSystem.InReach(sim, at, e))
                best = Math.Max(best, Math.Clamp(b.Meditation, 0f, 0.9f));
        }
        return best;
    }

    public override bool StillValid(Simulation sim) => sim.World.Has<Mana>(Worker);

    protected internal override void Begin()
    {
        HalfStart = 0;
        NextTurn = HalfLength();
    }

    /// <summary>Pain makes a breath come short or long.</summary>
    private int HalfLength() => Math.Max(50, (int)(HalfBreath * (1f + (Rand.NextFloat() * 2f - 1f) * 0.55f * FeltPain)));

    protected override void Press()
    {
        if (Flinching)
        {
            Calm = 0; // answered the pain, not the breath
            return;
        }
        if (!_answered && Math.Abs(StepsDone - NextTurn) <= WindowSteps)
        {
            _answered = true;
            Calm++;
            var gain = MaxMana * GainPerBreath * (1f + 0.15f * Math.Min(Calm, 6)) * (1f + Stillness);
            Gained = Math.Min(Room, Gained + gain);
            Sample(1f);
        }
        else
        {
            Calm = 0;
            Sample(0f);
        }
    }

    protected override void Step()
    {
        if (FeltPain > 0 && !Flinching && Rand.Chance(0.006f * FeltPain))
            FlinchUntil = StepsDone + FlinchSteps;
        if (StepsDone <= NextTurn + WindowSteps)
            return;
        // The turn of breath has gone by: on to the next half.
        if (!_answered)
        {
            Calm = 0;
            Sample(0f);
        }
        _answered = false;
        Turns++;
        Inhaling = !Inhaling;
        HalfStart = NextTurn;
        NextTurn += HalfLength();
        Done = Turns >= Breaths * 2 || Gained >= Room - 0.01f;
    }

    public override void Finish(Simulation sim)
    {
        ref var mana = ref sim.World.Get<Mana>(Worker);
        mana.Current = Math.Min(mana.Max, mana.Current + Gained);
        sim.Messages.Add(Gained >= Room * 0.5f ? MessageKind.Good : MessageKind.Info, Pain > 0.4f && Gained < Room * 0.5f
            ? $"The pain keeps pulling you out of it. You come back with {Gained:F0} mana."
            : $"You come back to yourself with {Gained:F0} mana.", sim.LocationOf(Worker));
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Float(Pain);
        w.Float(Stillness);
        w.Float(MaxMana);
        w.Float(Room);
        w.Int(Calm);
        w.Float(Gained);
        w.Int(Turns);
        w.Bool(Inhaling);
        w.Int(NextTurn);
        w.Int(HalfStart);
        w.Int(FlinchUntil);
        w.Bool(_answered);
    }

    protected override void ReadState(SaveReader r)
    {
        Pain = r.Float();
        Stillness = r.Float();
        MaxMana = r.Float();
        Room = r.Float();
        Calm = r.Int();
        Gained = r.Float();
        Turns = r.Int();
        Inhaling = r.Bool();
        NextTurn = r.Int();
        HalfStart = r.Int();
        FlinchUntil = r.Int();
        _answered = r.Bool();
    }
}
