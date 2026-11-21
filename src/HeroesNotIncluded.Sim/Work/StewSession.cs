using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// Tending the stew (<see cref="Cooking"/>). D feeds the fire under the pot and A banks it; the heat
/// follows slowly, and shows only in how the surface looks. It cooks from a simmer, faster at a
/// boil, but past a simmer it catches on the bottom unless it's stirred (hold F); stirring all the
/// time lets the heat out and slows it. Done when it's cooked through: the fire is banked to keep
/// it warm. Walk away early and the pot carries on as the fire was left. With the mouse the spoon
/// goes round as the mouse does: press in the pot and circle it (held still, it isn't stirring),
/// and the wheel feeds or banks the fire.
/// </summary>
public sealed class StewSession : WorkSession
{
    public Entity Pot { get; set; } = Entity.Null;

    /// <summary>The fire beside it was burning when the work began (if it goes out, the work stops).</summary>
    public bool Lit { get; private set; }

    public float Heat { get; private set; }
    public float Flame { get; private set; }
    public float Doneness { get; private set; }
    public float Scorch { get; private set; }

    /// <summary>How fast it cooks for what's in it (<see cref="Cooking.SpeedOf"/>): fixed for the session.</summary>
    public float Speed { get; private set; } = 1f;

    /// <summary>Steps since the spoon last went round.</summary>
    public int SinceStir { get; private set; } = Cooking.Untended;

    /// <summary>A fatty pot (<see cref="Cooking.Fatty"/>): at a rolling boil its fat may catch.</summary>
    public bool Fatty { get; private set; }

    /// <summary>Steps left before a flare's a real fire, unless the lid goes on (Space); 0 when it isn't flaring.</summary>
    public int Flaring { get; private set; }

    /// <summary>The flare was left: the session ends, and <see cref="Finish"/> sets the kitchen alight.</summary>
    public bool Caught { get; private set; }

    /// <summary>Lids clapped on, for the drawing.</summary>
    public int Lidded { get; private set; }

    private int _flameDir;

    public Surface Surface => Cooking.SurfaceOf(Heat);
    /// <summary>The spoon's going round: F held, or the mouse pressed in the pot and circling.</summary>
    public bool Stirring => Held && (!_byMouse || _spin >= StirSpin);

    /// <summary>Starting to catch: a careful cook can smell it (the bottom's catching at more than a trickle).</summary>
    public bool Catching => Heat > Cooking.SimmerAt && !Stirring && SinceStir > Cooking.CatchSteps;

    /// <summary>How fast the mouse must go round the pot to stir, as the smoothed turn a step: a round every three seconds or so.</summary>
    public const float StirSpin = 0.4f;

    /// <summary>How the turn a step is smoothed: the spoon keeps going a moment after the mouse slows.</summary>
    public const float SpinKeep = 0.92f;

    // The button pressed in the pot (it stirs as the mouse goes round, not by being held); how far
    // round it's gone since the last step (a turn, in radians), and that smoothed; the wheel on the fire.
    private bool _byMouse;
    private float _swept, _spin;
    private int _flameWheel;

    protected override void Clicked(float? u, float? v)
    {
        if (u != null)
            _byMouse = true;
    }

    /// <summary>The mouse over the pot (its mouth the picture's middle): how far round the middle it's gone, while pressed.</summary>
    protected override void Pointed(float u, float v)
    {
        if (!_byMouse || !Held || PointU is not { } was || PointV is not { } wasV)
            return;
        var a0 = DetMath.Atan2(wasV - 0.5f, was - 0.5f);
        var a1 = DetMath.Atan2(v - 0.5f, u - 0.5f);
        var d = a1 - a0;
        d -= MathF.Round(d / MathF.Tau) * MathF.Tau; // the short way round
        _swept += Math.Min(MathF.Abs(d), 1.2f);      // a jump across the middle isn't a stir
    }

