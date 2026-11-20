using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Scenarios.Lessons;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// The tutorial's run: which chapters a newcomer gets in which order (<see cref="Tutorial"/>), and
/// the basics chapter every trade shares (<see cref="BasicsLesson"/>; the fight is <see cref="CombatLessonTests"/>). The trades' own chapters are
/// walked through in <see cref="TradeLessonTests"/> and <see cref="SmithLessonTests"/>.
/// </summary>
public class TutorialTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    [Fact]
    public void EveryTradeWithAWorkplaceHasACoreChapterAndWordsToBeginWith()
    {
        foreach (var trade in LoadDefs().AllTrades.Where(t => t.HasWorkplace))
        {
            Assert.True(trade.Lessons.Any(l => l.Core), $"{trade.Name} has no core chapter");
            Assert.False(string.IsNullOrWhiteSpace(trade.TutorialIntro), $"{trade.Name} has no tutorialIntro");
        }
    }

    [Fact]
    public void TheBasicsHaveWordsForEveryStepTheirLogicNames()
    {
        var basics = LoadDefs().Basics;
        Assert.Equal(Tutorial.BasicsId, basics.Id);
        foreach (var step in BasicsLesson.Lesson.Steps)
            Assert.True(basics.IndexOf(step) >= 0, $"the basics' '{step}' has no words");
    }

    [Fact]
    public void TheRunIsTheBasicsThenTheCoreChaptersInOrder()
    {
        var defs = LoadDefs();
        var smith = defs.Trade("Blacksmith");
        var core = smith.Lessons.Where(l => l.Core).ToList();

        var shared = defs.SharedLessons.Where(l => l.Core).ToList();
        var fresh = Tutorial.Chapters(defs, smith, _ => false);
        Assert.Same(defs.Basics, fresh[0]);
        Assert.Equal(shared, fresh.Skip(1).Take(shared.Count));
        Assert.Equal(core, fresh.Skip(1 + shared.Count));

        // Someone who's done the basics and the shared chapters (in any trade) goes straight to the trade.
        var known = Tutorial.Chapters(defs, smith, key => key == Tutorial.BasicsKey || shared.Exists(l => Tutorial.SharedKey(l.Id) == key));
        Assert.Equal(core, known);
        Assert.Equal(smith.Lessons.Where(l => !l.Core), Tutorial.Extras(smith));

        Assert.False(Tutorial.CoreLearned(smith, _ => false));
        Assert.True(Tutorial.CoreLearned(smith, key => core.Exists(l => LessonRun.KeyOf(smith, l) == key)));
        Assert.Equal(Tutorial.BasicsKey, LessonRun.KeyOf(smith, defs.Basics));
    }

    [Fact]
    public void TheBasicsWalkThenTheWorkplaceThenTheWindows()
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade("Blacksmith")], []));
        sim.ApplyNow(new StartRoundCommand());
        sim.Round.Practice = true;
        var me = Players.CharacterOf(sim, Ana);
        var run = new LessonRun(sim, me, sim.Defs.Trade("Blacksmith"), sim.Defs.Basics);
        LessonStage Stage() => Lessons.Stage(sim, me, run);
        void StandAt(int3 cell)
        {
            sim.World.Get<Position>(me).Cell = cell;
            sim.World.Get<Position>(me).Pos = Position.CenterOf(cell);
        }

        Assert.Equal("move", Stage().Step);

        // A walk that ends outside the forge: to the forge, pointed at.
        var start = sim.World.Get<Position>(me).Cell;
        var outside = sim.SpiralFrom(start, 12).First(c => c.Z == start.Z && sim.Map.Passable(c)
            && Maps.Map.Distance(c, start) >= BasicsLesson.WalkCells && Buildings.At(sim, c) != run.Workplace);
        StandAt(outside);
        Assert.Equal(new LessonStage("workplace", run.Workplace), Stage());

        // In the forge, the windows: each the game's to say it saw opened.
        var inside = sim.SpiralFrom(sim.World.Get<PresetBuilding>(run.Workplace).Anchor, 8)
            .First(c => sim.Map.Passable(c) && Buildings.At(sim, c) == run.Workplace);
        StandAt(inside);
        Assert.Equal("pack", Stage().Step);
        StandAt(outside);
        Assert.Equal("pack", Stage().Step); // been there: walking out again doesn't undo it
        run.Passed.Add("pack");
        Assert.Equal("shop", Stage().Step);
        run.Passed.Add("shop");
        Assert.Equal("controls", Stage().Step);
        Assert.False(Stage().Done);
        run.Passed.Add("controls");
        Assert.True(Stage().Done);
    }

    [Fact]
    public void WithNoWorkplaceTheBasicsSkipTheWorkplaceAndTheShop()
    {
        // The musician plays anywhere: no building to walk to, no sign to price.
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade("Musician")], []));
        sim.ApplyNow(new StartRoundCommand());
        sim.Round.Practice = true;
        var me = Players.CharacterOf(sim, Ana);
        var trade = sim.Defs.Trade("Musician");
        Assert.False(trade.HasWorkplace);
        Assert.Contains(trade.Lessons, l => l.Core); // so the tutorial lists it
        var run = new LessonRun(sim, me, trade, sim.Defs.Basics);
        run.Passed.Add("moved");
        Assert.Equal("pack", Lessons.Stage(sim, me, run).Step);
        run.Passed.Add("pack");
        Assert.Equal("controls", Lessons.Stage(sim, me, run).Step);
        run.Passed.Add("controls");
        Assert.True(Lessons.Stage(sim, me, run).Done);
    }

    [Fact]
    public void ATradesLessonCantTakeTheBasicsId()
    {
        const string json = """
            { "trades": [ { "name": "Odd", "lessons": [ { "id": "basics", "steps": [ { "id": "a", "text": "x" } ] } ] } ] }
            """;
        var e = Assert.ThrowsAny<Exception>(() => DefDatabase.FromJson(json));
        Assert.Contains("basics", e.Message);
    }
}
