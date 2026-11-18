using HeroesNotIncluded.Sim.Carpentry;
using static HeroesNotIncluded.Sim.Scenarios.Lessons.Lessons;

using HeroesNotIncluded.Sim.Scenarios.Lessons;

namespace HeroesNotIncluded.Sim.Carpentry;

/// <summary>
/// The carpenter's lessons: planks sawn at the sawhorse, a quiver made at the bench and put
/// out for sale, and a bow tillered. Read from the session in hand and what the player carries and has on show.
/// </summary>
public static class CarpenterLessons
{
    public const string Trade = "Carpenter";

    public static readonly Lesson[] All =
    [
        new(Trade, "saw", ["logs", "start", "hew", "saw"], Saw),
        new(Trade, "quiver", ["start", "sight", "shave", "head", "fletch", "show"], Quiver),
        new(Trade, "bow", ["start", "tiller", "string"], Bow),
        new(Trade, "woods", ["tree", "aim", "notch", "back", "buck", "carry"], Woods),
        new(Trade, "firewood", ["start", "split"], Firewood),
    ];

    /// <summary>Planks by hand: logs from the chest, a batch started at the sawhorse, the log hewn square, the beam sawn on its lines.</summary>
    public static LessonStage Saw(Simulation sim, Entity me, LessonRun run)
    {
        var horse = run.Station(sim, Carpenters.SawhorseDef);
        if (Session(sim, me) is SawSession s)
            return new LessonStage(s.Hewing ? "hew" : "saw", horse);
        if (Gained(sim, me, run, Carpenters.PlanksDef))
            return new LessonStage("saw", horse, Done: true);
        // The chest's across the shed from the sawhorse: the logs are carried over.
        if (OnMe(sim, me, Carpenters.LogDef) < Carpenters.LogsPerBatch && OnMe(sim, me, Carpenters.WoodDef) < 6)
            return new LessonStage("logs", run.Station(sim, "Chest"));
        return new LessonStage("start", horse);
    }

    /// <summary>A quiver fletched at the bench (sighted, then feathered), then put down in the woodshop with a price.</summary>
    public static LessonStage Quiver(Simulation sim, Entity me, LessonRun run)
    {
        var w = sim.World;
        var bench = run.Station(sim, Carpenters.BenchDef);
        if (Session(sim, me) is FletchSession f)
            return new LessonStage(f.Stage switch
            {
                ArrowStage.Sight => "sight",
                ArrowStage.Shave => "shave",
                ArrowStage.Head => "head",
                _ => "fletch",
            }, bench);
        if (Shops.Goods.OnShow(sim).Exists(g => g.Shop == run.Workplace && !run.Had.Contains(g.Item)
                && w.TryGet<Components.Crafted>(g.Item, out var c) && c.Maker == me && w.Get<Components.Thing>(g.Item).Def.Quiver))
            return new LessonStage("show", Entity.Null, Done: true);
        if (!Made(sim, me, run, d => d.Quiver).IsNull)
            return new LessonStage("show", Entity.Null);
        return new LessonStage("start", bench);
    }

    /// <summary>
    /// A tree felled where it's aimed (aim, notch, back cut), its trunk bucked into logs, and a log
    /// carried home to the sawhorse. Pointed at the nearest tree, then the trunk it leaves.
    /// </summary>
    public static LessonStage Woods(Simulation sim, Entity me, LessonRun run)
    {
        var w = sim.World;
        switch (Session(sim, me))
        {
            case FellSession f:
                return new LessonStage(!f.AimSet ? "aim" : !f.Backing ? "notch" : "back", f.Tree);
            case BuckSession b:
                return new LessonStage("buck", b.Trunk);
        }
        var horse = run.Station(sim, Carpenters.SawhorseDef);
        if (OnMe(sim, me, Felling.LogDef) + OnMe(sim, me, Felling.GreatLogDef) > 0)
        {
            var home = !horse.IsNull && Systems.AvatarSystem.InReach(sim, w.Get<Components.Position>(me).Cell, horse);
            return new LessonStage("carry", horse, Done: home);
        }
        var near = w.Get<Components.Position>(me).Cell;
        Entity Nearest(Func<Entity, bool> what) => sim.SpiralFrom(near, 30)
            .Where(c => sim.Map.InBounds(c))
            .Select(c => sim.Map.Structure[sim.Map.Idx(c)])
            .FirstOrDefault(e => !e.IsNull && what(e), Entity.Null);
        // A trunk down already (theirs, or one left lying): buck it. Else the nearest standing tree.
        var trunk = Nearest(e => Felling.IsTrunk(sim, e));
        if (!trunk.IsNull)
            return new LessonStage("buck", trunk);
        return new LessonStage("tree", Nearest(e => w.Get<Components.Thing>(e).Def.Felling != null && !w.Has<Falling>(e)));
    }

    /// <summary>A log split into firewood at the chopping block.</summary>
    public static LessonStage Firewood(Simulation sim, Entity me, LessonRun run)
    {
        var block = run.Station(sim, Carpenters.BlockDef);
        if (Session(sim, me) is SplitSession)
            return new LessonStage("split", block);
        return new LessonStage("start", block, Done: Gained(sim, me, run, Carpenters.FirewoodDef));
    }

    /// <summary>A bow tillered at the bench, then strung.</summary>
    public static LessonStage Bow(Simulation sim, Entity me, LessonRun run)
    {
        var bench = run.Station(sim, Carpenters.BenchDef);
        if (Session(sim, me) is BowSession b)
            return new LessonStage(b.Stringing ? "string" : "tiller", bench);
        return new LessonStage("start", bench, Done: !Made(sim, me, run, d => d.Name == "Bow").IsNull);
    }
}
