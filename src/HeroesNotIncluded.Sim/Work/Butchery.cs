using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// The kitchen's butcher block (docs/DESIGN.md, "Cook"): what the carter brings whole is dressed on
/// it. A side of pork is jointed with the cleaver (<see cref="ButcherSession"/>), a bird plucked
/// (<see cref="PluckSession"/>). Done cleanly it gives its full yield; hacked through bone or torn
/// in the plucking, less. An AI cook dresses what's in the larder without the games, at an average
/// hand (<see cref="DressAll"/>).
/// </summary>
public static class Butchery
{
    public static bool IsBlock(Simulation sim, Entity e) =>
        !e.IsNull && sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Building is { ButcherBlock: true } && !sim.World.Has<Construction>(e);

    /// <summary>A side to joint on the cook (in hand, else in the pack), or Entity.Null.</summary>
    public static Entity Side(Simulation sim, Entity cook) => AvatarSystem.Supply(sim, cook, d => d.Joints != null);

    /// <summary>A bird to pluck on the cook (in hand, else in the pack), or Entity.Null.</summary>
    public static Entity Bird(Simulation sim, Entity cook) => AvatarSystem.Supply(sim, cook, d => d.Plucks != null);

    /// <summary>What the work key does at a butcher block: joint a side, else pluck a bird. Null if there's neither on you.</summary>
    public static string? StepAt(Simulation sim, Entity cook, Entity block)
    {
        if (!IsBlock(sim, block))
            return null;
        if (Side(sim, cook) is { IsNull: false } side)
            return $"Joint the {sim.World.Get<Thing>(side).Def.Label}";
        if (Bird(sim, cook) is { IsNull: false } bird)
            return $"Pluck the {sim.World.Get<Thing>(bird).Def.Label}";
        return null;
    }

    /// <summary>Joint or pluck at the block, or say what it's for.</summary>
    public static void Start(Simulation sim, Entity cook, Entity block)
    {
        if (!IsBlock(sim, block) || !AvatarSystem.InReach(sim, sim.World.Get<Position>(cook).Cell, block))
            return;
        if (Side(sim, cook) is { IsNull: false } side)
            WorkSystem.Start(sim, cook, ButcherSession.For(sim, block, side));
        else if (Bird(sim, cook) is { IsNull: false } bird)
            WorkSystem.Start(sim, cook, PluckSession.For(sim, block, bird));
        else
            sim.Messages.Add(MessageKind.Info, "Bring a side of pork or a bird from the larder: they're jointed and plucked here.", sim.World.Get<Thing>(block).Cell);
    }

    /// <summary>
    /// One of <paramref name="whole"/> dressed into <paramref name="count"/> of what it comes apart
    /// into, as far gone as it was: back where it came from (hands or pack). The made stack, or
    /// Entity.Null if it couldn't be.
    /// </summary>
    internal static Entity Dress(Simulation sim, Entity cook, Entity whole, DressProps props, int count)
    {
        var w = sim.World;
        if (!w.IsAlive(whole) || !sim.Defs.TryThing(props.Into, out var into))
            return Entity.Null;
        var rot = FoodPoisoning.RotOf(sim, whole);
        ref var hands = ref w.Get<Carrying>(cook);
        var inHand = hands.Item == whole && w.Get<Stack>(whole).Count <= 1;
        if (inHand)
            hands.Item = Entity.Null;
        w.Destroy(sim.TakeFromStack(whole, 1));
        var made = sim.CreateItem(into, Math.Max(1, count));
        if (w.Has<Rot>(made))
            w.Get<Rot>(made).Progress = rot;
        if (inHand)
            w.Get<Carrying>(cook).Item = made;
        else
            Inventory.Stow(sim, cook, made);
        return made;
    }

    /// <summary>What an average hand gets from it: most of it, not all.</summary>
    public const float AverageShare = 0.8f;

    /// <summary>
    /// An AI cook at the block dresses everything whole in <paramref name="store"/> (the larder),
    /// without the games, at an average hand: what comes off goes back in. How many it did.
    /// </summary>
    public static int DressAll(Simulation sim, Entity store)
    {
        var w = sim.World;
        var done = 0;
        foreach (var item in sim.StoredIn(store).ToList())
        {
            var def = w.Get<Thing>(item).Def;
            if ((def.Joints ?? def.Plucks) is not { } props || !sim.Defs.TryThing(props.Into, out var into))
                continue;
            var count = w.Get<Stack>(item).Count;
            var rot = FoodPoisoning.RotOf(sim, item);
            w.Remove<Stored>(item);
            sim.DestroyThing(item);
            var made = sim.CreateItem(into, Math.Max(1, (int)MathF.Round(props.Yield * AverageShare)) * count);
            if (w.Has<Rot>(made))
                w.Get<Rot>(made).Progress = rot;
            var left = sim.Store(store, made);
            if (!left.IsNull)
                sim.PlaceItemNear(left, sim.LocationOf(store));
            done += count;
        }
        return done;
    }
}

