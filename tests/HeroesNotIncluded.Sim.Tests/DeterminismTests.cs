namespace HeroesNotIncluded.Sim.Tests;

public class DeterminismTests
{
    private static (ulong State, ulong Full) RunGame(ulong seed, int ticks)
    {
        var (sim, _) = TestWorld.Millbrook(seed);
        sim.TickMany(ticks);
        return (sim.StateHash(), sim.FullHash());
    }

    [Fact]
    public void SameSeedSameState()
    {
        var a = RunGame(42, 10_000);
        var b = RunGame(42, 10_000);
        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentSeedsDiverge()
    {
        Assert.NotEqual(RunGame(1, 2_000), RunGame(2, 2_000));
    }
}
