using HeroesNotIncluded.Sim.Afterlife;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Tinkering;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The pairs drill (<c>--pairs-test</c>): every job for two pairs of hands, ready at once.</summary>
public class PairsDrillTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    [Fact]
    public void EveryJobForTwoIsStagedOnMillbrook()
    {
        var (sim, _) = TestWorld.Millbrook(7, 4);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        // Before the deal it does nothing (the command waits for a live round).
        sim.ApplyNow(new DevPairsDrillCommand());
        Assert.Empty(sim.World.Query<Wagon>());
        sim.ApplyNow(new StartRoundCommand());

        var staged = PairsDrill.Stage(sim);
        Assert.Equal(4, staged.Count);
        var w = sim.World;

        var smith = Players.CharacterOf(sim, Ana);
        Assert.True(RecipeBooks.Knows(sim, smith, sim.Defs.Recipe("ForgeGreatsword")), "the greatsword's known");
        var wagon = Assert.Single(w.Query<Wagon>());
        Assert.Equal(WagonDamage.Axle, w.Get<Wagon>(wagon).Damage);
        var dead = Assert.Single(w.Query<Death>());
        Assert.True(Revival.OnShrinePallet(sim, dead), "laid on a shrine pallet, where it keeps");
        Assert.True(Revival.Fresh(sim, dead));
        Assert.All(w.Query<Mana>(), e => Assert.Equal(w.Get<Mana>(e).Max, w.Get<Mana>(e).Current));
        Assert.Contains(sim.Messages.Messages, m => m.Text.StartsWith("Work for two pairs of hands, ready"));
    }
}
