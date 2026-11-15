using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Healing;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Scenarios.Lessons.Lessons;

namespace HeroesNotIncluded.Sim.Scenarios.Lessons;

/// <summary>
/// The healer's lessons: a patient's wound felt over, drawn in and mended by the gift; several at
/// once (a fever among them); festering found under a dressing; and the shrine's help, from mana won
/// back at the meditation stone to a charm put out for sale. Read from the wounds as the empath
/// feels them (never named by kind once drawn) and their own mana. (There were six; a playtest
/// found the wound, fever and many-hurts lessons too alike, and meditating too small on its own.)
/// </summary>
public static class HealerLessons
{
    public const string Trade = "Healer";

    private static readonly string[] Mending = ["feel", "read", "draw", "meditate", "weave"];

    public static readonly Lesson[] All =
    [
        new(Trade, "wound", Mending, Patient),
        new(Trade, "many", ["feel", "read", "draw", "next"], Many),
        new(Trade, "festering", ["feel", "read", "deep", "draw", "meditate", "weave"], Patient),
        new(Trade, "shrine", ["stone", "meditate", "corner", "bind", "altar", "show"], Shrine),
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
            case HandsOnSession h:
                return new LessonStage(HandsStep(h), patient);
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

    /// <summary>
    /// Where hands laid on someone have got to: drawing (the link open, or a wound read to draw),
    /// pressing long on a dressed wound to find what's under it, reading a warmth, or still skimming.
    /// </summary>
    private static string HandsStep(HandsOnSession h)
    {
        if (h.Under >= 0 && h.Spots[h.Under] is { Hidden: true, Found: >= 1f, Deep: < 1f })
            return "deep";
        if (h.Linked || h.Spots.Exists(h.Drawable))
            return "draw";
        return h.Spots.Exists(s => s.Sensed > 0f) ? "read" : "feel";
    }

    /// <summary>A patient with three hurts, all drawn in one sitting: after the first, on to the next (W/S).</summary>
    public static LessonStage Many(Simulation sim, Entity me, LessonRun run)
    {
        var w = sim.World;
        var patient = PatientOf(sim, run);
        if (!patient.IsNull && w.TryGet<Health>(patient, out var h) && h.Injuries.Count > 0)
            run.Passed.Add("hurt");
        if (run.Passed.Contains("hurt") && (patient.IsNull || !w.TryGet<Health>(patient, out var left) || left.Injuries.Count == 0))
            return new LessonStage("next", Entity.Null, Done: true);
        if (Session(sim, me) is HandsOnSession s)
        {
            var step = HandsStep(s);
            // One drawn and more to go: the hands move on, not off.
            if (s.Spots.Exists(p => p.Drawn >= 1f) && !s.Linked && step != "draw")
                return new LessonStage("next", patient);
            return new LessonStage(step, patient);
        }
        return new LessonStage("feel", patient);
    }

    /// <summary>
    /// The shrine's help: mana won back at the meditation stone (the lesson starts with none), a cut
    /// bound by the healer's corner (the wash basin and the herbs in reach, so it's washed and
    /// poulticed), then a charm woven at the rune altar and put out for sale.
    /// </summary>
    public static LessonStage Shrine(Simulation sim, Entity me, LessonRun run)
    {
        var w = sim.World;
        if (!run.Passed.Contains("rested"))
        {
            var rest = Meditate(sim, me, run);
            if (!rest.Done)
                return rest;
            run.Passed.Add("rested");
        }
        var patient = PatientOf(sim, run);
        if (Session(sim, me) is CastSession)
            return new LessonStage("altar", Entity.Null);
        if (Session(sim, me) is BandageSession)
            return new LessonStage("bind", patient);
        if (!run.Passed.Contains("bound"))
        {
            if (!patient.IsNull && w.TryGet<Health>(patient, out var h) && h.Injuries.Count > 0 && h.Injuries.TrueForAll(i => i.Tended && !i.Stanched))
                run.Passed.Add("bound");
            else
            {
                var (poultice, washes) = Bandaging.ShrineHelp(sim, me);
                var touching = !patient.IsNull && Ai.Pathfinder.Touches(w.Get<Position>(me).Cell, w.Get<Position>(patient).Cell);
                return poultice > 0f && washes && touching ? new LessonStage("bind", patient) : new LessonStage("corner", patient);
            }
        }
        var shrine = run.Workplace;
        if (Shops.Goods.OnShow(sim).Exists(g => g.Shop == shrine && w.Has<Charm>(g.Item) && w.Get<Charm>(g.Item).Maker == me))
            return new LessonStage("show", Entity.Null, Done: true);
        if (Charms.Carried(sim, me).Exists(c => w.Get<Charm>(c).Maker == me))
            return new LessonStage("show", Entity.Null);
        return new LessonStage("altar", run.Station(sim, "RuneAltar"));
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