    /// <summary>The wheel feeds the fire (up) or banks it (down), as D and A do.</summary>
    protected override void Axis2(int value) => _flameWheel = -value;

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Pot) && sim.World.TryGet<Thing>(Pot, out var t) ? t.Cell : null;

    public override Skill Skill => Skill.Cooking;
    public override float Xp => 25f;
    public override string Title => "Tending the stew";

    public override bool HeardByOthers => true;

    internal static StewSession For(Simulation sim, Entity pot)
    {
        var p = Cooking.PotOf(sim, pot);
        return new StewSession
        {
            Pot = pot, Lit = Cooking.Lit(sim, pot), Heat = p.Heat, Flame = p.Flame, Doneness = p.Doneness, Scorch = p.Scorch,
            Speed = Cooking.SpeedOf(p), Fatty = Cooking.Fatty(p),
            Flaring = p.FlareUntil > sim.Clock.Tick ? (int)Math.Min(Cooking.FlareSteps, p.FlareUntil - sim.Clock.Tick) : 0,
        };
    }

    public override bool StillValid(Simulation sim) =>
        Cooking.IsPot(sim, Pot) && !Cooking.PotOf(sim, Pot).Empty && Cooking.Lit(sim, Pot) == Lit
        && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Pot);

    protected override void Axis(int value) => _flameDir = value;

    /// <summary>The lid on a flare: the flames gutter out, a little caught for it.</summary>
    protected override void Act()
    {
        if (Flaring <= 0)
            return;
        Flaring = 0;
        Lidded++;
        Scorch = Math.Min(1f, Scorch + 0.04f);
        Heat = Math.Max(0f, Heat - 0.15f);
    }

    protected override void Step()
    {
        Flame = Math.Clamp(Flame + (_flameDir != 0 ? _flameDir : _flameWheel) * Cooking.FlameStep, 0f, 1f);
        if (!Held)
            _byMouse = false;
        _spin = _spin * SpinKeep + _swept;
        _swept = 0f;
        var stirring = Stirring;
        float heat = Heat, done = Doneness, scorch = Scorch;
        Cooking.Step(ref heat, Flame, Lit, ref done, ref scorch, SinceStir, stirring, 1f, Speed);
        (Heat, Doneness, Scorch) = (heat, done, scorch);
        SinceStir = stirring ? 0 : Math.Min(SinceStir + 1, Cooking.Untended);
        if (Flaring > 0)
        {
            if (--Flaring == 0)
            {
                Caught = true;
                Done = true;
                return;
            }
        }
        else if (Cooking.Flares(Heat, Fatty, Rand.NextFloat()))
            Flaring = Cooking.FlareSteps;
        if (Doneness >= 1f)
            Done = true;
    }

    public override void Finish(Simulation sim)
    {
        var p = WriteBack(sim);
        if (p == null)
            return;
        if (Caught)
        {
            Cooking.Catch(sim, Pot);
            return;
        }
        // Cooked through: bank the fire to keep it warm without catching.
        p.Flame = Math.Min(p.Flame, (Cooking.SteamingAt + Cooking.SimmerAt) * 0.5f);
        var burnt = p.Scorch > 0.35f ? " It's caught on the bottom: you can taste it." : p.Scorch > 0.12f ? " A little caught on the bottom." : "";
        sim.Messages.Tell(Worker, MessageKind.Info, $"The stew's cooked through; you bank the fire to keep it warm.{burnt}", sim.World.Get<Thing>(Pot).Cell);
    }

    public override void Interrupted(Simulation sim) => WriteBack(sim);

    private StewPot? WriteBack(Simulation sim)
    {
        if (!Cooking.IsPot(sim, Pot))
            return null;
        var p = Cooking.PotOf(sim, Pot);
        if (p.Empty)
            return null;
        (p.Heat, p.Flame, p.Doneness, p.Scorch) = (Heat, Flame, Doneness, Scorch);
        // A flare still going when the cook steps away carries on over the pot.
        p.FlareUntil = Flaring > 0 ? sim.Clock.Tick + Flaring : 0;
        return p;
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Pot);
        w.Bool(Lit);
        w.Float(Heat);
        w.Float(Flame);
        w.Float(Doneness);
        w.Float(Scorch);
        w.Int(SinceStir);
        w.Int(_flameDir);
        w.Float(Speed);
        w.Bool(Fatty);
        w.Int(Flaring);
        w.Bool(Caught);
        w.Int(Lidded);
        w.Bool(_byMouse);
        w.Float(_swept);
        w.Float(_spin);
        w.Int(_flameWheel);
    }

    protected override void ReadState(SaveReader r)
    {
        Pot = r.Entity();
        Lit = r.Bool();
        Heat = r.Float();
        Flame = r.Float();
        Doneness = r.Float();
        Scorch = r.Float();
        SinceStir = r.Int();
        _flameDir = Math.Sign(r.Int());
        Speed = r.Version >= 23 ? Math.Clamp(r.Float(), 1f, 1f + Cooking.ChoppedSpeed) : 1f; // chopping came with format 23
        if (r.Version >= 30) // grease fires came with format 30
        {
            Fatty = r.Bool();
            Flaring = Math.Clamp(r.Int(), 0, Cooking.FlareSteps);
            Caught = r.Bool();
            Lidded = Math.Clamp(r.Int(), 0, 1000);
        }
        if (r.Version >= 43) // stirred by the mouse going round (format 43)
        {
            _byMouse = r.Bool();
            _swept = Math.Clamp(r.Float(), 0f, 100f);
            _spin = Math.Clamp(r.Float(), 0f, 100f);
            _flameWheel = Math.Sign(r.Int());
        }
    }
}

