using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Scenarios.Lessons;

/// <summary>
/// Where a lesson has got to: the step to do now (a <see cref="LessonStepDef.Id"/>), what to point
/// at for it (a station, a customer; Entity.Null for nothing), and whether it's all done.
/// </summary>
public readonly record struct LessonStage(string Step, Entity Point, bool Done = false);

/// <summary>
/// A lesson under way, kept by the game, not the sim: when it began and what the player had then,
/// so "done" means done since. Stations are found once, the first time they're asked for.
/// </summary>
public sealed class LessonRun
{
    public LessonRun(Simulation sim, Entity me, TradeDef trade, LessonDef def)
    {
        Trade = trade;
        Def = def;
        StartTick = sim.Clock.Tick;
        var w = sim.World;
        if (sim.PackOf(me) is { IsNull: false } pack)
            foreach (var item in sim.StoredIn(pack))
                Had.Add(item);
        if (w.Get<Carrying>(me).Item is { IsNull: false } held)
            Had.Add(held);
        foreach (var item in Had)
            if (w.TryGet<Thing>(item, out var t))
                CountsAtStart[t.Def.Name] = CountsAtStart.GetValueOrDefault(t.Def.Name) + (w.TryGet<Stack>(item, out var s) ? s.Count : 1);
        Workplace = Practice.Workplace(sim, me);
        if (!Workplace.IsNull)
            Prices = Perception.For(sim, me).Prices(Workplace);
    }

    public TradeDef Trade { get; }
    public LessonDef Def { get; }

    /// <summary>Progress is kept under this (<c>Blacksmith/sharpen</c>).</summary>
    public string Key => KeyOf(Trade, Def);

    public static string KeyOf(TradeDef trade, LessonDef def) => $"{trade.Name}/{def.Id}";

    public long StartTick { get; }

    /// <summary>What the player had in hand and pack when it began.</summary>
    public HashSet<Entity> Had { get; } = [];

    /// <summary>How many of each thing (by def name) the player had in hand and pack when it began.</summary>
    public Dictionary<string, int> CountsAtStart { get; } = [];

    /// <summary>The sign as it read when it began.</summary>
    public List<PostedPrice> Prices { get; } = [];

    public Entity Workplace { get; }

    /// <summary>Parts of a lesson already done that can't be read back from the world (the smelting before the prices).</summary>
    public HashSet<string> Passed { get; } = [];

    private readonly Dictionary<string, Entity> _stations = [];

    /// <summary>The workplace's station of this kind, Entity.Null if it has none.</summary>
    public Entity Station(Simulation sim, string def)
    {
        if (_stations.TryGetValue(def, out var found) && sim.World.IsAlive(found))
            return found;
        found = Entity.Null;
        foreach (var e in sim.World.Query<PartOf>())
            if (sim.World.Get<PartOf>(e).Building == Workplace && sim.World.TryGet<Thing>(e, out var t) && t.Def.Name == def)
            {
                found = e;
                break;
            }
        _stations[def] = found;
        return found;
    }
}

/// <summary>
/// A lesson's logic: which step the player is on, read from what they know (<see cref="Perception"/>
/// and what's on their own person) every time it's asked. Nothing is advanced by a cursor, so a
/// lesson follows the player back when something goes wrong: a blade still chipped after the anvil
/// goes back to the fire, a refused one back to taking the order. Never changes the world.
/// </summary>
public sealed record Lesson(string Trade, string Id, string[] Steps, Func<Simulation, Entity, LessonRun, LessonStage> Stage);

/// <summary>Every trade's lessons, by trade and id (the words are in trades.json's <c>lessons</c>).</summary>
public static class Lessons
{
    public static readonly Lesson[] All = [.. SmithLessons.All];

    public static Lesson? For(string trade, string id) => Array.Find(All, l => l.Trade == trade && l.Id == id);

    /// <summary>Where the run has got to; a lesson with no logic (not written yet) never finishes.</summary>
    public static LessonStage Stage(Simulation sim, Entity me, LessonRun run)
    {
        if (!sim.World.IsAlive(me) || For(run.Trade.Name, run.Def.Id) is not { } lesson)
            return new LessonStage(run.Def.Steps[0].Id, Entity.Null);
        return lesson.Stage(sim, me, run);
    }

    // ---------------- Shared readings ----------------

    /// <summary>How many of a thing the player has in hand and pack.</summary>
    public static int OnMe(Simulation sim, Entity me, string def)
    {
        if (!sim.Defs.TryThing(def, out var d))
            return 0;
        var n = 0;
        foreach (var stack in Inventory.CarriedOf(sim, me, d))
            n += sim.World.TryGet<Stack>(stack, out var s) ? s.Count : 1;
        return n;
    }

    /// <summary>The thing someone handed the player to see to (<see cref="Entrusted"/>), Entity.Null if none.</summary>
    public static Entity EntrustedBy(Simulation sim, Entity me, Entity owner)
    {
        var w = sim.World;
        var held = w.Get<Carrying>(me).Item;
        if (w.IsAlive(held) && w.TryGet<Entrusted>(held, out var h) && h.Owner == owner)
            return held;
        if (sim.PackOf(me) is { IsNull: false } pack)
            foreach (var item in sim.StoredIn(pack))
                if (w.TryGet<Entrusted>(item, out var en) && en.Owner == owner)
                    return item;
        return Entity.Null;
    }

    /// <summary>Chips and bends, as the holder reads them (<see cref="PerceptionView.Condition"/>).</summary>
    public static bool LooksChipped(string condition) =>
        condition.Contains("chip") || condition.Contains("bent") || condition.Contains("breaking");

    /// <summary>An edge that isn't keen, as the holder reads it.</summary>
    public static bool LooksDull(string condition) => condition.Contains("dull") || condition.Contains("blunt");

    /// <summary>The kind of work session the player is in, if any.</summary>
    public static WorkSession? Session(Simulation sim, Entity me) => sim.World.TryGet<WorkSession>(me, out var s) ? s : null;
}