/// <summary>
/// Jointing a side at the butcher block: carry the cleaver along it (A/D, or where the mouse
/// points) and bring it down (F, or a click: on the move, it comes down once it's there). On a
/// joint, it parts clean; anywhere else it hacks through bone, and what's splintered is lost. A
/// blow struck before the hand's settled goes wide (<see cref="Aim"/>), and an unsteady hand wider
/// still. All the joints parted, or the side hacked about enough, it's done: meat by how clean.
/// </summary>
public sealed class ButcherSession : WorkSession
{
    /// <summary>Where the joints lie along the side, 0..1.</summary>
    public static readonly float[] JointsAt = [0.16f, 0.33f, 0.5f, 0.67f, 0.84f];

    /// <summary>How wide a settled, steady blow strays from where the cleaver's held.</summary>
    public const float Stray = 0.01f;

    /// <summary>Steps to lift the cleaver again after a blow: one before then waits for it.</summary>
    public const int RecoverySteps = 24;

    /// <summary>How near a joint a blow parts it, along the side, before practice widens it.</summary>
    public const float Window = 0.035f;

    /// <summary>Blows before the side's done however it's gone.</summary>
    public const int MostBlows = 12;

    public Entity Block { get; set; } = Entity.Null;
    public Entity Side { get; set; } = Entity.Null;
    public string Label { get; private set; } = "";

    /// <summary>Which joints are parted.</summary>
    public bool[] Parted { get; private set; } = new bool[JointsAt.Length];

    /// <summary>Blows that hacked through bone.</summary>
    public int Hacks { get; private set; }

    public int Blows { get; private set; }

    /// <summary>The last blow, for the drawing: 1 clean on a joint, 0 through bone.</summary>
    public int LastMark { get; private set; } = -1;

    /// <summary>Where the cleaver is along the side, 0..1.</summary>
    public float Cleaver => _aim.At;

    /// <summary>Where the last blow came down, 0..1 (for the drawing: it may be off where it was held).</summary>
    public float LastAt { get; private set; } = -1f;

    /// <summary>The hand's at rest over the side: a blow now lands where it's held, as near as the hand's steady.</summary>
    public bool Settled => _aim.Settled(StepsDone);

    /// <summary>The cleaver's up again after the last blow.</summary>
    public bool Ready => _recovery <= 0;

    private Aim _aim = new(0.05f);
    private int _dir, _recovery;

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Block) && sim.World.TryGet<Thing>(Block, out var t) ? t.Cell : null;
    public override Skill Skill => Skill.Cooking;
    public override float Xp => 15f;
    public override string Title => $"Jointing the {Label}";

    internal static ButcherSession For(Simulation sim, Entity block, Entity side) =>
        new() { Block = block, Side = side, Label = sim.World.Get<Thing>(side).Def.Label };

    public override bool StillValid(Simulation sim) =>
        Butchery.IsBlock(sim, Block) && sim.World.IsAlive(Side) && AvatarSystem.SupplyItem(sim, Worker, e => e == Side) == Side
        && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Block);

    protected override void Axis(int value) => _dir = value;

    /// <summary>A click strikes where it points: at once if the cleaver's there, else as soon as it's carried there.</summary>
    protected override void Clicked(float? u, float? v)
    {
        if (u is { } x && WorkSession.Toward(_aim.At, x, Aim.PerStep) != 0)
            _aim.Queued = true;
        else
            Press();
    }

    protected override void Press()
    {
        if (Done || !Ready)
            return;
        _recovery = RecoverySteps;
        // The blow comes down near where it's held: nearer the steadier the hand, and the more it's settled.
        var at = LastAt = Math.Clamp(_aim.At + (Rand.NextFloat() * 2f - 1f) * (Stray + Hands.Tremor * 0.1f) * _aim.Unsteadiness(StepsDone), 0f, 1f);
        var nearest = -1;
        var best = float.MaxValue;
        for (var i = 0; i < JointsAt.Length; i++)
            if (!Parted[i] && MathF.Abs(JointsAt[i] - at) < best)
                (nearest, best) = (i, MathF.Abs(JointsAt[i] - at));
        var window = Window * Hands.Tolerance;
        if (nearest >= 0 && best <= window)
        {
            Parted[nearest] = true;
            LastMark = 1;
            Sample(1f - best / window * 0.4f);
        }
        else
        {
            Hacks++;
            LastMark = 0;
            Sample(0.15f);
        }
        Blows++;
        if (Parted.All(p => p) || Blows >= MostBlows)
            Done = true;
    }

    protected override void Step()
    {
        if (Done)
            return;
        if (_recovery > 0)
            _recovery--;
        if (_aim.Step(_dir, PointU, StepsDone))
        {
            if (Ready)
                Press();
            else
                _aim.Queued = true; // still lifting it: the blow waits
        }
    }

    public override void Finish(Simulation sim)
    {
        if (!sim.World.IsAlive(Side) || sim.World.Get<Thing>(Side).Def.Joints is not { } props)
            return;
        var parted = Parted.Count(p => p) / (float)Parted.Length;
        var count = (int)MathF.Round(props.Yield * (0.45f + 0.35f * parted + 0.2f * Quality));
        var made = Butchery.Dress(sim, Worker, Side, props, count);
        if (made.IsNull)
            return;
        var w = sim.World;
        var hacked = Hacks switch { 0 => "", 1 => " One blow went through bone.", _ => $" {Hacks} blows went through bone: there's waste." };
        sim.Messages.Add(MessageKind.Info, $"Jointed the {Label}: {w.Get<Stack>(made).Count} {w.Get<Thing>(made).Def.Label}.{hacked}", w.Get<Thing>(Block).Cell);
    }

    protected override void Cloned() => Parted = (bool[])Parted.Clone();

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Block);
        w.Entity(Side);
        w.String(Label);
        foreach (var p in Parted)
            w.Bool(p);
        w.Int(Hacks);
        w.Int(Blows);
        w.Int(LastMark);
        _aim.Write(w);
        w.Int(_dir);
        w.Int(_recovery);
        w.Float(LastAt);
    }

    protected override void ReadState(SaveReader r)
    {
        Block = r.Entity();
        Side = r.Entity();
        Label = r.String();
        for (var i = 0; i < Parted.Length; i++)
            Parted[i] = r.Bool();
        Hacks = Math.Clamp(r.Int(), 0, MostBlows);
        Blows = Math.Clamp(r.Int(), 0, MostBlows);
        LastMark = Math.Clamp(r.Int(), -1, 1);
        if (r.Version >= 43) // the cleaver carried, not swept (format 43)
        {
            _aim = Aim.Read(r);
            _dir = Math.Sign(r.Int());
            _recovery = Math.Clamp(r.Int(), 0, RecoverySteps);
            LastAt = r.Float();
        }
    }
}

