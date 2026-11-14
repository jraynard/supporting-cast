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

    [Fact]
    public void EveryTradesStationsStandInItsWorkplace()
    {
        // The first-day rings mark these: one that isn't there marks nothing, silently.
        var (sim, _) = TestWorld.Millbrook();
        var w = sim.World;
        foreach (var trade in sim.Defs.AllTrades.Where(t => t.StationDefs.Count > 0))
        {
            var building = Shops.ShopSystem.BuildingOf(sim, trade.WorkplaceDef!);
            Assert.False(building.IsNull, $"{trade.Name}: no {trade.Workplace} in town");
            foreach (var station in trade.StationDefs)
                Assert.True(w.Query<Components.Thing>().Any(e => w.Get<Components.Thing>(e).Def == station && Systems.Buildings.Of(sim, e) == building),
                    $"{trade.Name}: no {station.Name} in the {trade.Workplace}");
        }
    }
}
