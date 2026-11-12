using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// The inn's upkeep at its stations: washing dirty crockery at the tub (<see cref="WashSession"/>)
/// and knocking a fresh keg into a dry cask (<see cref="TapSession"/>). What the work key offers
/// at each, and starting it.
/// </summary>
public static class BarWork
{
    /// <summary>A cask with no more than this many servings left can take a new keg; any fuller and you'd waste what's in it.</summary>
    public const int TapBelow = 10;

    public static bool IsTub(Simulation sim, Entity e) =>
        !e.IsNull && sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Building is { WashTub: true } && !sim.World.Has<Construction>(e);

    /// <summary>The dirty crockery someone has on them, in hand first.</summary>
    public static Entity DirtyOn(Simulation sim, Entity who) => AvatarSystem.Supply(sim, who, Vessels.IsDirty);

    /// <summary>The keg someone is holding, if it's what this cask takes.</summary>
    public static Entity KegFor(Simulation sim, Entity who, Entity cask)
    {
        var w = sim.World;
        var held = w.Get<Carrying>(who).Item;
        return w.IsAlive(held) && w.Get<Thing>(cask).Def.Building?.Cask is { } props && w.Get<Thing>(held).Def.Name == props.Keg ? held : Entity.Null;
    }

    /// <summary>What the work key does at a wash tub or cask for this person, or null.</summary>
    public static string? StepAt(Simulation sim, Entity who, Entity station)
    {
        if (station.IsNull || !sim.World.IsAlive(station))
            return null;
        if (IsTub(sim, station) && DirtyOn(sim, who) is { IsNull: false } dirty)
            return $"Wash the {sim.World.Get<Thing>(dirty).Def.Label}{(sim.World.Get<Stack>(dirty).Count > 1 ? "s" : "")}";
        if (sim.World.Get<Thing>(station).Def.Building?.Cask != null && !KegFor(sim, who, station).IsNull)
            return PourSession.ServingsLeft(sim, station) <= TapBelow ? "Knock in the new keg" : "The cask isn't low enough to change yet";
        if (Hospitality.Lodging.IsUnmade(sim, station))
            return "Make the bed";
        return null;
    }

    public static void Start(Simulation sim, Entity who, Entity station)
    {
        var w = sim.World;
        if (!AvatarSystem.InReach(sim, w.Get<Position>(who).Cell, station))
            return;
        var cell = w.Get<Thing>(station).Cell;
        if (Hospitality.Lodging.IsUnmade(sim, station))
        {
            WorkSystem.Start(sim, who, new Hospitality.MakeBedSession { Bed = station });
            return;
        }
        if (IsTub(sim, station))
        {
            if (DirtyOn(sim, who) is { IsNull: false } dirty)
                WorkSystem.Start(sim, who, WashSession.For(sim, station, dirty));
            return;
        }
        if (w.Get<Thing>(station).Def.Building?.Cask == null || KegFor(sim, who, station).IsNull)
            return;
        if (PourSession.ServingsLeft(sim, station) > TapBelow)
        {
            sim.Messages.Add(MessageKind.Info, $"There's still a good deal in the {w.Get<Thing>(station).Label}: change the keg when it's low.", cell);
            return;
        }
        WorkSystem.Start(sim, who, new TapSession { Cask = station });
    }

    /// <summary>A keg knocked into a cask: full again, less what sprayed out of a fumbled tap. The keg's used up.</summary>
    public static void Refill(Simulation sim, Entity cask, Entity keg, int wasted)
    {
        var w = sim.World;
        var props = w.Get<Thing>(cask).Def.Building!.Cask!;
        var drawn = Math.Clamp(wasted, 0, props.Servings);
        if (w.Has<CaskDrawn>(cask))
            w.Get<CaskDrawn>(cask).Drawn = drawn;
        else
            w.Add(cask, new CaskDrawn { Drawn = drawn });
        foreach (var e in w.Each<Carrying>())
            if (w.Get<Carrying>(e).Item == keg)
                w.Get<Carrying>(e).Item = Entity.Null;
        if (w.IsAlive(keg))
            w.Destroy(keg);
    }
}

/// <summary>
/// Washing up at the tub, a vessel at a time. Each comes out of the water with grime on it in
/// patches (different each time); move the brush over it (WASD) and hold F to scrub, and the
/// grime under the brush comes off. When none's left it shines, and the next comes up. Stop early
/// and what's washed stays washed.
/// </summary>
public sealed class WashSession : WorkSession
{
    /// <summary>The grime is kept on a grid over the piece, this many cells across and down.</summary>
    public const int GridW = 12, GridH = 9;

    /// <summary>How far the brush moves a step, across the piece (0..1).</summary>
    public const float BrushSpeed = 0.02f;

    /// <summary>How far round the brush scrubs, and how much grime a step of scrubbing takes off a cell under it.</summary>
    public const float BrushRadius = 0.2f, ScrubRate = 0.16f;

    /// <summary>Grime a cell can have left and still count clean.</summary>
    public const float CleanEnough = 0.06f;

