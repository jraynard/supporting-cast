using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Director;
using HeroesNotIncluded.Sim.Environment;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The fire drill (<c>--fire-test</c>): the inn alight as a real crisis, and you at the well with water.</summary>
public class FireDrillTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    [Theory]
    [InlineData("Millbrook")]
    [InlineData("Reedmere")]
    public void TheInnIsAlightAndYouStandAtTheWellWithAFullBucket(string map)
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234, map);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim, map);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new StartRoundCommand());
        var me = Players.CharacterOf(sim, Ana);

        var fire = FireDrill.Stage(sim, me);
        Assert.NotNull(fire);
        var inn = fire.Building;
        Assert.Equal(ShopSystem.BuildingFor(sim, TownRole.Tavern), inn);
        var burning = sim.Fire.BurningTiles.Count(i => Buildings.At(sim, sim.Map.CellOf(i)) == inn);
        Assert.True(burning >= 2, $"{burning} cells alight in the inn");

        Assert.True(Buckets.IsFull(sim, sim.World.Get<Carrying>(me).Item));
        var at = sim.World.Get<Position>(me).Cell;
        Assert.Contains(Buckets.Wells(sim), well => AvatarSystem.InReach(sim, at, well));

        // It can be swung at once, to throw at the fire (the buttons, as a bow's).
        Assert.True(Buckets.Prime(sim, me, true, sim.Clock.Tick));
    }
}
