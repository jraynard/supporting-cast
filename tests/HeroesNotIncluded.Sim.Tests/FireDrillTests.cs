using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Director;
using HeroesNotIncluded.Sim.Environment;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The fire drill (<c>--fire-test</c>): the inn alight as a real crisis, and you at the well with water.</summary>
public class FireDrillTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    [Fact]
    public void TheInnIsAlightAndYouStandAtTheWellWithAFullBucket()
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new StartRoundCommand());
        var me = Players.CharacterOf(sim, Ana);

        var fire = FireDrill.Stage(sim, me);
        Assert.NotNull(fire);
        var inn = fire.Building;
        Assert.Equal(FireDrill.Building, sim.World.Get<PresetBuilding>(inn).Def.Name);
        var burning = sim.Fire.BurningTiles.Count(i => Buildings.At(sim, sim.Map.CellOf(i)) == inn);
        Assert.True(burning >= 2, $"{burning} cells alight in the inn");

        Assert.True(Buckets.IsFull(sim, sim.World.Get<Carrying>(me).Item));
        var at = sim.World.Get<Position>(me).Cell;
        Assert.Contains(Buckets.Wells(sim), well => AvatarSystem.InReach(sim, at, well));

        // A throw at the fire from here is offered, if it's within a throw.
        var near = sim.Fire.BurningTiles.Select(i => sim.Map.CellOf(i)).OrderBy(c => Maps.Map.Distance(c, at)).First();
        if (Maps.Map.Distance(new int3(near.X, near.Y, at.Z), at) <= Buckets.MaxRange)
            Assert.Equal(AvatarActionKind.ThrowWater, AvatarSystem.ActionAt(sim, me, near).Kind);
    }
}
