using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tinkering;

/// <summary>
/// Picking a lock, one pin at a time: a strongbox's, or one a tinker fitted to a door
/// (<see cref="LockCut"/>, whose tightness narrows where a pin will set). One finger keeps tension
/// on the plug, eased up with W and down with S; it stays where it's left, give or take the hand's
/// unsteadiness and the plug pushing back now and then (<see cref="Pushing"/>), which wants
/// correcting. Too much tension freezes the pins: those set stay set, but the pin won't lift or
/// set. Coming back down from there is hitchy: the plug springs back toward slack, hard, while it's
/// too tight and for a moment after, and wants catching (W). Too little tension and every pin set
/// drops back. Meanwhile the pick lifts the pin (A/D): near the
/// height where it sets it goes stiff under the pick, and there, with the tension right, F sets it
/// with a click and the next is taken up. F anywhere else and the pin falls back to the bottom, to
/// be lifted again. Lifted past it, it oversets: a slip, which drops the last pin set, wears the
/// picks and may jar a trap that's still armed. Too many slips and the pick snaps in the lock. With
/// the last pin set the plug turns over, and the lock's open; an armed trap goes off as the lid
/// comes up.
/// </summary>
public sealed class PickSession : WorkSession
{
    /// <summary>How far one step of A/D lifts the pin (less as it goes stiff near its set point).</summary>
    public const float LiftPerStep = 0.006f;

    /// <summary>How near (0..1 of the lift) the pin goes stiff, and how near it must be to set (before skill widens it).</summary>
    public const float GiveRange = 0.15f, SetHalfWidth = 0.035f;

    /// <summary>Lifted this far past where it sets, the pin oversets: a slip.</summary>
    public const float OverRange = 0.1f;

    /// <summary>Tension eased on or off a step while W or S is held (about three seconds from none to all).</summary>
    public const float TensionPerStep = 0.006f;

    /// <summary>How far an unsteady finger lets the tension wander a step.</summary>
    public const float TensionWander = 0.0005f;

    /// <summary>
    /// Now and then the plug pushes back against the finger (or gives under it): a shove on the
    /// tension this strong a step (more for a tight lock), lasting this many steps, every so often.
    /// Slower than W/S, so it can always be caught.
    /// </summary>
    public const float PushPerStep = 0.0022f;
    public const int PushStepsMin = 20, PushStepsMax = 35, PushEveryMin = 120, PushEveryMax = 300;

    /// <summary>
    /// The spring-back from too much tension: a pull toward slack this strong a step (just under
    /// W's, so it can be held against) while the tension's too high, and for this many steps after.
    /// </summary>
    public const float HitchPerStep = 0.0048f;
    public const int HitchSteps = 100;

    /// <summary>The band of tension that holds set pins and lets the next one set (before skill widens it).</summary>
    public const float BandLow = 0.35f, BandHigh = 0.65f;

    /// <summary>Steps the plug takes to turn over once the last pin's set.</summary>
    public const int TurnSteps = 50;

    public const int MaxSlips = 5;

    /// <summary>The chance a slip jars an armed trap.</summary>
    public const float JarChance = 0.2f;

    /// <summary>How much of the picks one slip wears away.</summary>
    public const float WearPerSlip = 0.06f;

    /// <summary>The strongbox, or the door.</summary>
    public Entity Box { get; set; } = Entity.Null;

    /// <summary>It's a door's lock (<see cref="LockCut"/>), not a strongbox's.</summary>
    public bool Door { get; set; }

    /// <summary>How tight the lock is (<see cref="LockCut.Tightness"/>); 0 for a strongbox.</summary>
    public float Tightness { get; set; }

    public int Pins { get; set; }
    public uint LockSeed { get; set; }
    public bool Armed { get; set; }

    /// <summary>The pin being worked (all set when it reaches <see cref="Pins"/>).</summary>
    public int Pin { get; private set; }

    public float Lift { get; private set; }

    /// <summary>How stiff the pin is under the pick, 0..1: it rises as the pin nears where it sets. Smooth, no noise: what the fingers feel.</summary>
    public float Feel { get; private set; }

    /// <summary>The tension on the plug, 0..1.</summary>
    public float Tension { get; private set; }

