using HeroesNotIncluded.Sim.Saving;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// A tool carried along the work to strike where it is: the hammer over the bar, the cleaver over
/// the side. A/D carry it, or it follows where the mouse points at the hand's own pace; a click
/// there strikes once it's arrived. Struck while it's still moving, or just after, the blow goes
/// wider: a smith who lets the hand settle lands true, one who hurries doesn't.
/// </summary>
public struct Aim
{
    /// <summary>How far the hand carries the tool a step: across the whole work in about a second and a half.</summary>
    public const float PerStep = 0.011f;

    /// <summary>Steps after it last moved before the hand's settled.</summary>
    public const int SettleSteps = 18;

    /// <summary>How much wider a blow goes struck on the move, against a settled hand.</summary>
    public const float Hurry = 2.5f;

    /// <summary>Where the tool is over the work, 0..1.</summary>
    public float At;

    /// <summary>The step it last moved on.</summary>
    public int MovedAt;

    /// <summary>A click made before it got there: strike when it does.</summary>
    public bool Queued;

    public Aim(float at) => (At, MovedAt, Queued) = (at, int.MinValue / 2, false);

    /// <summary>
    /// Carry it a step: <paramref name="dir"/> (A/D) if held, else toward <paramref name="target"/>
    /// (the mouse). True when a queued blow should land now, it having come to rest.
    /// </summary>
    public bool Step(int dir, float? target, int step)
    {
        var d = dir != 0 ? dir : WorkSession.Toward(At, target, PerStep);
        if (d != 0)
        {
            At = Math.Clamp(At + d * PerStep, 0f, 1f);
            MovedAt = step;
            return false;
        }
        if (!Queued)
            return false;
        Queued = false;
        return true;
    }

    /// <summary>How settled the hand is to strike: 1 at rest, up to <see cref="Hurry"/> on the move.</summary>
    public readonly float Unsteadiness(int step)
    {
        var since = step - MovedAt;
        return since >= SettleSteps ? 1f : 1f + (Hurry - 1f) * (1f - since / (float)SettleSteps);
    }

    /// <summary>Settled enough that the hand's at rest (for the drawing and the bots).</summary>
    public readonly bool Settled(int step) => step - MovedAt >= SettleSteps;

    public readonly void Write(SaveWriter w)
    {
        w.Float(At);
        w.Int(MovedAt);
        w.Bool(Queued);
    }

    public static Aim Read(SaveReader r) => new() { At = Math.Clamp(r.Float(), 0f, 1f), MovedAt = r.Int(), Queued = r.Bool() };
}