/// <summary>
/// Plucking a bird at the butcher block: hold F to grip and pull, let go to take the handful; or
/// with the mouse, press on the bird and drag away, the pull as hard as it's dragged (it tightens
/// no quicker than a held F does, so a fling can't leap past a tear). The harder it's pulled the
/// more comes away, and the skin pulls taut; past where this bird's skin gives (never shown, a
/// little different every bird) it tears. A grip gives out after a handful, so short firm pulls
/// are the trick. A torn bird gives less.
/// </summary>
public sealed class PluckSession : WorkSession
{
    /// <summary>How fast the pull tightens while F is held, a step: taut in about a second.</summary>
    public const float PullPerStep = 1f / (1.1f * SimClock.TicksPerSecond);

    /// <summary>Feathers out a step, per unit of pull: about six good pulls to a bird.</summary>
    public const float PluckPerStep = 1f / 170f;

    /// <summary>Where the skin reads as pulling taut (it tears somewhere past this).</summary>
    public const float TautAt = 0.7f;

    public Entity Block { get; set; } = Entity.Null;
    public Entity Bird { get; set; } = Entity.Null;
    public string Label { get; private set; } = "";

    /// <summary>How hard it's being pulled now, 0..1+.</summary>
    public float Pull { get; private set; }

    /// <summary>How much is plucked, 0..1.</summary>
    public float Plucked { get; private set; }

    public int Tears { get; private set; }

    /// <summary>The last handful, for the drawing: 1 a good one, 0 timid, -1 a tear.</summary>
    public int LastMark { get; private set; } = 2;

    /// <summary>Hidden: where this bird's skin gives. Read only here.</summary>
    internal float TearAt { get; private set; } = 1f;

    private bool _rolled;

    public bool Taut => Pull >= TautAt;

    /// <summary>How much a grip brings away before it gives out (it tapers over the last of it).</summary>
    public const float Handful = 0.25f;

    /// <summary>How hard a drag pulls, per the picture's width dragged.</summary>
    public const float DragPull = 1.7f;

    /// <summary>Pressed on the bird with the mouse: the pull's how far it's dragged from here.</summary>
    public bool Gripping { get; private set; }
    public float GripU { get; private set; }
    public float GripV { get; private set; }

    // Plucked this grip; and a grip torn loose, waiting for the button to come up.
    private float _inHand;
    private bool _lost;

