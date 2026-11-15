using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Tinkering;
using static HeroesNotIncluded.Sim.Scenarios.Lessons.Lessons;

namespace HeroesNotIncluded.Sim.Scenarios.Lessons;

/// <summary>
/// The tinker's lessons: a strongbox felt over and picked, a curio looked over, a lock pinned and
/// fitted to a door, and a crossbow fitted together. Read from the box and curio as the tinker
/// knows them (never an armed trap they haven't found), and the locks they've made.
/// </summary>
public static class TinkerLessons
{
    public const string Trade = "Tinker";

    public static readonly Lesson[] All =
    [
        new(Trade, "box", ["wait", "take", "handover", "probe", "pick", "giveback"], Box),
        new(Trade, "curio", ["wait", "take", "handover", "appraise", "giveback"], Curio),
        new(Trade, "lock", ["pin", "fit", "turn"], Lock),
        new(Trade, "crossbow", ["choose", "fit"], Crossbow),
    ];

    /// <summary>A strongbox: felt over for traps, then picked, then back to its owner.</summary>
    public static LessonStage Box(Simulation sim, Entity me, LessonRun run)
    {
        var view = Perception.For(sim, me);
        var bench = run.Station(sim, Tinkers.BenchDef);
        return Customer(sim, me, run, view, ServiceKind.Unlock, box =>
        {
            switch (Session(sim, me))
            {
                case ProbeSession: return new LessonStage("probe", bench);
                case PickSession: return new LessonStage("pick", bench);
            }
            if (view.Strongbox(box) is not { Locked: true } known)
                return null;
            return new LessonStage(known.SearchedByMe ? "pick" : "probe", bench);
        });
    }

    /// <summary>A curio: looked over through the loupe, then back to its owner with a word on its worth.</summary>
    public static LessonStage Curio(Simulation sim, Entity me, LessonRun run)
    {
        var view = Perception.For(sim, me);
        var bench = run.Station(sim, Tinkers.BenchDef);
        return Customer(sim, me, run, view, ServiceKind.Appraise, curio =>
            Session(sim, me) is LoupeSession || view.Curio(curio) is not { Low: not null } ? new LessonStage("appraise", bench) : null);
    }

    /// <summary>A lock pinned at the bench, fitted to the shop's door, and locked with its key.</summary>
    public static LessonStage Lock(Simulation sim, Entity me, LessonRun run)
    {
        var view = Perception.For(sim, me);
        var w = sim.World;
        // A door with a lock of the player's own fitted since they began: lock it.
        foreach (var door in w.Query<LockCut>())
            if (Locksmithing.IsDoor(sim, door) && w.Get<LockCut>(door).Maker == me && !run.Had.Contains(door))
                return new LessonStage("turn", door, Done: view.Lock(door) is { Locked: true });
        if (Session(sim, me) is PinSession)
            return new LessonStage("pin", run.Station(sim, Tinkers.BenchDef));
        if (AvatarSystem.SupplyItem(sim, me, e => Locksmithing.IsLock(sim, e)) is { IsNull: false })
            return new LessonStage("fit", ShopDoor(sim, run));
        return new LessonStage("pin", run.Station(sim, Tinkers.BenchDef));
    }

    /// <summary>A crossbow fitted together at the bench.</summary>
    public static LessonStage Crossbow(Simulation sim, Entity me, LessonRun run)
    {
        var bench = run.Station(sim, Tinkers.BenchDef);
        if (Session(sim, me) is FitSession)
            return new LessonStage("fit", bench);
        return new LessonStage("choose", bench, Done: !Made(sim, me, run, d => d.Name == "Crossbow").IsNull);
    }

    /// <summary>A door of the shop with no lock on it yet, nearest the bench.</summary>
    private static Entity ShopDoor(Simulation sim, LessonRun run)
    {
        var w = sim.World;
        var bench = run.Station(sim, Tinkers.BenchDef);
        var at = w.TryGet<Thing>(bench, out var b) ? b.Cell : default;
        return w.Query<PartOf>().Where(e => w.Get<PartOf>(e).Building == run.Workplace && Locksmithing.IsDoor(sim, e) && !w.Has<LockCut>(e))
            .OrderBy(e => Maps.Map.Distance(w.Get<Thing>(e).Cell, at)).FirstOrDefault(Entity.Null);
    }
}
