using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tinkering;

/// <summary>
/// Fitting a contraption together at the tinker's bench, part by part, then winding its spring;
/// or fitting a new axle to a wagon (no spring). Each part has to go into its seat square: it
/// wanders off true as the hands shake, A/D bring it back, and holding F presses it in. Lined up,
/// it slides home under the pressure; pressed while it's off true, it binds against the seat's
/// edge instead, and pressed hard enough for long enough it jams in crooked: seated, but with a
/// hidden binding that may jam the thing later (<see cref="Contraption.Binding"/>). The deeper a
/// part's in, the better the seat guides it. Then the spring: tap F to wind it a click at a time
/// (it creeps back between clicks), and Space to latch it while the pawl's catching. Short of the catch
/// the pawl won't hold and the winding goes on; past it the spring strains (latched there, it's
/// weaker work), and further it snaps, and the parts come apart to be fitted again. Each seated
/// part sits home a moment before the next comes up, so it's plain one's done.
/// </summary>
public sealed class FitSession : WorkSession
{
    /// <summary>Half the width of true (as a share of the seat), before skill widens it: within it, a pressed part slides in.</summary>
    public const float TrueHalf = 0.07f;

    /// <summary>How far A/D move the part a step, and how fast the hands let it wander.</summary>
    public const float SteerPerStep = 0.0045f, Wander = 0.0007f;

    /// <summary>Pressure gained a step while F's held, and lost a step once it's let go.</summary>
    public const float PressPerStep = 0.025f, EasePerStep = 0.05f;

    /// <summary>Below this pressure a part neither moves in nor binds: a light touch to feel it.</summary>
    public const float LightTouch = 0.15f;

    /// <summary>How far in a part goes a step at full pressure, lined up (about three seconds home).</summary>
    public const float SinkPerStep = 0.0055f;

    /// <summary>How fast pressing off true binds a part, at full pressure and well off; and how fast it eases once it isn't.</summary>
    public const float BindPerStep = 0.014f, UnbindPerStep = 0.006f;

    /// <summary>How far one tap winds the spring, how much it creeps back a step, and how far past the catch it snaps.</summary>
    public const float WindPerTap = 0.05f, CreepPerStep = 0.0004f, SnapPast = 0.12f;

    /// <summary>Half the width of the band the pawl catches in, before skill widens it.</summary>
    public const float CatchHalf = 0.05f;

    /// <summary>Steps a seated part sits before the next comes up (about a second): you see it go home.</summary>
    public const int SettleSteps = 60;

    /// <summary>Steps the "won't hold yet" of an early latch shows for.</summary>
    public const int SlipSteps = 90;

    public Entity Station { get; set; } = Entity.Null;

    /// <summary>The wagon an axle's being fitted to; Entity.Null at the bench.</summary>
    public Entity Wagon { get; set; } = Entity.Null;

    public int Parts { get; set; } = 3;
    public bool Winds { get; set; }

    /// <summary>What it's being made into, for the title ("tapping rod").</summary>
    public string What { get; set; } = "";

    public int Part { get; private set; }

    /// <summary>How far off true the part is, −1..1 of the seat's width (0: square over it).</summary>
    public float Offset { get; private set; }

    /// <summary>How hard it's being pressed, 0..1.</summary>
    public float Pressure { get; private set; }

    /// <summary>How far into its seat the part's gone, 0..1 (home).</summary>
    public float Depth { get; private set; }

    /// <summary>How badly it's binding against the seat's edge, 0..1 (at 1 it jams in crooked).</summary>
    public float Bind { get; private set; }

    /// <summary>The last part went in crooked (for the window to say so).</summary>
    public bool Jammed { get; private set; }

    /// <summary>Steps left of a seated part sitting home before the next comes up (0: fitting).</summary>
    public int Settling { get; private set; }

    /// <summary>Steps left of an early latch's slip showing: the pawl wouldn't hold yet.</summary>
    public int Slipped { get; private set; }

