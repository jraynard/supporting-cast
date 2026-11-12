using SupportingCast.Sim.Components;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Systems;
using SupportingCast.Sim.Work;

namespace SupportingCast.Sim.Locks;

/// <summary>
/// Picking a strongbox's lock, one pin at a time. A/D lift the pin; the feel needle shows how
/// much it gives, and it gives most at the height where it sets. F sets it there. Set every pin
/// and the lid comes up; an armed trap goes off as it does. A slip (F off the mark) drops the last
/// pin set, wears the picks, and may jar a trap that's still armed. Too many slips and the pick
/// snaps in the lock.
/// </summary>
public sealed class PickSession : WorkSession
{
    /// <summary>How far one step of A/D lifts the pin.</summary>
    public const float LiftPerStep = 0.006f;

    /// <summary>How near (0..1 of the lift) the pin gives, and how near it must be to set (before skill widens it).</summary>
    public const float GiveRange = 0.15f, SetHalfWidth = 0.035f;

    public const int MaxSlips = 5;

    /// <summary>The chance a slip jars an armed trap.</summary>
    public const float JarChance = 0.2f;

    /// <summary>How much of the picks one slip wears away.</summary>
    public const float WearPerSlip = 0.06f;

    public Entity Box { get; set; } = Entity.Null;
    public int Pins { get; set; }
    public uint LockSeed { get; set; }
    public bool Armed { get; set; }

    /// <summary>The pin being worked (all set when it reaches <see cref="Pins"/>).</summary>
    public int Pin { get; private set; }

    public float Lift { get; private set; }

    /// <summary>How much the pin gives at this lift, 0..1, as the fingers feel it.</summary>
    public float Feel { get; private set; }

    public int Slips { get; private set; }
    public bool Opened { get; private set; }
    public bool Snapped { get; private set; }
    public bool Jarred { get; private set; }

    // Fixed for the lock, from its seed: shared between copies, never written.
    private float[] _heights = [];
    private float _drift;
    private int _steer;

    public float HalfWidth => SetHalfWidth * Hands.Tolerance;

    public override Skill Skill => Skill.Crafting;
    public override float Xp => Opened ? 50f : 15f;
    public override string Title => $"Picking the strongbox's lock (pin {Math.Min(Pin + 1, Pins)} of {Pins})";

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

    public override bool StillValid(Simulation sim) =>
        Lockwork.IsLocked(sim, Box) && Inventory.OnPerson(sim, Worker, Box) && !Lockwork.Picks(sim, Worker).IsNull;

    protected internal override void Begin()
    {
        _heights = Lockwork.PinHeights(LockSeed, Pins);
        Lift = 0.05f;
    }

    protected override void Axis(int value) => _steer = value;

    protected override void Step()
    {
        if (Pin >= _heights.Length)
            return;
        // Unsteady hands let the pin creep; A/D lift and lower it.
        _drift = Math.Clamp(_drift + (Rand.NextFloat() * 2f - 1f) * 0.0004f * (1f + Hands.Tremor * 4f), -0.002f, 0.002f);
        Lift = Math.Clamp(Lift + _drift + _steer * LiftPerStep, 0f, 1f);
        var give = Math.Max(0f, 1f - MathF.Abs(Lift - _heights[Pin]) / GiveRange);
        Feel = Math.Clamp(give * 0.9f + Rand.NextFloat() * 0.12f * (1f + Hands.Tremor), 0f, 1f);
    }

    protected override void Press()
    {
        if (Done || Pin >= _heights.Length)
            return;
        var off = MathF.Abs(Lift - _heights[Pin]);
        if (off <= HalfWidth)
        {
            Sample(1f - 0.5f * off / HalfWidth);
            Pin++;
            Lift = 0.05f;
            if (Pin >= _heights.Length)
            {
                Opened = true;
                Done = true;
            }
            return;
        }
        Sample(0f);
        Slips++;
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
        else if (Opened)
            Lockwork.Open(sim, Worker, Box);
        else if (Snapped)
            sim.Messages.Add(MessageKind.Warning, "The pick snaps off in the lock. You'll have to start again.", at);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Box);
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
    }

    protected override void ReadState(SaveReader r)
    {
        Box = r.Entity();
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
