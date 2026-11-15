using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Scenarios.Lessons.Lessons;

namespace HeroesNotIncluded.Sim.Scenarios.Lessons;

/// <summary>
/// The cook's lessons: a stew from the larder to a diner at the hatch, a roast on the spit, and
/// bread kneaded, proved and baked. Read from the pot, spit and oven as the cook sees them (never a
/// pot's rot or the seasoning it wants) and what's in their hands.
/// </summary>
public static class KitchenLessons
{
    public const string Trade = "Cook";

    /// <summary>What the kitchen's fires burn, and where it's kept.</summary>
    private const string Fuel = "Wood", LogBasket = "LogBasket";

    public static readonly Lesson[] All =
    [
        new(Trade, "stew", ["larder", "fill", "fuel", "stir", "season", "wash", "bowl", "ladle", "hatch", "served"], Stew),
        new(Trade, "roast", ["meat", "fuel", "spit", "turn"], Roast),
        new(Trade, "bread", ["flour", "knead", "prove", "fuel", "oven"], Bread),
    ];

    /// <summary>A stew, from the larder to a diner at the hatch.</summary>
    public static LessonStage Stew(Simulation sim, Entity me, LessonRun run)
    {
        var view = Perception.For(sim, me);
        var w = sim.World;
        if (view.Orders().Any(o => o.Kind == ServiceKind.Meal && o.PostedTick >= run.StartTick && o.Status == OrderStatus.Done))
            return new LessonStage("served", Entity.Null, Done: true);
        var pot = run.Station(sim, "StewPot");
        var hatch = Hatch.Of(sim, run.Workplace);
        switch (Session(sim, me))
        {
            case StewSession: return new LessonStage("stir", pot);
            case SeasonSession:
                run.Passed.Add("seasoned");
                return new LessonStage("season", pot);
        }
        // A bowl of it in hand: up on the hatch; one up there already: wait for a diner to take it.
        if (AvatarSystem.Supply(sim, me, d => d.Cooked) is { IsNull: false })
        {
            run.Passed.Add("dished");
            return new LessonStage("hatch", hatch);
        }
        if (run.Passed.Contains("dished") || (!hatch.IsNull && view.InContainer(hatch).Any(d => w.Get<Thing>(d).Def.Cooked)))
            return new LessonStage("served", hatch); // up, and waiting on a diner
        var food = AvatarSystem.Supply(sim, me, d => Cooking.Takes(sim, pot, d));
        if (view.Pot(pot) is not { } p) // out of sight: food in hand goes to the pot, else to the larder for some
            return food.IsNull ? new LessonStage("larder", run.Station(sim, "Larder")) : new LessonStage("fill", pot);
        if (p.Empty || (p.Cooked && p.Bowls == 0))
            return food.IsNull
                ? new LessonStage("larder", run.Station(sim, "Larder"))
                : new LessonStage("fill", pot);
        if (!p.Lit)
            return Feed(sim, me, FireBeside(sim, pot), Fuel, run.Station(sim, LogBasket));
        if (!p.Cooked)
            return new LessonStage("stir", pot);
        if (!run.Passed.Contains("seasoned"))
            return new LessonStage("season", pot);
        var bowl = Cooking.BowlOf(sim, pot);
        if (AvatarSystem.Supply(sim, me, d => d.Name == bowl).IsNull)
            return BarWork.DirtyOn(sim, me).IsNull
                ? new LessonStage("bowl", run.Station(sim, "Shelf", pot))
                : new LessonStage("wash", run.Station(sim, "WashTub"));
        return new LessonStage("ladle", pot);
    }

    /// <summary>A joint on the spit, turned golden all round, and lifted.</summary>
    public static LessonStage Roast(Simulation sim, Entity me, LessonRun run)
    {
        var view = Perception.For(sim, me);
        var spit = run.Station(sim, "Spit");
        if (Session(sim, me) is RoastSession)
            return new LessonStage("turn", spit);
        if (Gained(sim, me, run, Roasting.Dish))
            return new LessonStage("turn", Entity.Null, Done: true);
        var known = view.Spit(spit);
        var on = known is { Pieces: > 0 };
        if (!on && Roasting.Joint(sim, me).IsNull)
            return new LessonStage("meat", run.Station(sim, "Larder"));
        if (known is { Lit: false })
            return Feed(sim, me, FireBeside(sim, spit), Fuel, run.Station(sim, LogBasket));
        return new LessonStage(on ? "turn" : "spit", spit);
    }

    /// <summary>Flour kneaded to dough, proved, and baked by smell.</summary>
    public static LessonStage Bread(Simulation sim, Entity me, LessonRun run)
    {
        var view = Perception.For(sim, me);
        var oven = run.Station(sim, "Oven");
        var table = run.Station(sim, "PrepTable");
        switch (Session(sim, me))
        {
            case KneadSession: return new LessonStage("knead", table);
            case BakeSession: return new LessonStage("oven", oven);
        }
        if (Gained(sim, me, run, Baking.Bread))
            return new LessonStage("oven", Entity.Null, Done: true);
        if (view.Oven(oven) is { Loaves: > 0, Pie: false })
            return new LessonStage("oven", oven); // in already: take it out on the golden crust
        var dough = AvatarSystem.Supply(sim, me, d => d.Name == Baking.DoughDef);
        if (!dough.IsNull)
        {
            if (view.Dough(dough) is "slack" or "rising")
                return new LessonStage("prove", Entity.Null);
            if (view.Oven(oven) is { Lit: false })
                return Feed(sim, me, oven, Fuel, run.Station(sim, LogBasket));
            return new LessonStage("oven", oven);
        }
        return OnMe(sim, me, Baking.Flour) >= Baking.FlourPerDough
            ? new LessonStage("knead", table)
            : new LessonStage("flour", run.Station(sim, "Larder"));
    }

    /// <summary>The fire a pot or spit hangs over (the hearth beside it), lit or not; Entity.Null if there's none.</summary>
    private static Entity FireBeside(Simulation sim, Entity station)
    {
        var w = sim.World;
        if (!w.TryGet<Thing>(station, out var t))
            return Entity.Null;
        foreach (var d in Maps.Map.Neighbors8)
        {
            var c = new int3(t.Cell.X + d.X, t.Cell.Y + d.Y, t.Cell.Z);
            if (sim.Map.InBounds(c) && sim.Map.Structure[sim.Map.Idx(c)] is { IsNull: false } s
                && w.Get<Thing>(s).Def.Building is { Fuel: not null, HeatOutput: > 0 })
                return s;
        }
        return Entity.Null;
    }
}
