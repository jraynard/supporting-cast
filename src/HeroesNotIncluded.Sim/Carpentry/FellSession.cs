using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Carpentry;

/// <summary>
/// A tree felled by hand, as a woodsman would (docs/DESIGN.md, "Carpenter"): it goes where it's
/// aimed, if the cuts are right, and onto whoever's standing there if they aren't.
/// <list type="bullet">
/// <item><b>Aim</b>: the trunk seen from above. A/D (or the mouse round the trunk) turns where it's
/// to fall. Every tree leans a little somewhere (<see cref="Lean"/>, plain to see), and pulls that
/// way if the cuts give it the chance. The first blow sets the aim.</item>
/// <item><b>Notch</b>: on the side it's to fall to, cut a third of the way in (the chalk mark).
/// F held raises the axe, let go it strikes, as deep as it was raised (as the hew,
/// <see cref="SawSession"/>). Too shallow and the notch doesn't steer it; too deep and it's
/// weakened the hinge.</item>
/// <item><b>Back cut</b>: Space goes round to the other side (<see cref="Backing"/>). Blows there
/// thin the hinge left between the cuts, and when it's thin enough (<see cref="HingeFalls"/>) the
/// tree goes. A blow through the hinge leaves nothing to steer it: it falls with its lean.</item>
/// </list>
/// The fall is the sim's (<see cref="Felling"/>): a moment's creak, the "Timber!", and the trunk
/// lies along where it fell, hurting anyone it lands on. How true it fell is the quality.
/// </summary>
public sealed class FellSession : WorkSession
{
    /// <summary>Degrees the aim turns a step on the keys.</summary>
    public const float TurnPerStep = 1.5f;

    /// <summary>How fast holding F raises the axe, per step (as the hew).</summary>
    public const float RaisePerStep = SawSession.RaisePerStep;

    /// <summary>How deep (of the trunk's width, 1) a blow raised all the way goes into a pine; harder woods less (<see cref="Girth"/>).</summary>
    public const float DeepestBlow = 0.14f;

    /// <summary>The notch's chalk mark: a third of the way in.</summary>
    public const float NotchMark = 0.33f;

    /// <summary>A notch must be at least this deep to go round for the back cut.</summary>
    public const float LeastNotch = 0.08f;

    /// <summary>The deepest a notch can be cut (past it the axe only chips at the hinge).</summary>
    public const float DeepestNotch = 0.6f;

    /// <summary>The hinge this thin (of the trunk's width) and it goes; under none, it's cut through.</summary>
    public const float HingeFalls = 0.1f;

    /// <summary>How far (degrees) a fall wanders at worst for the hand's shake, and for cuts that don't steer it.</summary>
    public const float LeastWander = 3f, MostWander = 22f;

    /// <summary>The tree.</summary>
    public Entity Tree { get; set; } = Entity.Null;

    /// <summary>Where it's aimed to fall, degrees (0 east, 90 south: the map's +X and +Y).</summary>
    public float Aim { get; set; }

    /// <summary>Which way the tree leans (degrees), and how hard it pulls (0..1). Set from the tree as the work starts.</summary>
    public float Lean { get; set; }
    public float LeanPull { get; set; }

    /// <summary>How hard the wood takes the axe (the tree def's <see cref="Defs.FellingProps.Girth"/>).</summary>
    public float Girth { get; set; } = 1f;

    /// <summary>Gone round for the back cut (true), or still at the notch.</summary>
    public bool Backing { get; private set; }

    /// <summary>How deep the notch is, and the back cut, of the trunk's width.</summary>
    public float Notch { get; private set; }
    public float Back { get; private set; }

    /// <summary>The wood left between the cuts: what steers the fall. Under zero, cut through.</summary>
    public float Hinge => 1f - Notch - Back;

    /// <summary>How high the axe is raised now (0 down, 1 as high as it goes).</summary>
    public float Raised { get; private set; }

    /// <summary>Blows struck: for the sound and the chips.</summary>
    public int Blows { get; private set; }

    /// <summary>The aim's set (a blow's landed): it can't be turned now.</summary>
    public bool AimSet => Notch > 0f;

    /// <summary>Which way it went (degrees), once it's gone; and whether the hinge was cut through.</summary>
    public float FellDir { get; private set; }
    public bool Through { get; private set; }

    private bool _raising;
    private int _turn;
    private int _goRound;

