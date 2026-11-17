using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Scenarios.Lessons;

/// <summary>
/// A trade's tutorial, as a list of chapters: the shared basics (unless already learned), then the
/// trade's core lessons in the order trades.json lists them. Everything else is an extra chapter,
/// offered once the run is through. Pure, so which chapters a newcomer gets is tested, not eyeballed.
/// </summary>
public static class Tutorial
{
    /// <summary>The basics chapter's lesson id; no trade's lesson may take it.</summary>
    public const string BasicsId = "basics";

    /// <summary>Progress for the basics is kept once, not per trade.</summary>
    public const string BasicsKey = "Basics/basics";

    /// <summary>The main run: the basics if not yet <paramref name="learned"/> (by progress key), then the core lessons.</summary>
    public static List<LessonDef> Chapters(DefDatabase defs, TradeDef trade, Func<string, bool> learned)
    {
        var chapters = new List<LessonDef>();
        if (!learned(BasicsKey))
            chapters.Add(defs.Basics);
        chapters.AddRange(trade.Lessons.Where(l => l.Core));
        return chapters;
    }

    /// <summary>The trade's lessons outside the main run, in listed order.</summary>
    public static List<LessonDef> Extras(TradeDef trade) => [.. trade.Lessons.Where(l => !l.Core)];

    /// <summary>Has the player learned every core lesson of the trade?</summary>
    public static bool CoreLearned(TradeDef trade, Func<string, bool> learned) =>
        trade.Lessons.Where(l => l.Core).All(l => learned(LessonRun.KeyOf(trade, l)));
}
