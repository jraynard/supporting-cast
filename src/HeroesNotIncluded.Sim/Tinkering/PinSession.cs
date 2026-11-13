using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tinkering;

/// <summary>
/// Pinning a lock at the tinker's bench, or filing a copy of a key. Each pin starts long and is
/// filed down to its mark: hold F to file (the file bites harder the longer a stroke goes on),
/// let go to look, and Space to set the pin and take up the next. Set near the mark, the pin sits
/// crisply; filed past it, it's sloppy, and far past it the pin is ruined. A lock whose pins all
/// sit crisply is tight, and hard to pick (<see cref="LockCut.Tightness"/>), and its key turns
/// sweetly (<see cref="Key.Fit"/>). Copying a key is the same, filed to the bits of the key in hand.
/// </summary>
public sealed class PinSession : WorkSession
{
    /// <summary>How much a stroke files off per step at its start, and the most a long stroke gets to (times that).</summary>
    public const float FilePerStep = 0.0018f, MostBite = 3f;

    /// <summary>Steps of a stroke for the file to bite one more time as hard.</summary>
    public const int BiteSteps = 70;

    /// <summary>Within this of the mark (before skill widens it) a pin sits perfectly; this far past it, it's ruined.</summary>
    public const float Crisp = 0.012f, Ruined = 0.09f;

    /// <summary>The bench it's done at.</summary>
    public Entity Station { get; set; } = Entity.Null;

    /// <summary>The key being copied; Entity.Null when pinning a new lock.</summary>
    public Entity Source { get; set; } = Entity.Null;

    public uint Seed { get; set; }
    public int Pins { get; set; }

    /// <summary>The pin being filed (all set when it reaches <see cref="Pins"/>).</summary>
    public int Pin { get; private set; }

    /// <summary>How long the pin is still, 1 (uncut) down to 0.</summary>
    public float Length { get; private set; } = 1f;

    /// <summary>How far each set pin landed from its mark (positive: filed past it).</summary>
    public float[] Errors { get; private set; } = [];

    private float[] _marks = [];
    private int _set;
    private int _stroke;

    public bool IsCopy => !Source.IsNull;

    /// <summary>Where the pin being filed should end up (0..1 of its length).</summary>
    public float Mark => Pin < _marks.Length ? _marks[Pin] : 0f;

    public float CrispWidth => Crisp * Hands.Tolerance;

