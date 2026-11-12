using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tinkering;

/// <summary>
/// Fitting a contraption together at the tinker's bench, part by part, then winding its spring;
/// or fitting a new axle to a wagon (no spring). Each part starts too tight: hold F to file it,
/// and the bind needle drops as it eases; past the fit it rattles instead, loose. D seats the
/// part. Seated at the fit, it's sound; loose, it's sloppy and the work the worse for it; forced
/// home while it still binds, it seats, but leaves a hidden binding that may jam it later
/// (<see cref="Contraption.Binding"/>). Then the spring: tap F to wind it a click at a time
/// (it creeps back between clicks), and D to latch it while the pawl's catching. Past the catch
/// it snaps, and the parts come apart to be fitted again.
/// </summary>
public sealed class FitSession : WorkSession
{
    /// <summary>How much a part eases per step of filing.</summary>
    public const float FilePerStep = 0.0032f;

    /// <summary>Within this of the fit (before skill widens it), a part seats true.</summary>
    public const float SeatWidth = 0.045f;

    /// <summary>How far one tap winds the spring, how much it creeps back a step, and how far past the catch it snaps.</summary>
    public const float WindPerTap = 0.05f, CreepPerStep = 0.0006f, SnapPast = 0.1f;

    /// <summary>Half the width of the band the pawl catches in, before skill widens it.</summary>
    public const float CatchHalf = 0.05f;

    public Entity Station { get; set; } = Entity.Null;

    /// <summary>The wagon an axle's being fitted to; Entity.Null at the bench.</summary>
    public Entity Wagon { get; set; } = Entity.Null;

    public int Parts { get; set; } = 3;
    public bool Winds { get; set; }

    /// <summary>What it's being made into, for the title ("tapping rod").</summary>
    public string What { get; set; } = "";

    public int Part { get; private set; }

    /// <summary>How far the part's been filed, 0 (as cast) up.</summary>
    public float Filed { get; private set; }

    /// <summary>The bind the fingers feel, 0..1: high while it's tight.</summary>
    public float Bind { get; private set; }

    /// <summary>The rattle, 0..1: rising once it's past the fit.</summary>
    public float Rattle { get; private set; }

    public bool Winding => Part >= Parts && Winds;
    public float Tension { get; private set; }

    /// <summary>The pawl's catching (the spring's in the band to latch).</summary>
    public bool Catching { get; private set; }

    public bool Binding { get; private set; }
    public bool Snapped { get; private set; }
    public bool Latched { get; private set; }

    private float[] _fits = [];
    private float _catch;
    private int _seat;

    public float SeatHalf => SeatWidth * Hands.Tolerance;
    public float CatchHalfWidth => CatchHalf * Hands.Tolerance;

    public override Skill Skill => Skill.Crafting;
    public override float Xp => 15f + 8f * Parts;
    public override string Title => Winding ? $"Winding the {What}'s spring" : $"Fitting the {What} (part {Math.Min(Part + 1, Parts)} of {Parts})";

    public override int3? Focus(Simulation sim) =>
        sim.World.IsAlive(Station) ? sim.World.Get<Thing>(Station).Cell
        : sim.World.IsAlive(Wagon) && sim.World.TryGet<Thing>(Wagon, out var t) ? t.Cell : null;

    /// <summary>Fit a contraption together at a stocked bench (from <see cref="Tinkers.WorkAt"/>).</summary>
    public static void StartMaking(Simulation sim, Entity worker, Entity station, ContraptionProps c)
    {
        var bench = sim.World.Get<Workbench>(station);
        var what = bench.Active?.Products.FirstOrDefault(p => p.ThingDef.Contraption != null)?.ThingDef.Label ?? "contraption";
        WorkSystem.Start(sim, worker, new FitSession { Station = station, Parts = Math.Clamp(c.Parts, 2, 5), Winds = true, What = what });
    }

    /// <summary>Fit a new axle to a wagon in reach (<see cref="Wagons"/>).</summary>
    public static void StartAxle(Simulation sim, Entity worker, Entity wagon) =>
        WorkSystem.Start(sim, worker, new FitSession { Wagon = wagon, Parts = 2, Winds = false, What = "axle" });

    public override bool StillValid(Simulation sim)
    {
        var w = sim.World;
        if (!Wagon.IsNull)
            return Wagons.AxleProblem(sim, Worker, Wagon) == null;
        return Tinkers.IsBench(sim, Station) && AvatarSystem.InReach(sim, w.Get<Position>(Worker).Cell, Station)
            && w.Get<Workbench>(Station) is { Active: { } r, Stocked: true } bench && bench.By == Worker && Tinkers.MakesContraption(r) != null;
    }

    protected internal override void Begin()
    {
        _fits = new float[Parts];
        for (var i = 0; i < Parts; i++)
            _fits[i] = Rand.Range(0.35f, 0.8f);
        _catch = Rand.Range(0.55f, 0.78f);
    }

