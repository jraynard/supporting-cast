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
/// Jointing a side at the butcher block: the cleaver sweeps back and forth along it, and F brings
/// it down. On a joint, it parts clean; anywhere else it hacks through bone, and what's splintered
/// is lost. All the joints parted, or the side hacked about enough, it's done: meat by how clean.
/// </summary>
public sealed class ButcherSession : WorkSession
{
    /// <summary>Where the joints lie along the side, 0..1.</summary>
    public static readonly float[] JointsAt = [0.16f, 0.33f, 0.5f, 0.67f, 0.84f];

    /// <summary>Seconds the cleaver takes to sweep along and back.</summary>
    public const float Sweep = 2.4f;

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
    public float Cleaver => Triangle(StepsDone, Sweep);

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Block) && sim.World.TryGet<Thing>(Block, out var t) ? t.Cell : null;
    public override Skill Skill => Skill.Cooking;
    public override float Xp => 15f;
    public override string Title => $"Jointing the {Label}";

    internal static ButcherSession For(Simulation sim, Entity block, Entity side) =>
        new() { Block = block, Side = side, Label = sim.World.Get<Thing>(side).Def.Label };

    public override bool StillValid(Simulation sim) =>
        Butchery.IsBlock(sim, Block) && sim.World.IsAlive(Side) && AvatarSystem.SupplyItem(sim, Worker, e => e == Side) == Side
        && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Block);

    protected override void Press()
    {
        if (Done)
            return;
        var at = Cleaver;
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

    protected override void Step() { }

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
    }
}

/// <summary>
/// Plucking a bird at the butcher block: hold F to grip and pull, let go to take the handful. The
/// harder it's pulled the more comes away, and the skin pulls taut; past where this bird's skin
/// gives (never shown, a little different every bird) it tears. Short firm pulls are the trick. A
/// torn bird gives less.
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
        if (Held)
        {
            Pull += PullPerStep * (1f + Hands.Tremor);
            Plucked = Math.Min(1f, Plucked + Pull * PluckPerStep);
            if (Pull >= TearAt)
            {
                Tears++;
                LastMark = -1;
                Sample(0.1f);
                Pull = 0f;
                TearAt = 0.85f + 0.25f * Rand.NextFloat();
            }
        }
        else if (Pull > 0f)
        {
            // Let go: the handful's taken. A firm one is the trick; a timid one is slow work.
            LastMark = Pull >= 0.5f ? 1 : 0;
            Sample(Pull >= 0.5f ? 1f : 0.7f);
            Pull = 0f;
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
    }
}
