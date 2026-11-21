using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The 3D forge prototype's smithy: a careful smith there forges a whole sword through the real sessions.</summary>
public class ForgeYardTests
{
    [Fact]
    public void ACarefulSmithForgesASword()
    {
        var yard = ForgeYard.Setup(TestWorld.LoadDefs());
        var sim = yard.Sim;
        Assert.NotNull(Smithing.PieceOf(sim, yard.Smith));
        Assert.Equal(yard.Forge, ForgeYard.NextStation(yard));

        new SmithyKit(yard.Forge, yard.Anvil, yard.Tub, Entity.Null).Drive(sim, yard.Smith);

        Assert.Null(Smithing.PieceOf(sim, yard.Smith));
        Assert.Single(sim.World.Query<Crafted>());
        ForgeYard.NewPiece(yard);
        Assert.NotNull(Smithing.PieceOf(sim, yard.Smith));
    }
}