    /// <summary>How far the plug's turned over, 0..1, once every pin's set.</summary>
    public float Turned { get; private set; }

    /// <summary>The step the last pin clicked into place, and the last slip happened (-1: never), for the click and the jolt.</summary>
    public int ClickedAt { get; private set; } = -1;
    public int SlippedAt { get; private set; } = -1;

    /// <summary>The step the set pins last all dropped back, the tension too slack (-1: never).</summary>
    public int DroppedAt { get; private set; } = -1;

    /// <summary>The plug pushing on the tension now: −1 pushing it off, +1 giving under it, 0 not.</summary>
    public int Pushing => _hitch > 0 ? -1 : _pushLeft > 0 ? _push : 0;

    /// <summary>The plug's springing back hard toward slack, after too much tension (<see cref="HitchPerStep"/>).</summary>
    public bool Hitching => _hitch > 0;

    /// <summary>The step F last missed (the pin fell back to the bottom), -1 if never.</summary>
    public int MissedAt { get; private set; } = -1;

    public int Slips { get; private set; }
    public bool Opened { get; private set; }
    public bool Snapped { get; private set; }
    public bool Jarred { get; private set; }

    // Fixed for the lock, from its seed: shared between copies, never written.
    private float[] _heights = [];
    private float _drift;
    private int _steer;
    private int _tense;
    private int _push, _pushLeft, _pushIn = PushEveryMin;
    private int _hitch;

    public float HalfWidth => SetHalfWidth * Hands.Tolerance * (1f - Locksmithing.TightnessSqueeze * Tightness);
    public float Low => 0.5f - (0.5f - BandLow) * Math.Min(Hands.Tolerance, 1.5f);
    public float High => 0.5f + (BandHigh - 0.5f) * Math.Min(Hands.Tolerance, 1.5f);
    public bool InBand => Tension >= Low && Tension <= High;
    public bool Binding => Tension > High;
    public bool Turning => Pin >= Pins && !Done;

    public override Skill Skill => Skill.Crafting;
    public override float Xp => Opened ? 50f : 15f;
    public override string Title => $"Picking the {(Door ? "door's" : "strongbox's")} lock (pin {Math.Min(Pin + 1, Pins)} of {Pins})";

    public override int3? Focus(Simulation sim) => Door && sim.World.IsAlive(Box) ? sim.World.Get<Thing>(Box).Cell : null;

    public static void TryStart(Simulation sim, Entity worker, Entity box)
    {
        if (Lockwork.Problem(sim, worker, box) is { } why)
        {
            sim.Messages.Add(MessageKind.Info, $"Can't pick that: {why}.", sim.LocationOf(worker));
            return;
        }
        var b = sim.World.Get<Strongbox>(box);
        WorkSystem.Start(sim, worker, new PickSession { Box = box, Pins = b.Pins, LockSeed = b.LockSeed, Armed = b.Armed });
    }

    /// <summary>Pick the lock a tinker fitted to a door in reach, while it's locked.</summary>
    public static void TryStartDoor(Simulation sim, Entity worker, Entity door)
    {
        if (DoorProblem(sim, worker, door) is { } why)
        {
            sim.Messages.Add(MessageKind.Info, $"Can't pick that: {why}.", sim.LocationOf(worker));
            return;
        }
        var cut = sim.World.Get<LockCut>(door);
        WorkSystem.Start(sim, worker, new PickSession { Box = door, Door = true, Tightness = cut.Tightness, Pins = cut.Pins, LockSeed = cut.Seed });
    }

    public static string? DoorProblem(Simulation sim, Entity worker, Entity door)
    {
        if (!Locksmithing.HasLock(sim, door) || !sim.IsLocked(door))
            return "it isn't locked";
        if (!AvatarSystem.InReach(sim, sim.World.Get<Position>(worker).Cell, door))
            return "you need to be at the door";
        return Lockwork.Picks(sim, worker).IsNull ? "you need lockpicks" : null;
    }

    public override bool StillValid(Simulation sim) => Door
        ? DoorProblem(sim, Worker, Box) == null
        : Lockwork.IsLocked(sim, Box) && Inventory.OnPerson(sim, Worker, Box) && !Lockwork.Picks(sim, Worker).IsNull;

