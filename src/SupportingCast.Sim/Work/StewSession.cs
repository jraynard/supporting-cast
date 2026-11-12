using SupportingCast.Sim.Components;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Work;

/// <summary>
/// Tending the stew (<see cref="Cooking"/>). D feeds the fire under the pot and A banks it; the heat
/// follows slowly, and shows only in how the surface looks. It cooks from a simmer, faster at a
/// boil, but past a simmer it catches on the bottom unless it's stirred (hold F); stirring all the
/// time lets the heat out and slows it. Done when it's cooked through: the fire is banked to keep
/// it warm. Walk away early and the pot carries on as the fire was left.
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

    /// <summary>Steps since the spoon last went round.</summary>
    public int SinceStir { get; private set; } = Cooking.Untended;

    private int _flameDir;

    public Surface Surface => Cooking.SurfaceOf(Heat);
    public bool Stirring => Held;

    /// <summary>Starting to catch: a careful cook can smell it (the bottom's catching at more than a trickle).</summary>
    public bool Catching => Heat > Cooking.SimmerAt && !Held && SinceStir > Cooking.CatchSteps;

    public override Skill Skill => Skill.Cooking;
    public override float Xp => 25f;
    public override string Title => "Tending the stew";

    internal static StewSession For(Simulation sim, Entity pot)
    {
        var p = Cooking.PotOf(sim, pot);
        return new StewSession
        {
            Pot = pot, Lit = Cooking.Lit(sim, pot), Heat = p.Heat, Flame = p.Flame, Doneness = p.Doneness, Scorch = p.Scorch,
        };
    }

    public override bool StillValid(Simulation sim) =>
        Cooking.IsPot(sim, Pot) && !Cooking.PotOf(sim, Pot).Empty && Cooking.Lit(sim, Pot) == Lit
        && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Pot);

    protected override void Axis(int value) => _flameDir = value;

    protected override void Step()
    {
        Flame = Math.Clamp(Flame + _flameDir * Cooking.FlameStep, 0f, 1f);
        float heat = Heat, done = Doneness, scorch = Scorch;
        Cooking.Step(ref heat, Flame, Lit, ref done, ref scorch, SinceStir, Held);
        (Heat, Doneness, Scorch) = (heat, done, scorch);
        SinceStir = Held ? 0 : Math.Min(SinceStir + 1, Cooking.Untended);
        if (Doneness >= 1f)
            Done = true;
    }

    public override void Finish(Simulation sim)
    {
        var p = WriteBack(sim);
        if (p == null)
            return;
        // Cooked through: bank the fire to keep it warm without catching.
        p.Flame = Math.Min(p.Flame, (Cooking.SteamingAt + Cooking.SimmerAt) * 0.5f);
        var burnt = p.Scorch > 0.35f ? " It's caught on the bottom: you can taste it." : p.Scorch > 0.12f ? " A little caught on the bottom." : "";
        sim.Messages.Add(MessageKind.Info, $"The stew's cooked through; you bank the fire to keep it warm.{burnt}", sim.World.Get<Thing>(Pot).Cell);
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
    }
}

/// <summary>
/// Seasoning the stew a pinch at a time (F), tasting as you go (D), done with A. A taste says how it
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
    private int _lastAxis;

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

    protected override void Axis(int value)
    {
        if (value == _lastAxis)
            return;
        _lastAxis = value;
        if (value > 0)
        {
            // How well they can tell: a practised tongue is within a few hundredths, a raw one far off.
            var noise = Rand.Range(-1f, 1f) * 0.1f / Hands.Tolerance;
            Taste = Cooking.TasteWord(Seasoning - Target + noise);
            TastedAt = Pinches;
        }
        else if (value < 0)
            Done = true;
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
        w.Int(_lastAxis);
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
        _lastAxis = Math.Sign(r.Int());
    }
}