    public override Skill Skill => Skill.Crafting;
    public override float Xp => 30f;
    public override string Title => Backing ? "Felling: the back cut" : AimSet ? "Felling: the notch" : "Felling: where should it fall?";

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Tree) && sim.World.TryGet<Thing>(Tree, out var t) ? t.Cell : null;

    /// <summary>
    /// The way a tree leans and how hard, worked from where it stands: the same on every peer and
    /// every look, and no secret (you can see a tree lean).
    /// </summary>
    public static (float Dir, float Pull) LeanOf(int3 cell)
    {
        var h = SimRandom.Hash(cell.X, cell.Y, (uint)(cell.Z * 7919 + 17));
        var dir = h % 360u;
        var pull = 0.25f + (h >> 9) % 1000u / 1000f * 0.6f;
        return (dir, pull);
    }

    public override bool StillValid(Simulation sim)
    {
        var w = sim.World;
        return w.IsAlive(Tree) && w.TryGet<Thing>(Tree, out var t) && t.Def.Felling != null && !w.Has<Falling>(Tree)
            && AvatarSystem.InReach(sim, w.Get<Position>(Worker).Cell, Tree) && Crafting.ToolSpeed(sim, Worker, Felling.AxeKind) > 0f;
    }

    protected override void Axis(int value) => _turn = value;

    /// <summary>The mouse round the trunk aims it, before the first blow: up the panel is north.</summary>
    protected override void Pointed(float u, float v)
    {
        if (AimSet)
            return;
        var (x, y) = (u * 2f - 1f, v * 2f - 1f);
        if (x * x + y * y < 0.04f)
            return; // too near the middle to say
        Aim = Wrap360(DetMath.Atan2(y, x) * 180f / MathF.PI);
    }

    /// <summary>Space (or a right-click): go round for the back cut.</summary>
    protected override void Act() => _goRound = 1;

    protected override void Step()
    {
        if (Done)
            return;
        if (!AimSet && _turn != 0)
            Aim = Wrap360(Aim + _turn * TurnPerStep);
        if (_goRound > 0 && !_raising)
        {
            _goRound = 0;
            if (!Backing && Notch >= LeastNotch)
                Backing = true;
            return;
        }
        _goRound = 0;
        if (Held)
        {
            Raised = Math.Min(1f, Raised + RaisePerStep);
            _raising = true;
            return;
        }
        if (!_raising)
            return;
        _raising = false;
        var depth = Raised * DeepestBlow / Math.Max(0.3f, Girth) * (1f + (Rand.NextFloat() * 2f - 1f) * (0.08f + Hands.Tremor * 0.5f));
        Raised = 0f;
        Blows++;
        if (!Backing)
        {
            Notch = Math.Min(DeepestNotch, Notch + depth);
            return;
        }
        Back = Math.Min(1.2f - Notch, Back + depth);
        if (Hinge <= HingeFalls)
            Go();
    }

    /// <summary>
    /// How well the notch steers, 0..1: best at the mark, less short of it (nothing to fall into),
    /// less past it too (the hinge is weak before the back cut's begun).
    /// </summary>
    public static float NotchSteer(float notch) =>
        notch <= NotchMark ? Math.Clamp(notch / NotchMark, 0f, 1f) : Math.Max(0.35f, 1f - (notch - NotchMark) * 2.5f);

    /// <summary>The hinge gives: which way it goes, and how true that is.</summary>
    private void Go()
    {
        Through = Hinge < 0f;
        FellDir = FallDir(Aim, Lean, LeanPull, Notch, Through, Hands.Precision, Rand.NextFloat() * 2f - 1f);
        Sample(Math.Clamp(1f - MathF.Abs(Wrap180(FellDir - Aim)) / 90f, 0f, 1f));
        Done = true;
    }

    /// <summary>
    /// Where a tree goes when its hinge gives: where it was aimed, pulled toward its lean by as much
    /// as the notch doesn't hold it, and shaken by <paramref name="shake"/> (−1..1) more the less it
    /// steers. Cut through the hinge, nothing steers it: it goes with its lean.
    /// </summary>
    public static float FallDir(float aim, float lean, float leanPull, float notch, bool through, float precision, float shake)
    {
        var steer = through ? 0f : NotchSteer(notch) * (0.85f + 0.15f * Math.Clamp(precision, 0f, 1f));
        var wander = shake * (LeastWander + (MostWander - LeastWander) * (1f - steer));
        return through ? Wrap360(lean + wander) : Wrap360(aim + Wrap180(lean - aim) * leanPull * (1f - steer) + wander);
    }

    public static float Wrap360(float deg) => ((deg % 360f) + 360f) % 360f;

    public static float Wrap180(float deg) => Wrap360(deg + 180f) - 180f;

    public override void Finish(Simulation sim)
    {
        var w = sim.World;
        if (!w.IsAlive(Tree) || w.Has<Falling>(Tree))
            return;
        w.Add(Tree, new Falling { Dir = FellDir, At = sim.Clock.Tick, By = Worker, Through = Through });
        Speech.Barks.Say(sim, Worker, "felling.timber", Speech.Volume.Shout, "Timber!");
        var off = MathF.Abs(Wrap180(FellDir - Aim));
        var how = Through ? "You've cut through the hinge: it goes its own way!"
            : off < 10f ? "It goes just where you meant."
            : off < 35f ? "It goes, near enough where you meant."
            : "It twists off the hinge and goes wide.";
        sim.Messages.Tell(Worker, MessageKind.Info, how, w.Get<Thing>(Tree).Cell);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Tree);
        w.Float(Aim);
        w.Float(Lean);
        w.Float(LeanPull);
        w.Float(Girth);
        w.Bool(Backing);
        w.Float(Notch);
        w.Float(Back);
        w.Float(Raised);
        w.Int(Blows);
        w.Float(FellDir);
        w.Bool(Through);
        w.Bool(_raising);
        w.Int(_turn);
        w.Int(_goRound);
    }

    protected override void ReadState(SaveReader r)
    {
        Tree = r.Entity();
        Aim = Wrap360(r.Float());
        Lean = Wrap360(r.Float());
        LeanPull = Math.Clamp(r.Float(), 0f, 1f);
        Girth = Math.Clamp(r.Float(), 0.3f, 3f);
        Backing = r.Bool();
        Notch = Math.Clamp(r.Float(), 0f, DeepestNotch);
        Back = Math.Clamp(r.Float(), 0f, 1.2f);
        Raised = Math.Clamp(r.Float(), 0f, 1f);
        Blows = Math.Max(0, r.Int());
        FellDir = Wrap360(r.Float());
        Through = r.Bool();
        _raising = r.Bool();
        _turn = Math.Sign(r.Int());
        _goRound = Math.Sign(r.Int());
    }
}
