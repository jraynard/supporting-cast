using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Scenarios.Lessons.Lessons;

namespace HeroesNotIncluded.Sim.Scenarios.Lessons;

/// <summary>
/// The blacksmith's lessons: a dull blade to the grindstone, a chipped one through the forge,
/// something new from bar to edge, and a bloom smelted from ore with the prices after. Each step is
/// read the way <c>Bots.BotPlayer</c> decides its next move, from the order, the blade's condition
/// as its holder sees it, and the piece in the tongs.
/// </summary>
public static class SmithLessons
{
    public const string Trade = "Blacksmith";

    private static readonly string[] RepairSteps = ["wait", "take", "handover", "mend", "fuel", "heat", "hammer", "grind", "giveback"];

    public static readonly Lesson[] All =
    [
        new(Trade, "sharpen", RepairSteps, Repair),
        new(Trade, "mend", RepairSteps, Repair),
        new(Trade, "forge", ["choose", "fuel", "heat", "hammer", "quench", "grind"], Forge),
        new(Trade, "smelt", ["ore", "charcoal", "smelt", "fuel", "fire", "beat", "prices"], Smelt),
    ];

    /// <summary>A customer's blade, from their order to their paying for it.</summary>
    public static LessonStage Repair(Simulation sim, Entity me, LessonRun run)
    {
        var view = Perception.For(sim, me);
        return Customer(sim, me, run, view, ServiceKind.Repair, blade =>
        {
            if (Tongs(sim, me, run, view) is { } working)
                return working;
            var condition = view.Condition(blade) ?? "";
            if (LooksChipped(condition))
                return Cold(sim, me, run, view) ?? new LessonStage("mend", run.Station(sim, "Forge"));
            if (LooksDull(condition))
                return new LessonStage("grind", run.Station(sim, "Grindstone"));
            return null;
        });
    }

    /// <summary>Something new: chosen at the forge, worked to shape, quenched, and an edge put on it.</summary>
    public static LessonStage Forge(Simulation sim, Entity me, LessonRun run)
    {
        var view = Perception.For(sim, me);
        if (Tongs(sim, me, run, view) is { } working)
            return working;
        if (Made(sim, me, run, d => d.Weapon != null || d.Apparel != null) is { IsNull: false } made)
        {
            var edged = Combat.WeaponWear.Edged(sim.World.Get<Thing>(made).Def);
            return edged && LooksDull(view.Condition(made) ?? "")
                ? new LessonStage("grind", run.Station(sim, "Grindstone"))
                : new LessonStage("grind", Entity.Null, Done: true);
        }
        return Cold(sim, me, run, view) ?? new LessonStage("choose", run.Station(sim, "Forge"));
    }

    /// <summary>Ore to a bloom to bars, then the price sign.</summary>
    public static LessonStage Smelt(Simulation sim, Entity me, LessonRun run)
    {
        var view = Perception.For(sim, me);
        if (!run.Passed.Contains("smelted"))
        {
            if (Tongs(sim, me, run, view) is { } working)
                return working;
            if (Gained(sim, me, run, "IronBar"))
                run.Passed.Add("smelted");
            else
            {
                if (sim.Defs.AllRecipes.FirstOrDefault(r => r.Bloom) is not { } recipe)
                    return new LessonStage("smelt", run.Station(sim, "Forge"));
                var ore = recipe.Ingredients[0];
                if (OnMe(sim, me, ore.Def) < ore.Count)
                    return new LessonStage("ore", run.Station(sim, "Chest"));
                if (recipe.Ingredients.Count > 1 && recipe.Ingredients[1] is var fuel && OnMe(sim, me, fuel.Def) < fuel.Count)
                    return new LessonStage("charcoal", run.Station(sim, "FuelBin"));
                return Cold(sim, me, run, view) ?? new LessonStage("smelt", run.Station(sim, "Forge"));
            }
        }
        var now = view.Prices(run.Workplace);
        var changed = now.Count != run.Prices.Count || now.Where((p, i) => p != run.Prices[i]).Any();
        return new LessonStage("prices", Entity.Null, Done: changed);
    }

    /// <summary>
    /// The step for the piece in the player's tongs (or the game they're in at a station), null if
    /// they've neither: the fire while it's dark, the anvil while it glows, the tub once shaped.
    /// </summary>
    private static LessonStage? Tongs(Simulation sim, Entity me, LessonRun run, PerceptionView view)
    {
        var piece = view.Piece(me);
        var bloom = piece is { Bloom: not null };
        switch (Session(sim, me))
        {
            case BloomSession:
                return new LessonStage("fire", run.Station(sim, "Forge"));
            case HeatSession:
                return new LessonStage("heat", run.Station(sim, "Forge"));
            case HammerSession:
                return new LessonStage(bloom ? "beat" : "hammer", run.Station(sim, "Anvil"));
            case QuenchSession:
                return new LessonStage("quench", run.Station(sim, "QuenchTub"));
            case SharpenSession:
                return new LessonStage("grind", run.Station(sim, "Grindstone"));
        }
        if (piece is not { } p)
            return null;
        if (!p.Hot && Cold(sim, me, run, view) is { } feed)
            return feed;
        if (bloom)
            return p.Hot ? new LessonStage("beat", run.Station(sim, "Anvil")) : new LessonStage("fire", run.Station(sim, "Forge"));
        if (!p.Hot)
            return new LessonStage("heat", run.Station(sim, "Forge"));
        return p.Shaped ? new LessonStage("quench", run.Station(sim, "QuenchTub")) : new LessonStage("hammer", run.Station(sim, "Anvil"));
    }

    /// <summary>The forge gone out: charcoal from the bin, then into the fire. Null while it burns.</summary>
    private static LessonStage? Cold(Simulation sim, Entity me, LessonRun run, PerceptionView view)
    {
        var forge = run.Station(sim, "Forge");
        if (forge.IsNull || view.FuelIn(forge) is not <= 0f)
            return null;
        return new LessonStage("fuel", OnMe(sim, me, "Charcoal") > 0 ? forge : run.Station(sim, "FuelBin"));
    }
}