    /// <summary>The mouse's button pressed: a grip, where it's pressed (off the bird's picture, at its middle). It pulls only as it's dragged.</summary>
    protected override void Clicked(float? u, float? v) => (Gripping, GripU, GripV, _lost) = (true, u ?? 0.5f, v ?? 0.5f, false);

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Block) && sim.World.TryGet<Thing>(Block, out var t) ? t.Cell : null;
    public override Skill Skill => Skill.Cooking;
    public override float Xp => 12f;
    public override string Title => $"Plucking the {Label}";

    internal static PluckSession For(Simulation sim, Entity block, Entity bird) =>
        new() { Block = block, Bird = bird, Label = sim.World.Get<Thing>(bird).Def.Label };

    public override bool StillValid(Simulation sim) =>
        Butchery.IsBlock(sim, Block) && sim.World.IsAlive(Bird) && AvatarSystem.SupplyItem(sim, Worker, e => e == Bird) == Bird
        && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Block);

    protected override void Step()
    {
        if (!_rolled)
        {
            _rolled = true;
            TearAt = 0.85f + 0.25f * Rand.NextFloat();
        }
        if (Done)
            return;
        if (Held && !_lost)
        {
            var rise = PullPerStep * (1f + Hands.Tremor);
            if (Gripping)
            {
                // Dragged: toward how far, never quicker than a held F tightens; eased back as fast as it's let.
                var (u, v) = (PointU ?? GripU, PointV ?? GripV);
                var want = DragPull * MathF.Sqrt((u - GripU) * (u - GripU) + (v - GripV) * (v - GripV));
                var shake = (Rand.NextFloat() * 2f - 1f) * Hands.Tremor * 0.01f;
                Pull = Math.Max(0f, Pull + Math.Clamp(want - Pull, -rise * 3f, rise) + shake);
            }
            else
                Pull += rise;
            var left = Math.Clamp((Handful - _inHand) / (Handful * 0.3f), 0f, 1f);
            var came = Math.Min(1f - Plucked, Pull * PluckPerStep * left);
            Plucked += came;
            _inHand += came;
            if (Pull >= TearAt)
            {
                Tears++;
                LastMark = -1;
                Sample(0.1f);
                Pull = 0f;
                TearAt = 0.85f + 0.25f * Rand.NextFloat();
                _inHand = 0f;
                _lost = Gripping; // torn loose from the hand: grip it again (F, held, tightens on as it did)
            }
        }
        else if (!Held)
        {
            if (Pull > 0f)
            {
                // Let go: the handful's taken. A firm one is the trick; a timid one is slow work.
                LastMark = Pull >= 0.5f ? 1 : 0;
                Sample(Pull >= 0.5f ? 1f : 0.7f);
                Pull = 0f;
            }
            (_inHand, Gripping, _lost) = (0f, false, false);
        }
        if (Plucked >= 1f)
            Done = true;
    }

    public override void Finish(Simulation sim)
    {
        if (!sim.World.IsAlive(Bird) || sim.World.Get<Thing>(Bird).Def.Plucks is not { } props)
            return;
        var count = props.Yield - (Tears >= 2 ? 1 : 0);
        var made = Butchery.Dress(sim, Worker, Bird, props, count);
        if (made.IsNull)
            return;
        var w = sim.World;
        var torn = Tears switch { 0 => " Not a tear in it.", 1 => " The skin tore once.", _ => " The skin's torn: there's less of it." };
        sim.Messages.Add(MessageKind.Info, $"Plucked the {Label}: {w.Get<Stack>(made).Count} {w.Get<Thing>(made).Def.Label}.{torn}", w.Get<Thing>(Block).Cell);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Block);
        w.Entity(Bird);
        w.String(Label);
        w.Float(Pull);
        w.Float(Plucked);
        w.Int(Tears);
        w.Int(LastMark);
        w.Float(TearAt);
        w.Bool(_rolled);
        w.Bool(Gripping);
        w.Float(GripU);
        w.Float(GripV);
        w.Float(_inHand);
        w.Bool(_lost);
    }

    protected override void ReadState(SaveReader r)
    {
        Block = r.Entity();
        Bird = r.Entity();
        Label = r.String();
        Pull = Math.Clamp(r.Float(), 0f, 2f);
        Plucked = Math.Clamp(r.Float(), 0f, 1f);
        Tears = Math.Clamp(r.Int(), 0, 100);
        LastMark = Math.Clamp(r.Int(), -1, 2);
        TearAt = Math.Clamp(r.Float(), 0.5f, 1.5f);
        _rolled = r.Bool();
        if (r.Version >= 43) // the pull dragged by the mouse, a grip giving out after a handful (format 43)
        {
            Gripping = r.Bool();
            GripU = Math.Clamp(r.Float(), 0f, 1f);
            GripV = Math.Clamp(r.Float(), 0f, 1f);
            _inHand = Math.Clamp(r.Float(), 0f, 1f);
            _lost = r.Bool();
        }
    }
}
