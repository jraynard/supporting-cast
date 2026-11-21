using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Scenarios.Lessons;

/// <summary>
/// The tutorial's first chapter, the same for every trade (trades.json's "basics"): walk a few
/// steps, get to the workplace if you're not in it, then open the pack, the shop window and the
/// controls card, and (a workplace the carter supplies) send for something from the carter. Walking is
/// read from where the player stands, an order from the carter's book; the windows are the game's to know,
/// so it marks each in <see cref="LessonRun.Passed"/> ("pack", "shop", "controls") as it's opened.
/// </summary>
public static class BasicsLesson
{
    /// <summary>The trade a lesson every trade shares is filed under.</summary>
    public const string AnyTrade = "*";

    /// <summary>How far to walk before moving counts as learned.</summary>
    public const int WalkCells = 3;

    public static readonly Lesson Lesson = new(AnyTrade, Tutorial.BasicsId, ["move", "workplace", "pack", "shop", "carter", "controls"], Stage);

    public static LessonStage Stage(Simulation sim, Entity me, LessonRun run)
    {
        var w = sim.World;
        var cell = w.Get<Position>(me).Cell;
        if (Maps.Map.Distance(cell, run.StartCell) >= WalkCells)
            run.Passed.Add("moved");
        if (!run.Passed.Contains("moved"))
            return new LessonStage("move", Entity.Null);
        if (!run.Workplace.IsNull && Buildings.At(sim, cell) == run.Workplace)
            run.Passed.Add("there");
        if (!run.Workplace.IsNull && !run.Passed.Contains("there"))
            return new LessonStage("workplace", run.Workplace);
        if (!run.Passed.Contains("pack"))
            return new LessonStage("pack", Entity.Null);
        // The shop's sign and prices: a trade with no workplace (the musician) has none to learn.
        if (!run.Workplace.IsNull && !run.Passed.Contains("shop"))
            return new LessonStage("shop", Entity.Null);
        // What the shop needs comes to order: send for one thing, from the shop window.
        if (Shops.CarterOrders.Of(sim).All.Any(o => o.By == me))
            run.Passed.Add("carter");
        if (!run.Workplace.IsNull && !run.Passed.Contains("carter") && Shops.CarterOrders.Catalogue(sim, me).Count > 0)
            return new LessonStage("carter", Entity.Null);
        if (!run.Passed.Contains("controls"))
            return new LessonStage("controls", Entity.Null);
        return new LessonStage("controls", Entity.Null, Done: true);
    }
}