    protected internal override void Begin()
    {
        _heights = Lockwork.PinHeights(LockSeed, Pins);
        Lift = 0.05f;
    }

    protected override void Axis(int value) => _steer = value;

    /// <summary>F: set the pin, if it's at its set point with the tension right; else it falls back to the bottom.</summary>
    protected override void Press()
    {
        if (Done || Pin >= _heights.Length || Binding)
            return; // frozen: F does nothing
        var off = MathF.Abs(Lift - _heights[Pin]);
        if (InBand && off <= HalfWidth)
        {
            Sample(1f - 0.5f * off / HalfWidth);
            Pin++;
            ClickedAt = StepsDone;
        }
        else
            MissedAt = StepsDone;
        Lift = 0.05f;
    }

    /// <summary>W eases the tension on (-1), S eases it off (+1).</summary>
    protected override void Axis2(int value) => _tense = value;

    protected override void Step()
    {
        if (Done)
            return;
        // The finger on the wrench: eased up or down, else held where it is, wandering a little.
        var wander = Tension > 0.02f ? (Rand.NextFloat() * 2f - 1f) * TensionWander * (1f + Hands.Tremor * 3f) : 0f;
        // The plug's counter-push: mostly springing back against the finger, now and then giving.
        if (_pushLeft > 0)
            _pushLeft--;
        else if (Tension > 0.1f && --_pushIn <= 0)
        {
            _push = Rand.Chance(0.7f) ? -1 : 1;
            _pushLeft = PushStepsMin + Rand.NextInt(PushStepsMax - PushStepsMin + 1);
            _pushIn = PushEveryMin + Rand.NextInt(PushEveryMax - PushEveryMin + 1);
        }
        var push = _pushLeft > 0 ? _push * PushPerStep * (1f + 0.5f * Tightness) : 0f;
        // Too tight, the plug springs back toward slack, and keeps on a moment once it's back in the band.
        if (Binding)
            _hitch = HitchSteps;
        var hitch = _hitch > 0 ? -HitchPerStep : 0f;
        if (_hitch > 0 && !Binding)
            _hitch--;
        Tension = Math.Clamp(Tension - _tense * TensionPerStep + wander + push + hitch, 0f, 1f);
        if (Pin >= _heights.Length)
        {
            // Every pin set: the plug turns over under the finger, and it's open.
            Turned = Math.Min(1f, Turned + 1f / TurnSteps);
            if (Turned >= 1f)
            {
                Opened = true;
                Done = true;
            }
            return;
        }
        // Too slack: every pin set drops back. (Too tight they're frozen, below.)
        if (Tension < Low && Pin > 0)
        {
            Pin = 0;
            Lift = 0.05f;
            DroppedAt = StepsDone;
        }

        var height = _heights[Pin];
        var near = MathF.Abs(Lift - height);
        Feel = Math.Max(0f, 1f - near / GiveRange);
        // Unsteady hands let the pin creep; A/D lift and lower it, stiffly near the set point, and
        // hardly at all against too much tension.
        _drift = Math.Clamp(_drift + (Rand.NextFloat() * 2f - 1f) * 0.0004f * (1f + Hands.Tremor * 4f), -0.0015f, 0.0015f);
        // Frozen by too much tension: the pin won't move at all.
        if (Binding)
            return;
        var rate = LiftPerStep * (1f - 0.55f * Feel);
        Lift = Math.Clamp(Lift + _drift * (1f - 0.6f * Feel) + _steer * rate, 0f, 1f);

        if (Lift > height + OverRange && Tension > 0.15f)
        {
            Slip();
            return;
        }
    }

    /// <summary>Overset: the pin jumps the shear line, the last set pin falls back, and it may jar a trap.</summary>
    private void Slip()
    {
        Sample(0f);
        Slips++;
        SlippedAt = StepsDone;
        if (Pin > 0)
            Pin--;
        Lift = 0.05f;
        if (Armed && Rand.Chance(JarChance))
        {
            Jarred = true;
            Armed = false;
            Done = true;
            return;
        }
        if (Slips >= MaxSlips)
        {
            Snapped = true;
            Done = true;
        }
    }

