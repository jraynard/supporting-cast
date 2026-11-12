using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Work;

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
/// Washing up at the tub, a vessel at a time: scrub on the beat (press F as the brush comes round)
/// and each is clean in a few good strokes, or in a lot of bad ones. Stop early and what's clean
/// stays clean.
/// </summary>
public sealed class WashSession : WorkSession
{
    /// <summary>Seconds a stroke of the brush takes, round and back.</summary>
    public const float Beat = 0.8f;

    /// <summary>How clean a stroke on the beat gets a vessel, and one off it.</summary>
    public const float GoodStroke = 0.34f, PoorStroke = 0.05f;

    /// <summary>Steps a stroke takes: presses while the brush is still going are lost, so mashing doesn't pay.</summary>
    public const int StrokeSteps = 20;

    public Entity Tub { get; set; } = Entity.Null;

    /// <summary>The kind being washed, and how many of them there were when it began.</summary>
    public string DirtyDef { get; set; } = "";
    public int Count { get; set; }

    public int Washed { get; private set; }

    /// <summary>How clean the one in hand is, 0..1.</summary>
    public float Scrub { get; private set; }

    /// <summary>Where the brush is in its stroke, 0..1..0: a stroke counts when it's near the top.</summary>
    public float Brush => Triangle(StepsDone, Beat);

    public bool OnBeat => Brush > 0.75f;

    /// <summary>The last stroke landed on the beat (for the scene).</summary>
    public bool LastGood { get; private set; }

    private int _sinceStroke = StrokeSteps;

    public override Skill Skill => Skill.Cooking;
    public override float Xp => 3f;
    public override string Title => "Washing up";

    internal static WashSession For(Simulation sim, Entity tub, Entity dirty) =>
        new() { Tub = tub, DirtyDef = sim.World.Get<Thing>(dirty).Def.Name, Count = sim.World.Get<Stack>(dirty).Count };

    public override bool StillValid(Simulation sim) =>
        BarWork.IsTub(sim, Tub) && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Tub)
        && !AvatarSystem.Supply(sim, Worker, d => d.Name == DirtyDef).IsNull;

    protected override void Press()
    {
        if (_sinceStroke < StrokeSteps)
            return;
        _sinceStroke = 0;
        LastGood = OnBeat;
        Scrub += LastGood ? GoodStroke * Hands.Precision : PoorStroke;
        if (Scrub < 1f)
            return;
        Scrub = 0f;
        Washed++;
        if (Washed >= Count)
            Done = true;
    }

    protected override void Step()
    {
        if (_sinceStroke < StrokeSteps)
            _sinceStroke++;
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
        sim.Messages.Add(MessageKind.Info, $"Washed {done} {(done == 1 ? clean.Label : clean.Label + "s")}.", w.Get<Position>(Worker).Cell);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Tub);
        w.String(DirtyDef);
        w.Int(Count);
        w.Int(Washed);
        w.Float(Scrub);
        w.Bool(LastGood);
        w.Int(_sinceStroke);
    }

    protected override void ReadState(SaveReader r)
    {
        Tub = r.Entity();
        DirtyDef = r.String();
        Count = r.Int();
        Washed = r.Int();
        Scrub = r.Float();
        LastGood = r.Bool();
        _sinceStroke = r.Int();
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
