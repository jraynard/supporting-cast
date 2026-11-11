using SupportingCast.Sim.Components;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Scenarios;

namespace SupportingCast.Sim.Tests;

/// <summary>
/// The village a round is played in, left to its AI villagers for a whole round's worth of days.
/// A trade nobody took has to keep getting done, so a town that can't feed and house itself
/// unattended is a bug. When this fails, read the message log: it's usually real AI behaviour.
/// </summary>
public class VillageDayScenarioTests
{
    private const int Days = 4;

    [Theory]
    [InlineData(1234UL)]
    [InlineData(99UL)]
    public void TheVillageSurvivesARoundUnattended(ulong seed)
    {
        var sim = Simulation.CreateStandard(TestWorld.LoadDefs(), 160, 160, seed, MapGenerator.RecommendedDepth);
        sim.Presets.PresetMode = true;
        var town = VillageRound.Setup(sim);
        Assert.False(town.Tavern.IsNull, "the longhouse should fit near the landing");
        Assert.False(town.Forge.IsNull, "the smithy should fit near the landing");
        Assert.False(town.Shrine.IsNull, "the shrine should fit near the landing");

        sim.TickMany(SimClock.TicksPerDay * Days);

        var w = sim.World;
        var people = sim.Creatures().Where(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Defs.Race.Human }).ToList();
        var log = string.Join("\n", sim.Messages.Messages.TakeLast(30).Select(m => m.Text));
        Assert.True(people.Count >= VillageRound.StartingVillagers, $"{people.Count} villagers left:\n{log}");
        Assert.All(people, e => Assert.False(w.Get<Creature>(e).Dead, $"{w.Get<Creature>(e).Name} died:\n{log}"));
    }

    [Fact]
    public void APlayerWalksIntoTown()
    {
        var sim = Simulation.CreateStandard(TestWorld.LoadDefs(), 160, 160, 1234, MapGenerator.RecommendedDepth);
        sim.Presets.PresetMode = true;
        var town = VillageRound.Setup(sim);
        var me = VillageRound.SpawnHero(sim, new Hero("Arin", null, null), town.Center);

        Assert.True(sim.World.Has<Avatar>(me));
        Assert.Equal("Arin", sim.World.Get<Creature>(me).Name);
        Assert.True(sim.Map.Passable(sim.World.Get<Position>(me).Cell));
    }
}