    /// <summary>Steps a clean piece shines before the next comes up.</summary>
    public const int ShineSteps = 36;

    public Entity Tub { get; set; } = Entity.Null;

    /// <summary>The kind being washed, and how many of them there were when it began.</summary>
    public string DirtyDef { get; set; } = "";
    public int Count { get; set; }

    public int Washed { get; private set; }

    /// <summary>The brush, 0..1 across and down the piece.</summary>
    public float BrushX { get; private set; } = 0.5f;
    public float BrushY { get; private set; } = 0.5f;

    public bool Scrubbing => Held;

    /// <summary>Steps of shine left on a piece just washed (0: none).</summary>
    public int Shine { get; private set; }

    private float[] _grime = new float[GridW * GridH];
    private int _dx, _dy;

    /// <summary>The grime on the piece at a grid cell, 0..1.</summary>
    public float GrimeAt(int x, int y) => _grime[y * GridW + x];

    /// <summary>The shape of the piece on the grid: a tankard's tall body, a bowl's wide one. Grime only sits on it.</summary>
    public bool OnPiece(int x, int y)
    {
        float u = (x + 0.5f) / GridW, v = (y + 0.5f) / GridH;
        return Bowl
            ? (u - 0.5f) * (u - 0.5f) / 0.2f + (v - 0.55f) * (v - 0.55f) / 0.14f <= 1f
            : u is > 0.22f and < 0.78f && v is > 0.08f and < 0.95f;
    }

    /// <summary>What's being washed is a bowl (else a tankard).</summary>
    public bool Bowl => DirtyDef.Contains("Bowl");

    /// <summary>How clean the piece in hand is, 0..1.</summary>
    public float Clean
    {
        get
        {
            float sum = 0f, cells = 0f;
            for (var y = 0; y < GridH; y++)
                for (var x = 0; x < GridW; x++)
                    if (OnPiece(x, y))
                    {
                        sum += _grime[y * GridW + x];
                        cells++;
                    }
            return cells == 0 ? 1f : 1f - sum / cells;
        }
    }

    public override Skill Skill => Skill.Cooking;
    public override float Xp => 3f;
    public override string Title => "Washing up";

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Tub) && sim.World.TryGet<Thing>(Tub, out var t) ? t.Cell : null;

    internal static WashSession For(Simulation sim, Entity tub, Entity dirty) =>
        new() { Tub = tub, DirtyDef = sim.World.Get<Thing>(dirty).Def.Name, Count = sim.World.Get<Stack>(dirty).Count };

    public override bool StillValid(Simulation sim) =>
        BarWork.IsTub(sim, Tub) && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Tub)
        && !AvatarSystem.Supply(sim, Worker, d => d.Name == DirtyDef).IsNull;

    protected internal override void Begin() => Soil();

    protected override void Cloned() => _grime = (float[])_grime.Clone();

    /// <summary>A fresh piece out of the water: a few patches of grime, heavier in their middles.</summary>
    private void Soil()
    {
        Array.Clear(_grime);
        var patches = Rand.NextInt(3) + 3;
        for (var k = 0; k < patches; k++)
        {
            float cx = Rand.Range(0.15f, 0.85f) * GridW, cy = Rand.Range(0.15f, 0.85f) * GridH, r = Rand.Range(1.4f, 2.8f);
            for (var y = 0; y < GridH; y++)
                for (var x = 0; x < GridW; x++)
                {
                    var d = MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy)) / r;
                    if (d < 1f && OnPiece(x, y))
                        _grime[y * GridW + x] = Math.Min(1f, _grime[y * GridW + x] + (1f - d * d) * Rand.Range(0.7f, 1f));
                }
        }
    }

    protected override void Axis(int value) => _dx = value;

    protected override void Axis2(int value) => _dy = value;

    protected override void Step()
    {
        BrushX = Math.Clamp(BrushX + _dx * BrushSpeed, 0f, 1f);
        BrushY = Math.Clamp(BrushY + _dy * BrushSpeed, 0f, 1f);
        if (Shine > 0)
        {
            // Admiring it a moment; then the next one out of the water.
            if (--Shine == 0 && Washed < Count)
                Soil();
            return;
        }
        if (!Held)
            return;
        var any = false;
        for (var y = 0; y < GridH; y++)
            for (var x = 0; x < GridW; x++)
            {
                ref var g = ref _grime[y * GridW + x];
                if (g <= 0f)
                    continue;
                // The piece is square to the eye: across and down measure alike.
                float dx = (x + 0.5f) / GridW - BrushX, dy = (y + 0.5f) / GridH - BrushY;
                var d = MathF.Sqrt(dx * dx + dy * dy) / BrushRadius;
                if (d < 1f)
                    g = Math.Max(0f, g - ScrubRate * (1f - d * 0.5f) * Hands.Precision);
                if (g > CleanEnough)
                    any = true;
            }
        if (any)
            return;
        Washed++;
        Shine = ShineSteps;
        if (Washed >= Count)
            Done = true;
    }

    public override void Finish(Simulation sim) => Apply(sim);

    public override void Interrupted(Simulation sim) => Apply(sim);

    /// <summary>The washed ones come out of the dirty stack and back clean, into the washer's hands or pack.</summary>
    private void Apply(Simulation sim)
    {
        var w = sim.World;
        var left = Washed;
        Washed = 0;
        var clean = sim.Defs.Thing(sim.Defs.Thing(DirtyDef).WashesInto!);
        var done = 0;
        while (left > 0 && AvatarSystem.Supply(sim, Worker, d => d.Name == DirtyDef) is { IsNull: false } dirty)
        {
            var n = Math.Min(left, w.Get<Stack>(dirty).Count);
            w.Destroy(Inventory.TakeFromPerson(sim, Worker, dirty, n));
            left -= n;
            done += n;
        }
        if (done == 0)
            return;
        Inventory.Stow(sim, Worker, sim.CreateItem(clean, done));
        sim.Messages.Add(MessageKind.Info, $"Washed {done} {(done == 1 ? clean.Label : clean.Label + "s")}: clean and shining.", w.Get<Position>(Worker).Cell);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Tub);
        w.String(DirtyDef);
        w.Int(Count);
        w.Int(Washed);
        w.Float(BrushX);
        w.Float(BrushY);
        w.Int(Shine);
        w.Int(_dx);
        w.Int(_dy);
        foreach (var g in _grime)
            w.Float(g);
    }

    protected override void ReadState(SaveReader r)
    {
        Tub = r.Entity();
        DirtyDef = r.String();
        Count = r.Int();
        Washed = r.Int();
        BrushX = r.Float();
        BrushY = r.Float();
        Shine = r.Int();
        _dx = Math.Sign(r.Int());
        _dy = Math.Sign(r.Int());
        _grime = new float[GridW * GridH];
        for (var i = 0; i < _grime.Length; i++)
            _grime[i] = r.Float();
    }
}

