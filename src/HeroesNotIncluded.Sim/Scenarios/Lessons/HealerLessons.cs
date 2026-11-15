using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Healing;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Scenarios.Lessons.Lessons;

namespace HeroesNotIncluded.Sim.Scenarios.Lessons;

/// <summary>
/// The healer's lessons: a patient's wound felt over, drawn in and mended by the gift; a fever the
/// same way, which passes to whoever lingers; and mana won back at the meditation stone. Read from
/// the wounds as the empath feels them (never named by kind once drawn) and their own mana.
/// </summary>
public static class HealerLessons
{
    public const string Trade = "Healer";

    private static readonly string[] Mending = ["feel", "draw", "meditate", "weave"];

    public static readonly Lesson[] All =
    [
        new(Trade, "wound", Mending, Patient),
        new(Trade, "fever", Mending, Patient),
        new(Trade, "meditate", ["stone", "meditate"], Meditate),
    ];

    /// <summary>Mana won back this far (a share of the most) finishes the meditation lesson.</summary>
    public const float Rested = 0.5f;

    /// <summary>The practice patient: felt over, their wound drawn in, and mended in the healer by the gift.</summary>
    public static LessonStage Patient(Simulation sim, Entity me, LessonRun run)
    {
        var view = Perception.For(sim, me);
        var w = sim.World;
        var patient = PatientOf(sim, run);
        switch (Session(sim, me))
        {
            case FeelSession: return new LessonStage("feel", patient);
            case DrawSession: return new LessonStage("draw", patient);
            case CastSession: return new LessonStage("weave", Entity.Null);
            case MeditateSession: return new LessonStage("meditate", run.Station(sim, "MeditationStone"));
        }
        // What you've drawn in: mended by the gift (Y), or mana first if there's too little.
        var carried = (view.FeltWounds(me) ?? []).Where(f => f.Absorbed).ToList();
        if (carried.Count > 0)
        {
            run.Passed.Add("drawn");
            var cheapest = sim.Defs.AllMendings.Count > 0 ? sim.Defs.AllMendings.Min(m => m.ManaCost) : 0f;
            return w.TryGet<Mana>(me, out var mana) && mana.Current < cheapest
                ? new LessonStage("meditate", run.Station(sim, "MeditationStone"))
                : new LessonStage("weave", Entity.Null);
        }
        var felt = patient.IsNull ? null : view.FeltWounds(patient);
        if (run.Passed.Contains("drawn") && felt is not { Count: > 0 })
            return new LessonStage("weave", Entity.Null, Done: true);
        return new LessonStage(felt is { Count: > 0 } ? "draw" : "feel", patient);
    }

    /// <summary>Mana spent, and won back by meditating, best at the shrine's stone.</summary>
    public static LessonStage Meditate(Simulation sim, Entity me, LessonRun run)
    {
        var w = sim.World;
        var stone = run.Station(sim, "MeditationStone");
        if (!w.TryGet<Mana>(me, out var mana))
            return new LessonStage("meditate", stone);
        if (Session(sim, me) is MeditateSession)
            return new LessonStage("meditate", stone);
        if (mana.Current >= mana.Max * Rested && run.Passed.Contains("spent"))
            return new LessonStage("meditate", stone, Done: true);
        if (mana.Current < mana.Max * Rested)
            run.Passed.Add("spent");
        return !stone.IsNull && !AvatarSystem.InReach(sim, w.Get<Position>(me).Cell, stone)
            ? new LessonStage("stone", stone)
            : new LessonStage("meditate", stone);
    }

    /// <summary>The practice patient in the shrine (Practice.PatientName): the newest there.</summary>
    private static Entity PatientOf(Simulation sim, LessonRun run)
    {
        var w = sim.World;
        var found = Entity.Null;
        foreach (var e in sim.Creatures())
            if (w.Get<Creature>(e) is { Dead: false } c && c.Name == Practice.PatientName && !w.Has<Avatar>(e)
                && Buildings.At(sim, w.Get<Position>(e).Cell) == run.Workplace)
                found = e;
        return found;
    }
}
