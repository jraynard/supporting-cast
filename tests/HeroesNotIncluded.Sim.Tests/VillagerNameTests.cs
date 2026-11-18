using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Scenarios;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The town's names: every one in the list can be dealt, and bots get only names nobody has.</summary>
public class VillagerNameTests
{
    [Fact]
    public void EveryNameInTheListCanBeDealtToATown()
    {
        var dealt = new HashSet<string>();
        for (var seed = 0UL; seed < (ulong)MapGenerator.VillagerNames.Length; seed++)
        {
            var sim = VillageRound.NewSimulation(LoadDefs(), seed);
            VillageRound.Setup(sim);
            foreach (var e in sim.World.Query<Creature>())
                dealt.Add(sim.World.Get<Creature>(e).Name);
        }
        Assert.All(MapGenerator.VillagerNames, n => Assert.Contains(n, dealt));
    }

    [Fact]
    public void UnusedNamesLeaveOutTheTownAndTheHero()
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        VillageRound.Setup(sim);
        var town = sim.World.Query<Creature>().Select(e => sim.World.Get<Creature>(e).Name).ToHashSet();
        var hero = MapGenerator.VillagerNames.First(n => !town.Contains(n));
        var free = MapGenerator.UnusedNames(sim, hero.ToLowerInvariant());
        Assert.NotEmpty(free);
        Assert.DoesNotContain(hero, free);
        Assert.All(free, n => Assert.DoesNotContain(n, town));
    }
}
