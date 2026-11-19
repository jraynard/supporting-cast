using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Scenarios.Lessons;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Scenarios.Lessons.Lessons;

namespace HeroesNotIncluded.Miner;

/// <summary>
/// The miner's lessons (their words are <c>lessons</c> in miner.json): into the mine, a face hewn, and what came away
/// carried back to the shed's chest. Read from where the player stands, the session in hand and what they carry,
/// as every lesson is; nothing here changes the world.
/// </summary>
public static class MinerLessons
{
    /// <summary>What a face gives up: the rock, and the ores a seam may hold (as the face looks, it's all rock till it's open).</summary>
    public static readonly string[] Hewn = ["Stone", "Coal", "IronOre", "CopperOre", "TinOre"];

    public static readonly Lesson[] All =
    [
        new(MinerModule.TradeName, "hew", ["mine", "face", "hew", "carry"], Hew),
    ];

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
