using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Tests;

public class DefTests
{
    [Fact]
    public void EveryThoughtCodeAddsHasADef()
    {
        var db = TestWorld.LoadDefs();
        foreach (var name in KnownThoughts.Names)
        {
            var def = db.Thought(name);
            Assert.False(def.Situational, $"{name} is added as a memory, so it can't have a 'when'");
        }
        foreach (var condition in Enum.GetValues<ThoughtCondition>().Skip(1))
            Assert.Contains(db.AllThoughts, t => t.When == condition);
    }

    [Fact]
    public void UnknownReferenceThrowsClearError()
    {
        var ex = Assert.ThrowsAny<Exception>(() => DefDatabase.FromJson(
            """{ "things": [ { "name": "Bad", "building": { "cost": [ { "def": "Unobtainium" } ] } } ] }"""));
        Assert.Contains("Unobtainium", ex.Message);
    }

    [Fact]
    public void LaterFilesOverrideEarlierOnes()
    {
        var db = DefDatabase.FromJson(
            """{ "things": [ { "name": "Rock", "mass": 1, "maxHp": 10 } ] }""",
            """{ "things": [ { "name": "Rock", "mass": 1, "maxHp": 99 } ] }""");
        Assert.Single(db.AllThings);
        Assert.Equal(99, db.Thing("Rock").MaxHp);
    }
}
