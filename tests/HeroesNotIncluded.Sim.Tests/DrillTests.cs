using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Environment;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Tinkering;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The dragon and bridge drills (<c>--dragon-test</c>, <c>--bridge-test</c>), for the tuning playtest's solo checks.</summary>
public class DrillTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private static (Simulation Sim, Entity Me) Round(string trade)
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(trade)], []));
        sim.ApplyNow(new StartRoundCommand());
        return (sim, Players.CharacterOf(sim, Ana));
    }

    [Fact]
    public void TheDragonIsComingAndYouStandAtTheWellWithAFullBucket()
    {
        var (sim, me) = Round("Blacksmith");
        var dragon = DragonDrill.Stage(sim, me);
        Assert.NotNull(dragon);
        Assert.Contains(sim.Director.Active, c => c.Kind == CrisisKind.Dragon);
        Assert.True(sim.World.IsAlive(dragon.Dragon));
        Assert.True(Buckets.IsFull(sim, sim.World.Get<Carrying>(me).Item));
        var at = sim.World.Get<Position>(me).Cell;
        Assert.Contains(Buckets.Wells(sim), well => AvatarSystem.InReach(sim, at, well));
    }

    [Fact]
    public void TheBridgeIsDownAndTheTinkerStandsAtTheGapWithWoodToMendIt()
    {
        var (sim, me) = Round("Tinker");
        var breach = BridgeDrill.Stage(sim, me);
        Assert.NotNull(breach);
        Assert.True(sim.Repairs.RoadCut);
        Assert.Equal("Tinker", sim.World.Get<Trade>(me).Def.Name);
        Assert.True(Wagons.WoodOn(sim, me) >= Repairs.MendWood);
        Assert.Null(Repairs.MendProblem(sim, me, breach));
    }
}
