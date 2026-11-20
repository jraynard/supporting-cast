using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Tests;

/// <summary>
/// The tutorial's words as the game shows them: every key mark turned into a key, and every
/// button a step glows (its "ui") an action the game has.
/// </summary>
public class TutorialWordsTests
{
    private static readonly Lazy<DefDatabase> Defs = new(() => TestContent.Defs());

    private static IEnumerable<(string Where, LessonDef Lesson)> Chapters()
    {
        yield return ("basics", Defs.Value.Basics);
        foreach (var trade in Defs.Value.AllTrades)
            foreach (var lesson in trade.Lessons)
                yield return ($"{trade.Name}/{lesson.Id}", lesson);
    }

    [Fact]
    public void EveryKeyMarkIsOneTheGameKnows()
    {
        // A mod's lessons name its own controls (~@musician:note1~, ~@miner:lean_left~): registered as the game registers a mod's game half.
        var before = Mods.GameMods.Loaded;
        Mods.GameMods.Use([.. before.Where(m => m.ModId is not ("musician" or "miner")),
            ("musician", typeof(Musician.MusicianGame).Assembly), ("miner", typeof(Miner.MinerGame).Assembly)]);
        try
        {
            Marks();
        }
        finally
        {
            Mods.GameMods.Use(before);
        }
    }

    private static void Marks()
    {
        var texts = Defs.Value.AllTrades.Select(t => ($"{t.Name} intro", t.TutorialIntro))
            .Concat(Chapters().SelectMany(c => c.Lesson.Steps.SelectMany(s => new[] { ($"{c.Where}/{s.Id}", s.Text), ($"{c.Where}/{s.Id} more", s.More ?? "") })
                .Concat(c.Lesson.Notes.Select(n => ($"{c.Where} note", n)))));
        foreach (var (where, text) in texts)
            Assert.False(Controls.Keys(text).Contains('~'), $"{where}: a key mark the game doesn't know in \"{text}\"");
    }

    [Fact]
    public void EveryGlowedButtonIsAnAction()
    {
        var acts = Controls.Defs.Select(d => d.Id).ToHashSet();
        foreach (var (where, lesson) in Chapters())
            foreach (var step in lesson.Steps.Where(s => s.Ui != null))
                Assert.True(acts.Contains(step.Ui!), $"{where}/{step.Id}: no action '{step.Ui}'");
    }
}