    protected override void Cloned()
    {
        _fits = (float[])_fits.Clone();
    }

    protected override void Axis(int value) => _seat = value;

    protected override void Press()
    {
        if (Done || !Winding)
            return;
        Tension += WindPerTap * (1f + (Rand.NextFloat() * 2f - 1f) * (0.2f + Hands.Tremor));
        if (Tension > _catch + CatchHalfWidth + SnapPast)
        {
            Snapped = true;
            Sample(0f);
            Done = true;
        }
    }

    protected override void Step()
    {
        if (Done)
            return;
        if (Winding)
        {
            Tension = Math.Max(0f, Tension - CreepPerStep);
            var off = MathF.Abs(Tension - _catch);
            // The pawl ticks over the ratchet while it's in the band: with a little noise to it.
            Catching = off <= CatchHalfWidth && Rand.NextFloat() > 0.1f * (1f + Hands.Tremor * 3f);
            if (_seat > 0)
            {
                _seat = 0;
                Latched = true;
                Sample(off <= CatchHalfWidth ? 1f : Tension < _catch ? 0.35f : 0.6f);
                Done = true;
            }
            return;
        }
        if (Part >= Parts)
        {
            Done = true;
            return;
        }
        if (_seat > 0)
        {
            _seat = 0;
            SeatPart();
            return;
        }
        if (Held)
            Filed += FilePerStep * (1f + (Rand.NextFloat() * 2f - 1f) * (0.3f + Hands.Tremor));
        var fit = _fits[Part];
        var noise = (Rand.NextFloat() * 2f - 1f) * 0.08f * (1f + Hands.Tremor * 2f);
        Bind = Math.Clamp((fit - Filed) / fit + noise, 0f, 1f);
        Rattle = Math.Clamp((Filed - fit) / 0.25f + noise, 0f, 1f);
    }

    private void SeatPart()
    {
        var off = Filed - _fits[Part]; // past the fit: positive
        if (off < -SeatHalf)
        {
            Binding = true;
            Sample(0.5f);
        }
        else if (off <= SeatHalf)
            Sample(1f - 0.3f * MathF.Abs(off) / SeatHalf);
        else
            Sample(Math.Max(0f, 0.6f * (1f - (off - SeatHalf) / 0.25f)));
        Part++;
        Filed = 0f;
        Bind = 1f;
        Rattle = 0f;
        if (Part >= Parts && !Winds)
            Done = true;
    }

    public override void Finish(Simulation sim)
    {
        var w = sim.World;
        var at = sim.LocationOf(Worker);
        if (!Wagon.IsNull)
        {
            Wagons.AxleFitted(sim, Worker, Wagon, Quality);
            return;
        }
        if (Snapped)
        {
            // The parts come apart, and the materials stay staged on the bench to begin again.
            sim.Messages.Add(MessageKind.Warning, $"The {What}'s spring snaps as you wind it. The parts come apart: you'll have to fit them again.", at);
            return;
        }
        var quality = Crafting.QualityFrom(Quality);
        foreach (var item in Crafting.Finish(sim, Station, Worker, quality))
        {
            if (w.Get<Thing>(item).Def.Contraption != null)
                w.Add(item, new Contraption { Binding = Binding, Maker = Worker });
            Inventory.Stow(sim, Worker, item);
            sim.Messages.Add(MessageKind.Info, $"Made a {sim.LabelOf(item)}.", at);
        }
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Station);
        w.Entity(Wagon);
        w.Int(Parts);
        w.Bool(Winds);
        w.String(What);
        w.Int(Part);
        w.Float(Filed);
        w.Float(Bind);
        w.Float(Rattle);
        w.Float(Tension);
        w.Bool(Catching);
        w.Bool(Binding);
        w.Bool(Snapped);
        w.Bool(Latched);
        foreach (var f in _fits)
            w.Float(f);
        w.Float(_catch);
        w.Int(_seat);
    }

    protected override void ReadState(SaveReader r)
    {
        Station = r.Entity();
        Wagon = r.Entity();
        Parts = r.Int();
        if (Parts is < 1 or > 8)
            throw new SaveException($"Corrupt save: a contraption of {Parts} parts");
        Winds = r.Bool();
        What = r.String();
        Part = Math.Clamp(r.Int(), 0, Parts);
        Filed = r.Float();
        Bind = r.Float();
        Rattle = r.Float();
        Tension = r.Float();
        Catching = r.Bool();
        Binding = r.Bool();
        Snapped = r.Bool();
        Latched = r.Bool();
        _fits = new float[Parts];
        for (var i = 0; i < Parts; i++)
            _fits[i] = r.Float();
        _catch = r.Float();
        _seat = Math.Sign(r.Int());
    }
}
