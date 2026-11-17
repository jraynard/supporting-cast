using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tinkering;

/// <summary>
/// Feeling along a strongbox's lid for a trap. A/D run a probe along the seam from the hasp to
/// the hinge; the feel needle twitches as it goes, and catches hard near a trigger. Sweep light:
/// dragging the probe across a trigger with any weight on it sets the trap off. Stopped,
/// the fingers take a read of the spot (<see cref="SpotRead"/>, how hard it catches there, settling
/// while the probe's still): the better the read, the wider the green. Hold F to press in (the
/// pressure rises while held, falls when let go) and flick the spring out with Space while it's in
/// the green: out it comes, and that's the search done. Flicked outside it, or pressed past the
/// green, the trap fires; flicked where there's no trigger, nothing gives. Space with F let go runs on to the hinge. Reaching the hinge ends the
/// search, whatever was or wasn't found, so the session never says whether a trap was there.
/// </summary>
public sealed class ProbeSession : WorkSession
{
    /// <summary>How far the probe moves along the seam per step held.</summary>
    public const float ProbePerStep = 0.0035f;

    /// <summary>How near (seam fraction) a trigger is felt, and how near it must be to work it.</summary>
    public const float CatchRange = 0.09f, TriggerRange = 0.03f;

    /// <summary>Pressure gained per step held and lost per step let go.</summary>
    public const float PressPerStep = 0.007f, EasePerStep = 0.016f;

    /// <summary>The middle of the pressure that flicks a spring out, and what's too much.</summary>
    public const float BandMiddle = 0.575f, SpringAbove = 0.88f;

    /// <summary>
    /// Half the green's width, with no read of the spot and with a sure one (before skill widens it).
    /// At level 0 an unread green is still about 10 steps of pressing wide (the precision games' floor,
    /// HISTORY "Precision games"), a sure one nearly half a second.
    /// </summary>
    public const float BandUnsure = 0.045f, BandSure = 0.13f;

    /// <summary>
    /// A flick this many steps after the needle rose out of the green still counts: the eye and the
    /// screen lag the hand, so a press seen in the green lands a little past it.
    /// </summary>
    public const int LateSteps = 4;

    /// <summary>How fast the read settles toward what the probe feels, per still step.</summary>
    public const float ReadPerStep = 0.04f;

    /// <summary>More weight than this, moving across a trigger, sets it off.</summary>
    public const float LightTouch = 0.25f;

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

    /// <summary>
    /// The fingers' read of the spot the probe rests on, 0..1: how hard it catches there, settling
    /// while it's held still, gone when it moves. It's only what the feel says, steadied.
    /// </summary>
    public float SpotRead { get; private set; }

    /// <summary>Times Space was pressed in, F held, where nothing gave: no spring there to flick.</summary>
    public int NothingGave { get; private set; }

    public bool Disarmed { get; private set; }
    public bool Sprung { get; private set; }

    private int _move;

    /// <summary>Which way the probe's being run (-1 toward the hasp, 1 toward the hinge), 0 if it's held still.</summary>
    public int Moving => _going;

    /// <summary>Pressing harder than a sweep can take: running the probe now would set off a trigger it crossed.</summary>
    public bool TooHeavyToSweep => Pressure > LightTouch;

    /// <summary>Half the green's width: the read of the spot, widened by skill.</summary>
    public float HalfBand => (BandUnsure + (BandSure - BandUnsure) * SpotRead) * Hands.Tolerance;

    public float Low => Math.Max(LightTouch + 0.05f, BandMiddle - HalfBand);
    public float High => Math.Min(SpringAbove - 0.03f, BandMiddle + HalfBand);
    public bool InBand => Pressure >= Low && Pressure <= High;

    public override Skill Skill => Skill.Crafting;
    public override float Xp => Disarmed ? 60f : 20f;
    public override string Title => "Feeling the strongbox over for traps";

    public static void TryStart(Simulation sim, Entity worker, Entity box)
    {
        if (Lockwork.Problem(sim, worker, box) is { } why)
        {
            sim.Messages.Tell(worker, MessageKind.Info, $"Can't search that: {why}.", sim.LocationOf(worker));
            return;
        }
        var b = sim.World.Get<Strongbox>(box);
        WorkSystem.Start(sim, worker, new ProbeSession { Box = box, Trap = b.Trap, TrapAt = b.TrapAt, Armed = b.Armed });
    }

    public override bool StillValid(Simulation sim) =>
        Lockwork.IsLocked(sim, Box) && Inventory.OnPerson(sim, Worker, Box) && !Lockwork.Picks(sim, Worker).IsNull;

    protected override void Axis(int value) => _move = value;