    /// <summary>Where the pawl catches (the middle of the band), as tension: the ratchet's marked teeth.</summary>
    public float CatchAt => _catch;

    public bool Winding => Part >= Parts && Winds;
    public float Tension { get; private set; }

    /// <summary>Taps of the winding key so far: the ratchet turns a click for each.</summary>
    public int Clicks { get; private set; }

    /// <summary>The pawl's in a tooth (the spring's in the band to latch).</summary>
    public bool Catching { get; private set; }

    /// <summary>How near snapping, 0..1, once it's wound past the catch (0 below it).</summary>
    public float Strain => Math.Clamp((Tension - (_catch + CatchHalfWidth)) / SnapPast, 0f, 1f);

    public bool Binding { get; private set; }
    public bool Snapped { get; private set; }
    public bool Latched { get; private set; }

    private float _drift;
    private float _catch;
    private int _steer, _latch;
    private float _trueSum;
    private int _trueSteps;

    public float TrueHalfWidth => TrueHalf * Hands.Tolerance;
    public float CatchHalfWidth => CatchHalf * Hands.Tolerance;
    public bool OnTrue => MathF.Abs(Offset) <= TrueHalfWidth;

    public override Skill Skill => Skill.Crafting;
    public override float Xp => 15f + 8f * Parts;
    public override string Title => Winding ? $"Winding the {What}'s spring" : $"Fitting the {What} (part {Math.Min(Part + 1, Parts)} of {Parts})";

    public override int3? Focus(Simulation sim) =>
        sim.World.IsAlive(Station) ? sim.World.Get<Thing>(Station).Cell
        : sim.World.IsAlive(Wagon) && sim.World.TryGet<Thing>(Wagon, out var t) ? t.Cell : null;

    /// <summary>Fit a contraption (or the crossbow) together at a stocked bench (from <see cref="Tinkers.WorkAt"/>).</summary>
    public static void StartMaking(Simulation sim, Entity worker, Entity station, RecipeDef recipe, FitProps fit)
    {
        var what = (recipe.Products.FirstOrDefault(p => p.ThingDef.Contraption != null) ?? recipe.Products.FirstOrDefault())?.ThingDef.Label ?? "contraption";
        WorkSystem.Start(sim, worker, new FitSession { Station = station, Parts = Math.Clamp(fit.Parts, 2, 5), Winds = fit.Winds, What = what });
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
            && w.Get<Workbench>(Station) is { Active: { } r, Stocked: true } bench && bench.By == Worker && Tinkers.FitOf(r) != null;
    }

    protected internal override void Begin()
    {
        _catch = Rand.Range(0.55f, 0.75f);
        NextPart();
    }

    /// <summary>A fresh part over its seat, somewhat off true.</summary>
    private void NextPart()
    {
        Offset = Rand.Range(0.25f, 0.6f) * (Rand.Chance(0.5f) ? -1f : 1f);
        _drift = 0f;
        Depth = 0f;
        Bind = 0f;
        _trueSum = 0f;
        _trueSteps = 0;
    }

    protected override void Axis(int value) => _steer = value;

    /// <summary>Latch the wound spring (Space); nothing while fitting parts.</summary>
    protected override void Act() => _latch = 1;

    protected override void Press()
    {
        if (Done || !Winding)
            return;
        Clicks++;
        Tension += WindPerTap * (1f + (Rand.NextFloat() * 2f - 1f) * (0.15f + Hands.Tremor));
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
            Catching = MathF.Abs(Tension - _catch) <= CatchHalfWidth;
            if (Slipped > 0)
                Slipped--;
            if (_latch > 0)
            {
                _latch = 0;
                // Short of the catch the pawl won't hold: it slips back, and the winding goes on.
                if (!Catching && Tension < _catch)
                {
                    Slipped = SlipSteps;
                    return;
                }
                Latched = true;
                Sample(Catching ? 1f : 0.6f); // past the catch: latched, but strained
                Done = true;
            }
            return;
        }
        if (Part >= Parts)
        {
            Done = true;
            return;
        }
        _latch = 0; // Space does nothing while fitting: it only latches the spring
        if (Settling > 0)
        {
            // The seated part sits home a moment; then on to the next.
            if (--Settling == 0)
                NextUp();
            return;
        }