    public override void Finish(Simulation sim)
    {
        var at = sim.LocationOf(Worker);
        if (Slips > 0)
            Lockwork.WearPicks(sim, Worker, WearPerSlip * Slips + (Snapped ? 0.25f : 0f));
        if (Jarred)
            Lockwork.Spring(sim, Worker, Box);
        else if (Opened && Door)
        {
            sim.SetLocked(Box, false);
            sim.Messages.Add(MessageKind.Info, $"{sim.World.Get<Creature>(Worker).Name} picks the lock on the {sim.World.Get<Thing>(Box).Label}.", at);
        }
        else if (Opened)
            Lockwork.Open(sim, Worker, Box);
        else if (Snapped)
            sim.Messages.Add(MessageKind.Warning, "The pick snaps off in the lock. You'll have to start again.", at);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Box);
        w.Bool(Door);
        w.Float(Tightness);
        w.Int(Pins);
        w.UInt(LockSeed);
        w.Bool(Armed);
        w.Int(Pin);
        w.Float(Lift);
        w.Float(Feel);
        w.Int(Slips);
        w.Bool(Opened);
        w.Bool(Snapped);
        w.Bool(Jarred);
        w.Float(_drift);
        w.Int(_steer);
        w.Float(Tension);
        w.Float(Turned);
        w.Int(ClickedAt);
        w.Int(SlippedAt);
        w.Int(DroppedAt);
        w.Int(MissedAt);
        w.Int(_tense);
        w.Int(_push);
        w.Int(_pushLeft);
        w.Int(_pushIn);
        w.Int(_hitch);
    }

    protected override void ReadState(SaveReader r)
    {
        Box = r.Entity();
        if (r.Version >= 7)
        {
            Door = r.Bool();
            Tightness = Math.Clamp(r.Float(), 0f, 1f);
        }
        Pins = r.Int();
        if (Pins is < 1 or > Lockwork.MaxPins)
            throw new SaveException($"Corrupt save: a lock of {Pins} pins");
        LockSeed = r.UInt();
        Armed = r.Bool();
        Pin = Math.Clamp(r.Int(), 0, Pins);
        Lift = r.Float();
        Feel = r.Float();
        Slips = r.Int();
        Opened = r.Bool();
        Snapped = r.Bool();
        Jarred = r.Bool();
        _drift = r.Float();
        _steer = Math.Sign(r.Int());
        if (r.Version >= 7)
        {
            Tension = r.Float();
            Turned = r.Float();
            ClickedAt = r.Int();
            SlippedAt = r.Int();
            DroppedAt = r.Int();
            MissedAt = r.Int();
            _tense = Math.Sign(r.Int());
            _push = Math.Sign(r.Int());
            _pushLeft = Math.Clamp(r.Int(), 0, PushStepsMax);
            _pushIn = Math.Clamp(r.Int(), 0, PushEveryMax);
            _hitch = Math.Clamp(r.Int(), 0, HitchSteps);
        }
        _heights = Lockwork.PinHeights(LockSeed, Pins);
    }
}

/// <summary>Run a probe along a strongbox's seam for traps (<see cref="ProbeSession"/>).</summary>
public sealed record ProbeBoxCommand(Entity Avatar, Entity Box) : ISimCommand, Core.IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (Core.InventoryCommand.Ready(sim, Avatar) && Core.InventoryCommand.IsItem(sim, Box) && !sim.World.Has<WorkSession>(Avatar))
            ProbeSession.TryStart(sim, Avatar, Box);
    }
}

/// <summary>Pick a strongbox's lock (<see cref="PickSession"/>).</summary>
public sealed record PickLockCommand(Entity Avatar, Entity Box) : ISimCommand, Core.IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (Core.InventoryCommand.Ready(sim, Avatar) && Core.InventoryCommand.IsItem(sim, Box) && !sim.World.Has<WorkSession>(Avatar))
            PickSession.TryStart(sim, Avatar, Box);
    }
}

/// <summary>Pick the lock fitted to a door in reach (<see cref="PickSession"/>).</summary>
public sealed record PickDoorCommand(Entity Avatar, Entity Door) : ISimCommand, Core.IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (Core.InventoryCommand.Ready(sim, Avatar) && Locksmithing.HasLock(sim, Door) && !sim.World.Has<WorkSession>(Avatar))
            PickSession.TryStartDoor(sim, Avatar, Door);
    }
}