    /// <summary>
    /// The other action (Space, the right button): pressing in (F held), flick the spring out;
    /// otherwise run on to the hinge and finish the search.
    /// </summary>
    protected override void Act()
    {
        if (Held)
            _flick = true;
        else
            _onToEnd = true;
    }

    // Run on to the end, or flick the spring out at the next step (Act); and how the probe went last step, keys or mouse.
    private bool _onToEnd, _flick;
    private int _going;

    // Steps since the pressure was last in the green (for a late flick).
    private int _sinceGreen = int.MaxValue / 2;

    /// <summary>
    /// How the probe goes this step: A/D, else toward where the mouse points along the seam at the
    /// probe's own pace, so it passes over what's there as a sweep does. The mouse alone stops just
    /// short of the hinge: finishing is a choice (<see cref="Act"/>), not a cursor at the edge. While
    /// pressing in (F held), the mouse leaves the probe where it is: only the keys can drag weight
    /// across the seam, and they mean to.
    /// </summary>
    private int Going() =>
        _move != 0 ? _move : _onToEnd ? 1 : Held ? 0 : Toward(Probe, PointU is { } u ? Math.Min(u, MouseReach) : null, ProbePerStep);

    /// <summary>The furthest along that pointing takes a tool that finishes at its end.</summary>
    public const float MouseReach = 0.97f;

    private float Near => Armed ? MathF.Abs(Probe - TrapAt) : 1f;

    /// <summary>Would a flick now land in the green: in it, or only just risen past it (<see cref="LateSteps"/>)?</summary>
    public bool FlickLands => InBand || (Pressure > High && _sinceGreen <= LateSteps);

    protected override void Step()
    {
        var move = _going = Going();
        var overTrigger = Armed && Near <= TriggerRange;

        // A flick is judged on the pressure as it was when pressed, before this step presses further.
        var flick = _flick;
        _flick = false;
        if (flick && overTrigger)
        {
            if (!FlickLands)
            {
                Spring();
                return;
            }
            // Out cleanly: the nearer the middle of the green, the cleaner. That's the search done: the
            // spring's in your fingers, and a box has the one trap.
            Disarmed = true;
            Armed = false;
            Sample(Math.Clamp(1f - MathF.Abs(Pressure - BandMiddle) / Math.Max(0.01f, HalfBand) * 0.4f, 0.5f, 1f));
            Done = true;
            return;
        }
        else if (flick)
            NothingGave++;

        var tremor = (Rand.NextFloat() * 2f - 1f) * 0.004f * (1f + Hands.Tremor * 4f);
        Pressure = Math.Clamp(Pressure + (Held ? PressPerStep : -EasePerStep) + (Pressure > 0.05f ? tremor : 0f), 0f, 1f);
        _sinceGreen = InBand ? 0 : Math.Min(_sinceGreen + 1, int.MaxValue / 2);
        var catching = Armed ? Math.Max(0f, 1f - Near / CatchRange) : 0f;
        Feel = Math.Clamp(catching * 0.85f + Rand.NextFloat() * 0.22f * (1f + Hands.Tremor), 0f, 1f);
        SpotRead = move == 0 ? SpotRead + (Feel - SpotRead) * ReadPerStep : 0f;

        if (overTrigger && (Pressure > SpringAbove || (move != 0 && Pressure > LightTouch)))
        {
            Spring();
            return;
        }
        Probe = Math.Clamp(Probe + move * ProbePerStep, 0f, 1f);
        if (Probe >= 1f)
        {
            if (!Disarmed)
                Sample(0.5f);
            Done = true;
        }
    }

    private void Spring()
    {
        Sprung = true;
        Armed = false;
        Sample(0f);
        Done = true;
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
            sim.Messages.Add(MessageKind.Good, $"Snick: {w.Get<Creature>(Worker).Name} flicks {Lockwork.TrapLabel(Trap)} out of the strongbox's lid. It's safe to pick.", at);
        }
        else
            sim.Messages.Tell(Worker, MessageKind.Info, "You've run the probe along the whole seam.", at);
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
        w.Float(SpotRead);
        w.Bool(Disarmed);
        w.Bool(Sprung);
        w.Int(_move);
        w.Bool(_onToEnd);
        w.Int(_going);
        w.Bool(_flick);
        w.Int(NothingGave);
        w.Int(_sinceGreen);
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
        SpotRead = r.Float(); // before 51, how far the spring was eased out: near enough a read of the spot
        Disarmed = r.Bool();
        Sprung = r.Bool();
        _move = Math.Sign(r.Int());
        if (r.Version >= 43)
        {
            _onToEnd = r.Bool();
            _going = Math.Sign(r.Int());
        }
        if (r.Version >= 51)
        {
            _flick = r.Bool();
            NothingGave = r.Int();
            _sinceGreen = r.Int();
        }
    }
}
