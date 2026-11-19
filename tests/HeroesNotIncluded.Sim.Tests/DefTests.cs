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
    public void APatchMergesOnlyTheFieldsItGives()
    {
        var db = DefDatabase.FromJson(
            """{ "things": [ { "name": "Rock", "label": "rock", "mass": 3, "maxHp": 10 }, { "name": "Pebble", "mass": 1 } ] }""",
            """{ "things": [ { "name": "rock", "$patch": "merge", "maxHp": 99 } ] }""");
        var rock = db.Thing("Rock");
        Assert.Equal(99, rock.MaxHp);
        Assert.Equal(3, rock.Mass);
        Assert.Equal("rock", rock.Label);
        Assert.Equal(0, rock.Index); // it keeps its place
        Assert.Equal(1, db.Thing("Pebble").Index);
    }

    [Fact]
    public void APatchMergesObjectsAndReplacesArrays()
    {
        var db = DefDatabase.FromJson(
            """{ "things": [ { "name": "Rock", "mass": 1 }, { "name": "Pebble", "mass": 1 }, { "name": "Bellows", "mass": 5, "building": { "storageOnly": ["Rock", "Pebble"], "bellows": { "heat": 100, "lift": 50 } } } ] }""",
            """{ "things": [ { "name": "Bellows", "$patch": "merge", "building": { "storageOnly": ["Pebble"], "bellows": { "lift": 80 } } } ] }""");
        var building = db.Thing("Bellows").Building!;
        Assert.Equal(100, building.Bellows!.Heat);
        Assert.Equal(80, building.Bellows.Lift);
        Assert.Equal(["Pebble"], building.StorageOnly);
    }

    [Fact]
    public void APatchRemovesADefAndTheRestCloseUp()
    {
        var db = DefDatabase.FromJson(
            """{ "things": [ { "name": "Rock", "mass": 1 }, { "name": "Pebble", "mass": 1 } ] }""",
            """{ "things": [ { "name": "Rock", "$patch": "remove" } ] }""");
        Assert.Equal(["Pebble"], db.AllThings.Select(t => t.Name));
        Assert.Equal(0, db.Thing("Pebble").Index);
    }

    [Fact]
    public void APatchOnNothingSaysSo()
    {
        var ex = Assert.ThrowsAny<Exception>(() => DefDatabase.FromJson("""{ "things": [ { "name": "Rock", "$patch": "merge", "mass": 2 } ] }"""));
        Assert.Contains("no earlier file defines", ex.Message);
        ex = Assert.ThrowsAny<Exception>(() => DefDatabase.FromJson("""{ "things": [ { "name": "Rock", "mass": 1 } ] }""", """{ "things": [ { "name": "Rock", "$patch": "replace" } ] }"""));
        Assert.Contains("\"merge\" or \"remove\"", ex.Message);
    }

    [Fact]
    public void ModDirectoriesLayerOverTheGamesOwn()
    {
        var core = Directory.CreateTempSubdirectory("hni-core").FullName;
        var mod = Directory.CreateTempSubdirectory("hni-mod").FullName;
        try
        {
            File.WriteAllText(Path.Combine(core, "a.json"), """{ "things": [ { "name": "Rock", "mass": 1, "maxHp": 10 } ] }""");
            File.WriteAllText(Path.Combine(mod, "a.json"), """{ "things": [ { "name": "Rock", "$patch": "merge", "maxHp": 50 }, { "name": "Lute", "mass": 2 } ] }""");
            var db = DefDatabase.LoadDirectories([core, mod]);
            Assert.Equal(50, db.Thing("Rock").MaxHp);
            Assert.Equal(1, db.Thing("Lute").Index);
        }
        finally
        {
            Directory.Delete(core, true);
            Directory.Delete(mod, true);
        }
    }

    [Fact]
    public void EveryTradesStationsStandInItsWorkplace()
    {
        // The first-day rings mark these: one that isn't there marks nothing, silently.
        var (sim, _) = TestWorld.Millbrook();
        var w = sim.World;
        foreach (var trade in sim.Defs.AllTrades.Where(t => t.StationDefs.Count > 0))
        {
            var building = Shops.ShopSystem.WorkplaceOf(sim, trade);
            Assert.False(building.IsNull, $"{trade.Name}: no {trade.Workplace} in town");
            foreach (var station in trade.StationDefs)
                Assert.True(w.Query<Components.Thing>().Any(e => w.Get<Components.Thing>(e).Def == station
                        && (Systems.Buildings.Of(sim, e) == building || Systems.Crafting.KeptAt(sim, e) == building)), // or in its yard (the smithy's kiln)
                    $"{trade.Name}: no {station.Name} in the {trade.Workplace}");
        }
    }
}
