using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Speech;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>What's said when something happens (<see cref="Barks"/>), and how a bot's temper is drawn (<see cref="BotTemperament"/>).</summary>
public class BarksTests
{
    private static DefDatabase Defs() => DefDatabase.FromJson("""
        { "barks": [ { "name": "greet", "lines": [
            { "text": "Morning, {who}." },
            { "text": "Oi, {who}!", "when": [ "rowdy" ] },
            { "text": "Lovely to see you, {who}.", "when": [ "innkeeper", "chatty" ] } ] } ],
          "botTempers": [ { "name": "Careful", "care": 0.9 }, { "name": "Hasty", "care": 0.3, "haste": 0.9 } ] }
        """);

    [Fact]
    public void ALineIsSaidOnlyBySomeoneWithEveryTagItAsksFor()
    {
        var defs = Defs();
        var rand = new SimRandom(1);
        for (var i = 0; i < 50; i++)
        {
            var plain = Barks.Pick(defs, "greet", ["villager"], rand, who: "Ana");
            Assert.Equal("Morning, Ana.", plain);
            var keeper = Barks.Pick(defs, "greet", ["innkeeper"], rand, who: "Ana");
            Assert.Equal("Morning, Ana.", keeper); // chatty too, or not at all
        }
        var said = Enumerable.Range(0, 200).Select(_ => Barks.Pick(defs, "greet", ["innkeeper", "chatty", "rowdy"], rand, who: "Bea")).ToHashSet();
        Assert.Equal(3, said.Count);
        Assert.Null(Barks.Pick(defs, "nothing written", ["villager"], rand));
    }

    [Fact]
    public void TheMoreParticularLineIsTheLikelier()
    {
        var defs = Defs();
        var rand = new SimRandom(2);
        var rowdy = Enumerable.Range(0, 1000).Count(_ => Barks.Pick(defs, "greet", ["rowdy"], rand, who: "Cal") == "Oi, Cal!");
        Assert.InRange(rowdy, 650, 850); // weight 3 to the plain line's 1
    }

    [Fact]
    public void PicksAreTheSameForTheSameSeedAndAvoidTheLastSaid()
    {
        var defs = Defs();
        string?[] Run(ulong seed)
        {
            var rand = new SimRandom(seed);
            return [.. Enumerable.Range(0, 20).Select(_ => Barks.Pick(defs, "greet", ["rowdy"], rand, who: "Dov"))];
        }
        Assert.Equal(Run(9), Run(9));
        var r = new SimRandom(3);
        for (var i = 0; i < 50; i++)
            Assert.Equal("Morning, Dov.", Barks.Pick(defs, "greet", ["rowdy"], r, not: "Oi, Dov!", who: "Dov"));
    }

    [Fact]
    public void ABotsTemperIsDrawnByItsSeedAndWobbledALittle()
    {
        var defs = Defs();
        Assert.Equal(BotTemperament.Roll(defs, 5), BotTemperament.Roll(defs, 5));
        var drawn = Enumerable.Range(0, 40).Select(i => BotTemperament.Roll(defs, (ulong)i)).ToList();
        Assert.Contains(drawn, t => t.Name == "Careful");
        Assert.Contains(drawn, t => t.Name == "Hasty");
        Assert.All(drawn.Where(t => t.Name == "Careful"), t => Assert.InRange(t.Care, 0.9f - BotTemperament.Wobble, 1f));
        Assert.Equal(BotTemperament.Steady, BotTemperament.Roll(DefDatabase.FromJson("{}"), 5));
    }

    [Fact]
    public void TheShippedBarksAndTempersLoad()
    {
        var defs = TestWorld.LoadDefs();
        Assert.NotEmpty(defs.AllBotTempers);
        foreach (var id in (string[])["greet", "reply", "idle", "alarm_fire", "alarm_dragon", "alarm_raid", "alarm_downed", "alarm_body", "need", "too_dear_reply", "blamed_key", "revived", "carry", "douse"])
            Assert.NotNull(Barks.Pick(defs, id, ["bot", "villager"], new SimRandom(1)));
    }
}