    public override Skill Skill => Skill.Crafting;
    public override float Xp => 15f + 6f * Pins;
    public override string Title => IsCopy ? $"Filing a key (bit {Math.Min(Pin + 1, Pins)} of {Pins})" : $"Pinning a lock (pin {Math.Min(Pin + 1, Pins)} of {Pins})";

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Station) ? sim.World.Get<Thing>(Station).Cell : null;

    /// <summary>Pin a new lock at a stocked bench (from <see cref="Tinkers.WorkAt"/>).</summary>
    public static void StartLock(Simulation sim, Entity worker, Entity station)
    {
        var pins = Locksmithing.PinsFor(SkillRules.LevelOf(sim, worker, Skill.Crafting));
        WorkSystem.Start(sim, worker, new PinSession { Station = station, Seed = sim.Rand.NextUInt() | 1u, Pins = pins });
    }

    /// <summary>Copy a key you have at the bench, onto a blank filed from an iron bar.</summary>
    public static void StartCopy(Simulation sim, Entity worker, Entity station, Entity key)
    {
        if (CopyProblem(sim, worker, station, key) is { } why)
        {
            sim.Messages.Add(MessageKind.Info, $"Can't copy that: {why}.", sim.LocationOf(worker));
            return;
        }
        var k = sim.World.Get<Key>(key);
        WorkSystem.Start(sim, worker, new PinSession { Station = station, Source = key, Seed = k.Seed, Pins = k.Pins });
    }

    /// <summary>Why this key can't be copied here now; null if it can.</summary>
    public static string? CopyProblem(Simulation sim, Entity worker, Entity station, Entity key)
    {
        if (!Tinkers.IsBench(sim, station) || !AvatarSystem.InReach(sim, sim.World.Get<Position>(worker).Cell, station))
            return "you need to be at a tinker's bench";
        if (!Locksmithing.IsKey(sim, key) || !Inventory.OnPerson(sim, worker, key))
            return "you need the key in hand or in your pack";
        if (Tinkers.Bar(sim, worker).IsNull)
            return "you need an iron bar to file the blank from";
        return null;
    }

    public override bool StillValid(Simulation sim)
    {
        var w = sim.World;
        if (!Tinkers.IsBench(sim, Station) || !AvatarSystem.InReach(sim, w.Get<Position>(Worker).Cell, Station))
            return false;
        if (IsCopy)
            return Inventory.OnPerson(sim, Worker, Source) && !Tinkers.Bar(sim, Worker).IsNull;
        return w.Get<Workbench>(Station) is { Active: { } r, Stocked: true } bench && bench.By == Worker && Tinkers.MakesLock(r);
    }

    protected internal override void Begin()
    {
        _marks = Lockwork.PinHeights(Seed, Pins);
        Errors = new float[_marks.Length];
    }

    protected override void Cloned()
    {
        Errors = (float[])Errors.Clone();
    }

    /// <summary>Set the pin and take up the next (Space), on the next step.</summary>
    protected override void Act() => _set = 1;

    protected override void Step()
    {
        if (Pin >= _marks.Length)
            return;
        if (_set > 0)
        {
            SetPin();
            _set = 0; // one pin per press
            return;
        }
        if (!Held)
        {
            _stroke = 0;
            return;
        }
        _stroke++;
        var bite = Math.Min(MostBite, 1f + _stroke / (float)BiteSteps);
        var wobble = 1f + (Rand.NextFloat() * 2f - 1f) * (0.25f + Hands.Tremor);
        Length = Math.Max(0f, Length - FilePerStep * bite * wobble);
    }

    private void SetPin()
    {
        var past = Mark - Length; // filed beyond the mark: positive
        Errors[Pin] = past;
        Sample(Accuracy(past));
        Pin++;
        Length = 1f;
        _stroke = 0;
        if (Pin >= _marks.Length)
            Done = true;
    }

    /// <summary>How well a pin set this far from its mark sits, 0..1. Short of the mark is as bad as past it, but never ruins it.</summary>
    public float Accuracy(float past)
    {
        var off = MathF.Abs(past);
        if (past >= Ruined)
            return 0f;
        return off <= CrispWidth ? 1f : Math.Max(0f, 1f - (off - CrispWidth) / (Ruined - CrispWidth));
    }

    public override void Finish(Simulation sim)
    {
        var w = sim.World;
        var at = sim.LocationOf(Worker);
        var ruined = Errors.Count(e => e >= Ruined);
        var fit = Quality;
        if (IsCopy)
        {
            var bar = Tinkers.Bar(sim, Worker);
            if (bar.IsNull)
                return;
            sim.DestroyThing(Inventory.TakeFromPerson(sim, Worker, bar, 1));
            // A copy is never better than what it was filed from.
            var copy = Locksmithing.MakeKey(sim, Seed, Pins, Math.Min(fit, w.Get<Key>(Source).Fit));
            Inventory.Stow(sim, Worker, copy);
            sim.Messages.Add(MessageKind.Info, ruined > 0 ? "You file a copy of the key. A bit's gone too deep: it'll stick." : "You file a copy of the key.", at);
            return;
        }
        var quality = Crafting.QualityFrom(fit);
        foreach (var item in Crafting.Finish(sim, Station, Worker, quality))
        {
            if (w.Get<Thing>(item).Def.Name == Locksmithing.LockDef)
                Inventory.Stow(sim, Worker, Locksmithing.CutLock(sim, item, Seed, Pins, fit, 0.4f + 0.6f * fit, Worker));
            Inventory.Stow(sim, Worker, item);
        }
        var note = ruined > 0 ? $" {ruined} {(ruined == 1 ? "pin was" : "pins were")} filed too far: it's a loose lock." : fit >= 0.85f ? " Every pin sits crisply: it'll be a hard one to pick." : "";
        sim.Messages.Add(MessageKind.Info, $"You pin a {Pins}-pin lock and cut its key.{note}", at);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Station);
        w.Entity(Source);
        w.UInt(Seed);
        w.Int(Pins);
        w.Int(Pin);
        w.Float(Length);
        foreach (var e in Errors)
            w.Float(e);
        w.Int(_set);
        w.Int(_stroke);
    }

    protected override void ReadState(SaveReader r)
    {
        Station = r.Entity();
        Source = r.Entity();
        Seed = r.UInt();
        Pins = r.Int();
        if (Pins is < 1 or > Lockwork.MaxPins)
            throw new SaveException($"Corrupt save: a lock of {Pins} pins");
        Pin = Math.Clamp(r.Int(), 0, Pins);
        Length = r.Float();
        _marks = Lockwork.PinHeights(Seed, Pins);
        Errors = new float[Pins];
        for (var i = 0; i < Pins; i++)
            Errors[i] = r.Float();
        _set = Math.Sign(r.Int());
        _stroke = r.Int();
    }
}

/// <summary>Copy a key you carry at a tinker's bench in reach (<see cref="PinSession"/>).</summary>
public sealed record CopyKeyCommand(Entity Avatar, Entity Station, Entity Key) : ISimCommand, Core.IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (Core.InventoryCommand.Ready(sim, Avatar) && Core.InventoryCommand.IsItem(sim, Key) && !sim.World.Has<WorkSession>(Avatar)
            && sim.World.IsAlive(Station) && sim.World.Has<Thing>(Station))
            PinSession.StartCopy(sim, Avatar, Station, Key);
    }
}
