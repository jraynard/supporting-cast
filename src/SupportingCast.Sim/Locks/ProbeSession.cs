using SupportingCast.Sim.Components;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Systems;
using SupportingCast.Sim.Work;

namespace SupportingCast.Sim.Locks;

/// <summary>
/// Feeling along a strongbox's lid for a trap. A/D run a probe along the seam from the hasp to
/// the hinge; the feel needle twitches as it goes, and catches hard near a trigger. Sweep light:
/// dragging the probe across a trigger with any weight on it sets the trap off. Stopped over one,
/// hold F to press in and ease the spring out, keeping the pressure in the band (it rises while
/// held and falls when let go); press too hard and it fires. Reaching the hinge end ends the
/// search, whatever was or wasn't found, so the session never says whether a trap was there.
/// </summary>
public sealed class ProbeSession : WorkSession
{
    /// <summary>How far the probe moves along the seam per step held.</summary>
    public const float ProbePerStep = 0.0035f;

    /// <summary>How near (seam fraction) a trigger is felt, and how near it must be to work it.</summary>
    public const float CatchRange = 0.09f, TriggerRange = 0.03f;

    /// <summary>Pressure gained per step held and lost per step let go.</summary>
    public const float PressPerStep = 0.012f, EasePerStep = 0.016f;

    /// <summary>The pressure band that works a spring loose (before skill widens it), and what's too much.</summary>
    public const float BandLow = 0.45f, BandHigh = 0.7f, SpringAbove = 0.88f;

    /// <summary>More weight than this, moving across a trigger, sets it off.</summary>
    public const float LightTouch = 0.25f;

    /// <summary>Steps in the band, over the trigger, to draw the spring out.</summary>
    public const int StepsToDisarm = 210;

    public Entity Box { get; set; } = Entity.Null;

    // The trap as it was when the search began: the session's own copy, so the client can run it
    // ahead. Nothing drawn from it is shown but the feel needle.
    public TrapKind Trap { get; set; }
    public float TrapAt { get; set; }
    public bool Armed { get; set; }

    /// <summary>Where the probe is along the seam, 0 (the hasp) to 1 (the hinge).</summary>
    public float Probe { get; private set; }

    public float Pressure { get; private set; }

    /// <summary>What the probe tells the fingers, 0..1: a twitch, or a hard catch.</summary>
    public float Feel { get; private set; }

    /// <summary>How far the spring has been eased out, 0..1.</summary>
    public float Progress { get; private set; }

    public bool Disarmed { get; private set; }
    public bool Sprung { get; private set; }

    private int _move;

    public float Low => Math.Max(LightTouch + 0.05f, (BandLow + BandHigh) * 0.5f - (BandHigh - BandLow) * 0.5f * Hands.Tolerance);
    public float High => Math.Min(SpringAbove - 0.05f, (BandLow + BandHigh) * 0.5f + (BandHigh - BandLow) * 0.5f * Hands.Tolerance);
    public bool InBand => Pressure >= Low && Pressure <= High;

    public override Skill Skill => Skill.Crafting;
    public override float Xp => Disarmed ? 60f : 20f;
    public override string Title => "Feeling the strongbox over for traps";

    public static void TryStart(Simulation sim, Entity worker, Entity box)
    {
        if (Lockwork.Problem(sim, worker, box) is { } why)
        {
            sim.Messages.Add(MessageKind.Info, $"Can't search that: {why}.", sim.LocationOf(worker));
            return;
        }
        var b = sim.World.Get<Strongbox>(box);
        WorkSystem.Start(sim, worker, new ProbeSession { Box = box, Trap = b.Trap, TrapAt = b.TrapAt, Armed = b.Armed });
    }

    public override bool StillValid(Simulation sim) =>
        Lockwork.IsLocked(sim, Box) && Inventory.OnPerson(sim, Worker, Box) && !Lockwork.Picks(sim, Worker).IsNull;

    protected override void Axis(int value) => _move = value;

    private float Near => Armed ? MathF.Abs(Probe - TrapAt) : 1f;

    protected override void Step()
    {
        var tremor = (Rand.NextFloat() * 2f - 1f) * 0.004f * (1f + Hands.Tremor * 4f);
        Pressure = Math.Clamp(Pressure + (Held ? PressPerStep : -EasePerStep) + (Pressure > 0.05f ? tremor : 0f), 0f, 1f);
        var catching = Armed ? Math.Max(0f, 1f - Near / CatchRange) : 0f;
        Feel = Math.Clamp(catching * 0.85f + Rand.NextFloat() * 0.22f * (1f + Hands.Tremor), 0f, 1f);

        var overTrigger = Armed && Near <= TriggerRange;
        if (overTrigger && (Pressure > SpringAbove || (_move != 0 && Pressure > LightTouch)))
        {
            Sprung = true;
            Armed = false;
            Sample(0f);
            Done = true;
            return;
        }
        if (overTrigger && _move == 0 && Held && InBand)
        {
            Progress += 1f / StepsToDisarm;
            if (Progress >= 1f)
            {
                Disarmed = true;
                Armed = false;
                Sample(1f);
            }
        }
        Probe = Math.Clamp(Probe + _move * ProbePerStep, 0f, 1f);
        if (Probe >= 1f)
        {
            if (!Disarmed)
                Sample(0.5f);
            Done = true;
        }
    }

    /// <summary>What was found, if anything, becomes true of the box.</summary>
    public override void Finish(Simulation sim)
    {
        var w = sim.World;
        var at = sim.LocationOf(Worker);
        w.Get<Strongbox>(Box).SearchedBy = Worker;
        if (Sprung)
            Lockwork.Spring(sim, Worker, Box);
        else if (Disarmed)
        {
            w.Get<Strongbox>(Box).Armed = false;
            sim.Messages.Add(MessageKind.Good, $"{w.Get<Creature>(Worker).Name} eases {Lockwork.TrapLabel(Trap)} out of the strongbox's lid.", at);
        }
        else
            sim.Messages.Add(MessageKind.Info, "You've run the probe along the whole seam.", at);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Box);
        w.Byte((byte)Trap);
        w.Float(TrapAt);
        w.Bool(Armed);
        w.Float(Probe);
        w.Float(Pressure);
        w.Float(Feel);
        w.Float(Progress);
        w.Bool(Disarmed);
        w.Bool(Sprung);
        w.Int(_move);
    }

    protected override void ReadState(SaveReader r)
    {
        Box = r.Entity();
        var trap = r.Byte();
        if (!Enum.IsDefined((TrapKind)trap))
            throw new SaveException($"Corrupt save: trap {trap}");
        Trap = (TrapKind)trap;
        TrapAt = r.Float();
        Armed = r.Bool();
        Probe = r.Float();
        Pressure = r.Float();
        Feel = r.Float();
        Progress = r.Float();
        Disarmed = r.Bool();
        Sprung = r.Bool();
        _move = Math.Sign(r.Int());
    }
}