        // The hands let it wander, less as the seat takes it; A/D bring it back.
        _drift = Math.Clamp(_drift * 0.97f + (Rand.NextFloat() * 2f - 1f) * Wander * (1f + Hands.Tremor * 3f), -0.01f, 0.01f);
        Offset = Math.Clamp(Offset + _drift * (1f - 0.7f * Depth) + _steer * SteerPerStep, -1f, 1f);
        Pressure = Math.Clamp(Pressure + (Held ? PressPerStep : -EasePerStep), 0f, 1f);

        if (Pressure > LightTouch)
        {
            if (OnTrue)
            {
                Depth = Math.Min(1f, Depth + SinkPerStep * Pressure);
                _trueSum += 1f - 0.5f * MathF.Abs(Offset) / TrueHalfWidth;
                _trueSteps++;
                Bind = Math.Max(0f, Bind - UnbindPerStep);
            }
            else
            {
                // Pressed off true: it rides up on the seat's edge, the further off the harder.
                var off = Math.Min(1f, (MathF.Abs(Offset) - TrueHalfWidth) / 0.2f + 0.25f);
                Bind = Math.Min(1f, Bind + BindPerStep * Pressure * off);
                if (Bind >= 1f)
                {
                    // Forced: it jams home crooked. Seated, but it'll bind.
                    Binding = true;
                    Jammed = true;
                    Sample(0.35f);
                    Seated();
                    return;
                }
            }
        }
        else
            Bind = Math.Max(0f, Bind - UnbindPerStep);

        if (Depth >= 1f)
        {
            var square = _trueSteps == 0 ? 0.5f : _trueSum / _trueSteps;
            Sample(Math.Clamp(square - 0.4f * Bind, 0f, 1f));
            Seated();
        }
    }

    /// <summary>The part's home: it sits there a moment, so you see it go in, before the next comes up.</summary>
    private void Seated()
    {
        Depth = 1f;
        Pressure = 0f;
        Settling = SettleSteps;
    }

    private void NextUp()
    {
        Part++;
        if (Part >= Parts && !Winds)
            Done = true;
        else if (Part < Parts)
        {
            Jammed = false;
            NextPart();
        }
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
        w.Float(Offset);
        w.Float(Pressure);
        w.Float(Depth);
        w.Float(Bind);
        w.Bool(Jammed);
        w.Int(Settling);
        w.Int(Slipped);
        w.Float(Tension);
        w.Int(Clicks);
        w.Bool(Catching);
        w.Bool(Binding);
        w.Bool(Snapped);
        w.Bool(Latched);
        w.Float(_drift);
        w.Float(_catch);
        w.Int(_steer);
        w.Int(_latch);
        w.Float(_trueSum);
        w.Int(_trueSteps);
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
        Offset = Math.Clamp(r.Float(), -1f, 1f);
        Pressure = r.Float();
        Depth = r.Float();
        Bind = r.Float();
        Jammed = r.Bool();
        Settling = Math.Clamp(r.Int(), 0, SettleSteps);
        Slipped = Math.Clamp(r.Int(), 0, SlipSteps);
        Tension = r.Float();
        Clicks = r.Int();
        Catching = r.Bool();
        Binding = r.Bool();
        Snapped = r.Bool();
        Latched = r.Bool();
        _drift = r.Float();
        _catch = r.Float();
        _steer = Math.Clamp(r.Int(), -1, 1);
        _latch = Math.Clamp(r.Int(), 0, 1);
        _trueSum = r.Float();
        _trueSteps = r.Int();
    }
}
