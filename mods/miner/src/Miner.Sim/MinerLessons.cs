using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Scenarios.Lessons;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Scenarios.Lessons.Lessons;

namespace HeroesNotIncluded.Miner;

/// <summary>
/// The miner's lessons (their words are <c>lessons</c> in miner.json): into the mine, a face hewn, and what came away
/// carried back to the shed's chest; the rock sounded for a seam, and chalked; and a barrowload wheeled to the smithy. Read from where the player stands, the session in hand and what they carry,
/// as every lesson is; nothing here changes the world.
/// </summary>
public static class MinerLessons
{
    /// <summary>What a face gives up: the rock, and the ores a seam may hold (as the face looks, it's all rock till it's open).</summary>
    public static readonly string[] Hewn = ["Stone", "Coal", "IronOre", "CopperOre", "TinOre"];

    public static readonly Lesson[] All =
    [
        new(MinerModule.TradeName, "hew", ["mine", "face", "hew", "carry"], Hew),
        new(MinerModule.TradeName, "sound", ["hammer", "face", "tap", "chalk"], Sound),
        new(MinerModule.TradeName, "haul", ["barrow", "load", "wheel", "tip"], Haul),
    ];

    /// <summary>
    /// A barrow from by the shed, loaded with ore, wheeled to the smithy and tipped into its chest. Read from what's in hand,
    /// the session in hand, and the smithy's chest: done once there's more ore in it than when the lesson first looked.
    /// </summary>
    public static LessonStage Haul(Simulation sim, Entity me, LessonRun run)
    {
        var w = sim.World;
        var smithy = HeroesNotIncluded.Sim.Shops.ShopSystem.BuildingOf(sim, sim.Defs.Building(HeroesNotIncluded.Sim.Shops.OreCarterSystem.SmithyDef));
        var chest = smithy.IsNull ? Entity.Null : Stock.ChestOf(sim, smithy);
        const string At = "smithy=";
        var ore = chest.IsNull ? 0 : sim.StoredIn(chest).Where(e => Barrows.Loadable(sim, w.Get<Thing>(e).Def)).Sum(e => w.Get<Stack>(e).Count);
        if (run.Passed.FirstOrDefault(p => p.StartsWith(At)) is not { } first)
            run.Passed.Add(At + ore);
        else if (int.TryParse(first[At.Length..], out var had) && ore > had)
            return new LessonStage("tip", chest, Done: true);

        if (Session(sim, me) is LoadSession)
            return new LessonStage("load", Entity.Null);
        var held = Barrows.Held(sim, me);
        if (!held.IsNull)
            return Barrows.LoadOf(sim, held).Pieces.Count > 0 ? new LessonStage("wheel", chest) : new LessonStage("load", Entity.Null);
        // The nearest barrow lying about (one stands by the shed).
        var here = w.Get<Position>(me).Cell;
        var barrow = w.Query<Thing>().Where(e => Barrows.IsBarrow(sim, e) && w.Get<Thing>(e).Spawned)
            .OrderBy(e => HeroesNotIncluded.Sim.Maps.Map.Distance(w.Get<Thing>(e).Cell, here)).ThenBy(e => e.Index).DefaultIfEmpty(Entity.Null).First();
        return new LessonStage("barrow", barrow);
    }

    /// <summary>
    /// The sounding hammer in hand, at a face, tapped along the wall, a seam chalked (or the sounding put down with chalk
    /// left on the wall). Read from the hand, the session in hand and the chalk in the mine; done once there's more
    /// chalk on the wall than when the lesson first looked.
    /// </summary>
    public static LessonStage Sound(Simulation sim, Entity me, LessonRun run)
    {
        const string At = "chalk=";
        var chalked = sim.SystemOrNull<ChalkMarks>()?.All.Count ?? 0;
        if (run.Passed.FirstOrDefault(p => p.StartsWith(At)) is not { } first)
            run.Passed.Add(At + chalked);
        else if (int.TryParse(first[At.Length..], out var had) && chalked > had)
            return new LessonStage("chalk", Entity.Null, Done: true);

        if (Session(sim, me) is SoundSession s)
            return new LessonStage(s.TapCount < SoundSession.Spots ? "tap" : "chalk", Entity.Null);
        if (!SoundSession.HammerInHand(sim, me))
            return new LessonStage("hammer", Entity.Null);
        var here = sim.World.Get<Position>(me).Cell;
        var underRock = sim.Map.InBounds(here) && sim.Map.CoveredAbove(sim.Map.Idx(here)) && Buildings.At(sim, here).IsNull;
        return new LessonStage("face", underRock ? Entity.Null : run.Workplace);
    }

    /// <summary>Into the mine (pointed at the shed by its mouth), a face, the hewing, then back to the shed's chest with what came away.</summary>
    public static LessonStage Hew(Simulation sim, Entity me, LessonRun run)
    {
        var w = sim.World;
        var chest = run.Station(sim, "Chest");
        // The chest's count when the lesson first looked: "done" is more in it than that.
        const string At = "chest=";
        var inChest = InChest(sim, chest);
        if (run.Passed.FirstOrDefault(p => p.StartsWith(At)) is not { } first)
            run.Passed.Add(At + inChest);
        else if (int.TryParse(first[At.Length..], out var had) && inChest > had)
            return new LessonStage("carry", chest, Done: true);

        if (Session(sim, me) is HewSession)
            return new LessonStage("hew", Entity.Null);
        if (Hewn.Sum(d => OnMe(sim, me, d)) > 0)
            return new LessonStage("carry", chest);
        // What's come away lying at the player's feet: pick it up (it's the same step, pointed at it).
        var here = w.Get<Position>(me).Cell;
        foreach (var c in sim.SpiralFrom(here, 2))
            if (sim.Map.InBounds(c) && sim.Map.Item[sim.Map.Idx(c)] is { IsNull: false } item
                && w.TryGet<Thing>(item, out var t) && Hewn.Contains(t.Def.Name))
                return new LessonStage("carry", item);
        // Under cover and in no building: under the rock, in the mine.
        var underRock = sim.Map.InBounds(here) && sim.Map.CoveredAbove(sim.Map.Idx(here)) && Buildings.At(sim, here).IsNull;
        return underRock ? new LessonStage("face", Entity.Null) : new LessonStage("mine", run.Workplace);
    }

    private static int InChest(Simulation sim, Entity chest) =>
        chest.IsNull ? 0 : Hewn.Sum(d => Stock.CountIn(sim, chest, d));
}