/// <summary>
/// Knocking a new keg into the cask: the mallet swings, and each blow that lands as it comes down
/// drives the tap home. Three good ones seat it. A blow off the beat jars it and ale sprays out of
/// the bung: servings lost before the cask's even full.
/// </summary>
public sealed class TapSession : WorkSession
{
    /// <summary>Good blows that seat the tap.</summary>
    public const int BlowsNeeded = 3;

    /// <summary>Servings that spray out with each fumbled blow.</summary>
    public const int SprayPerMiss = 4;

    /// <summary>Seconds a swing of the mallet takes.</summary>
    public const float Swing = 1.1f;

    public Entity Cask { get; set; } = Entity.Null;

    public int Blows { get; private set; }
    public int Misses { get; private set; }

    /// <summary>The mallet's swing, 0 (raised) to 1 (on the tap).</summary>
    public float Mallet => Triangle(StepsDone, Swing);

    /// <summary>The mallet's coming down onto the tap: the moment to strike.</summary>
    public bool OnTheTap => Mallet > 0.82f;

    public int Wasted => Misses * SprayPerMiss;

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Cask) && sim.World.TryGet<Thing>(Cask, out var t) ? t.Cell : null;

    public override Skill Skill => Skill.Cooking;
    public override float Xp => 15f;
    public override string Title => "Tapping a keg";

    public override bool StillValid(Simulation sim) =>
        sim.World.IsAlive(Cask) && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Cask) && !BarWork.KegFor(sim, Worker, Cask).IsNull;

    protected override void Press()
    {
        if (OnTheTap && Rand.NextFloat() < 0.75f + 0.25f * Hands.Precision)
            Blows++;
        else
            Misses++;
        if (Blows >= BlowsNeeded || Misses >= 6)
            Done = true;
    }

    protected override void Step() { }

    public override void Finish(Simulation sim)
    {
        var keg = BarWork.KegFor(sim, Worker, Cask);
        if (keg.IsNull)
            return;
        BarWork.Refill(sim, Cask, keg, Wasted);
        var at = sim.World.Get<Thing>(Cask).Cell;
        if (Misses > 0)
            sim.Mess.Spill(sim, Hospitality.MessKind.SpilledAle, sim.World.Get<Position>(Worker).Cell, 0.4f * Misses);
        sim.Messages.Add(MessageKind.Info, Misses == 0 ? "The tap's in clean, and the cask is full."
            : $"The tap's in, after a soaking: {Wasted} servings down your front and on the floor.", at);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Cask);
        w.Int(Blows);
        w.Int(Misses);
    }

    protected override void ReadState(SaveReader r)
    {
        Cask = r.Entity();
        Blows = r.Int();
        Misses = r.Int();
    }
}

/// <summary>The work key at a wash tub or a cask with a keg in hand (<see cref="BarWork.Start"/>).</summary>
public sealed record BarWorkCommand(Entity Avatar, Entity Station) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (!InventoryCommand.Ready(sim, Avatar) || !sim.World.IsAlive(Station) || !sim.World.Has<Thing>(Station))
            return;
        BarWork.Start(sim, Avatar, Station);
    }
}
