using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Scenarios.Lessons.Lessons;

namespace HeroesNotIncluded.Sim.Scenarios.Lessons;

/// <summary>
/// The innkeeper's lessons: an ale poured and handed over, a named drink mixed at the bar, and a
/// party sent off on a quest. Read from the orders the innkeeper heard, what's in their hands, and
/// the pour or mix in progress.
/// </summary>
public static class InnLessons
{
    public const string Trade = "Innkeeper";

    public static readonly Lesson[] All =
    [
        new(Trade, "ale", ["wait", "tankard", "pour", "give"], Ale),
        new(Trade, "mixed", ["tankard", "mix"], Mixed),
        new(Trade, "quest", ["wait", "offer"], Quest),
    ];

    /// <summary>A thirsty customer's ale: a clean tankard, the pour, and into their hand.</summary>
    public static LessonStage Ale(Simulation sim, Entity me, LessonRun run)
    {
        var view = Perception.For(sim, me);
        var w = sim.World;
        var orders = view.Orders().Where(o => o.Kind is ServiceKind.Drink or ServiceKind.MixedDrink && o.PostedTick >= run.StartTick).ToList();
        var cask = run.Station(sim, "AleCask");
        if (orders.Any(o => o.Status == OrderStatus.Done))
            return new LessonStage("give", Entity.Null, Done: true);
        if (Session(sim, me) is PourSession)
            return new LessonStage("pour", cask);
        if (orders.Cast<KnownOrder?>().FirstOrDefault(o => o!.Value.Status == OrderStatus.Open) is not { } open)
            return new LessonStage("wait", Entity.Null);
        if (w.Get<Carrying>(me).Item is { IsNull: false } held && w.Get<Thing>(held).Def.Drink)
            return new LessonStage("give", open.Poster);
        return Tankard(sim, me, run, cask) ?? new LessonStage("pour", cask);
    }

    /// <summary>A named drink mixed at the bar: its adds (in your pack), a clean tankard, and the cues.</summary>
    public static LessonStage Mixed(Simulation sim, Entity me, LessonRun run)
    {
        var w = sim.World;
        var bar = run.Station(sim, "BarCounter");
        if (Session(sim, me) is MixSession)
            return new LessonStage("mix", bar);
        if (AvatarSystem.SupplyItem(sim, me, e => !run.Had.Contains(e) && w.Get<Thing>(e).Def.Mix != null) is { IsNull: false })
            return new LessonStage("mix", bar, Done: true);
        return Tankard(sim, me, run, run.Station(sim, "AleCask")) ?? new LessonStage("mix", bar);
    }

    /// <summary>A party wanting work, sent where the innkeeper thinks fits.</summary>
    public static LessonStage Quest(Simulation sim, Entity me, LessonRun run)
    {
        var view = Perception.For(sim, me);
        return OrderSince(view, run, ServiceKind.Quest) switch
        {
            { Status: OrderStatus.Done } => new LessonStage("offer", Entity.Null, Done: true),
            { Status: OrderStatus.Open } open => new LessonStage("offer", open.Poster),
            _ => new LessonStage("wait", Entity.Null),
        };
    }

    /// <summary>"tankard" from the shelf behind the bar while there's no clean one in hand or pack; null once there is.</summary>
    private static LessonStage? Tankard(Simulation sim, Entity me, LessonRun run, Entity cask)
    {
        if (!sim.World.TryGet<Thing>(cask, out var c) || c.Def.Building?.Cask is not { } props)
            return null;
        return AvatarSystem.Supply(sim, me, d => d.Name == props.Into).IsNull ? new LessonStage("tankard", run.Station(sim, "BarShelf", cask)) : null;
    }
}