/// <summary>
/// Seasoning the stew a pinch at a time (F), tasting as you go (Space), done when you step away. A taste says how it
/// sits against what this pot wants, roughly: the less practised the cook, the less they can tell.
/// Nothing takes salt back out.
/// </summary>
public sealed class SeasonSession : WorkSession
{
    /// <summary>Steps between pinches: a hand can't drop them faster.</summary>
    public const int PinchSteps = 12;

    public Entity Pot { get; set; } = Entity.Null;

    public float Seasoning { get; private set; }

    /// <summary>Hidden: what this pot wants. Read only within the session, for the taste.</summary>
    internal float Target { get; private set; }

    public int Pinches { get; private set; }

    /// <summary>What the last taste said, and at which pinch (-1: not tasted yet).</summary>
    public string Taste { get; private set; } = "";
    public int TastedAt { get; private set; } = -1;

    private int _sincePinch = PinchSteps;

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Pot) && sim.World.TryGet<Thing>(Pot, out var t) ? t.Cell : null;

    public override Skill Skill => Skill.Cooking;
    public override float Xp => 10f;
    public override string Title => "Seasoning the stew";

    internal static SeasonSession For(Simulation sim, Entity pot)
    {
        var p = Cooking.PotOf(sim, pot);
        return new SeasonSession { Pot = pot, Seasoning = p.Seasoning, Target = p.SeasonTarget };
    }

    public override bool StillValid(Simulation sim) =>
        Cooking.IsPot(sim, Pot) && !Cooking.PotOf(sim, Pot).Empty && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Pot);

    protected override void Press()
    {
        if (_sincePinch < PinchSteps)
            return;
        _sincePinch = 0;
        Seasoning += Cooking.Pinch * Rand.Range(0.7f, 1.3f) * (1f + Hands.Tremor);
        Pinches++;
    }

    /// <summary>A taste. Stepping away (Esc) is "that'll do": it keeps the seasoning just as finishing would.</summary>
    protected override void Act()
    {
        // How well they can tell: a practised tongue is within a few hundredths, a raw one far off.
        var noise = Rand.Range(-1f, 1f) * 0.1f / Hands.Tolerance;
        Taste = Cooking.TasteWord(Seasoning - Target + noise);
        TastedAt = Pinches;
    }

    protected override void Step()
    {
        if (_sincePinch < PinchSteps)
            _sincePinch++;
    }

    public override void Finish(Simulation sim) => WriteBack(sim);

    public override void Interrupted(Simulation sim) => WriteBack(sim);

    private void WriteBack(Simulation sim)
    {
        if (Cooking.IsPot(sim, Pot) && Cooking.PotOf(sim, Pot) is { Empty: false } p)
            p.Seasoning = Seasoning;
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Pot);
        w.Float(Seasoning);
        w.Float(Target);
        w.Int(Pinches);
        w.String(Taste);
        w.Int(TastedAt);
        w.Int(_sincePinch);
    }

    protected override void ReadState(SaveReader r)
    {
        Pot = r.Entity();
        Seasoning = r.Float();
        Target = r.Float();
        Pinches = r.Int();
        Taste = r.String();
        TastedAt = r.Int();
        _sincePinch = r.Int();
        if (r.Version < 8)
            r.Int(); // the last A/D, from when tasting was D
    }
}
