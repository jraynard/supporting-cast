using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Scenarios.Lessons;
using static HeroesNotIncluded.Sim.Scenarios.Lessons.Lessons;

namespace HeroesNotIncluded.Musician;

/// <summary>
/// The musician's lessons (their words are <c>lessons</c> in musician.json): a scale played free, a tune struck up from
/// the book and carried through, the lute tuned, and busking for tips. Read from the session in hand, as every lesson
/// is: what's been done is kept on the run (<see cref="LessonRun.Passed"/>), game-side, never in the world.
/// </summary>
public static class MusicianLessons
{
    public const string Trade = MusicianModule.TradeName;

    /// <summary>How near true (cents) every string must be for the lute to count as tuned: well inside sour.</summary>
    public const float InTune = 10f;

    public static readonly Lesson[] All =
    [
        new(Trade, "scale", ["start", "scale"], Scale),
        new(Trade, "tune", ["start", "book", "opening", "carry"], Tune),
        new(Trade, "strings", ["lute", "start", "tune"], Strings),
        new(Trade, "busk", ["start", "tips"], Busk),
    ];

    /// <summary>The instrument out and played free: the eight keys, D up to D, in order.</summary>
    public static LessonStage Scale(Simulation sim, Entity me, LessonRun run)
    {
        if (run.Passed.Contains("scale"))
            return new LessonStage("scale", Entity.Null, Done: true);
        if (Session(sim, me) is not PlaySession s)
            return new LessonStage("start", Entity.Null);
        // Up the scale: each key 0..7 in turn, other notes between them allowed (a slip, a second go).
        var next = 0;
        foreach (var note in s.Notes)
            if (note.Index == next && ++next == 8)
            {
                run.Passed.Add("scale");
                return new LessonStage("scale", Entity.Null, Done: true);
            }
        return new LessonStage("scale", Entity.Null);
    }

    /// <summary>The book opened, a tune's opening played, the hands carrying it, and the tune seen through to its end.</summary>
    public static LessonStage Tune(Simulation sim, Entity me, LessonRun run)
    {
        if (run.Passed.Contains("finished"))
            return new LessonStage("carry", Entity.Null, Done: true);
        if (Session(sim, me) is not PlaySession s)
            return new LessonStage("start", Entity.Null);
        if (s.Finished > 0)
        {
            run.Passed.Add("finished");
            return new LessonStage("carry", Entity.Null, Done: true);
        }
        if (s.Carrying != null)
            return new LessonStage("carry", Entity.Null);
        return new LessonStage(s.Mode == PlayMode.Book ? "opening" : "book", Entity.Null);
    }

    /// <summary>The lute in hand (its strings put out for the lesson), its tuning opened, and every string brought true.</summary>
    public static LessonStage Strings(Simulation sim, Entity me, LessonRun run)
    {
        var w = sim.World;
        if (run.Passed.Contains("tuned"))
            return new LessonStage("tune", Entity.Null, Done: true);
        var lute = LuteOf(sim, me);
        switch (Session(sim, me))
        {
            case TuneSession t when t.InstrumentName == LuteDef:
                run.Passed.Add("tuning");
                return new LessonStage("tune", Entity.Null);
            case PlaySession p when p.InstrumentName == LuteDef:
                return new LessonStage("start", Entity.Null);
        }
        // Tuned once they've been at it, and every string's whole and near its note.
        if (run.Passed.Contains("tuning") && !lute.IsNull
            && sim.Defs.ModDef<InstrumentDef>(InstrumentDef.Kind, LuteDef) is { } def && Tunings.Of(sim, lute, def) is { } tuning
            && tuning.Broken == 0 && tuning.Cents.All(c => MathF.Abs(c) <= InTune))
        {
            run.Passed.Add("tuned");
            return new LessonStage("tune", Entity.Null, Done: true);
        }
        var inHand = w.Get<Carrying>(me).Item == lute && !lute.IsNull;
        return new LessonStage(inHand ? "start" : "lute", Entity.Null);
    }

    /// <summary>Playing where the town can hear, the hat down, and a tip landed in it.</summary>
    public static LessonStage Busk(Simulation sim, Entity me, LessonRun run)
    {
        if (run.Passed.Contains("tipped"))
            return new LessonStage("tips", Entity.Null, Done: true);
        if (Session(sim, me) is not PlaySession s)
            return new LessonStage("start", Entity.Null);
        if (s.Tips.Exists(t => t.Tick >= run.StartTick))
        {
            run.Passed.Add("tipped");
            return new LessonStage("tips", Entity.Null, Done: true);
        }
        return new LessonStage("tips", Entity.Null);
    }

    public const string LuteDef = "Lute";

    /// <summary>The player's lute: in hand, or the first in the pack.</summary>
    public static Entity LuteOf(Simulation sim, Entity me)
    {
        var w = sim.World;
        var held = w.Get<Carrying>(me).Item;
        if (w.IsAlive(held) && w.Get<Thing>(held).Def.Name == LuteDef)
            return held;
        return sim.Defs.TryThing(LuteDef, out var def) ? HeroesNotIncluded.Sim.Systems.Inventory.CarriedOf(sim, me, def).FirstOrDefault(Entity.Null) : Entity.Null;
    }

    /// <summary>The strings lesson's lute, put out of tune (each string a different way, none snapped), as a lute left a while is.</summary>
    public static void PutOut(Simulation sim, Entity me)
    {
        var lute = LuteOf(sim, me);
        if (lute.IsNull || sim.Defs.ModDef<InstrumentDef>(InstrumentDef.Kind, LuteDef) is not { } def || Tunings.Of(sim, lute, def) is not { } tuning)
            return;
        float[] off = [-38f, 24f, -45f, 31f, -20f];
        for (var i = 0; i < tuning.Cents.Length; i++)
            tuning.Cents[i] = off[i % off.Length];
        tuning.Broken = 0;
    }
}
