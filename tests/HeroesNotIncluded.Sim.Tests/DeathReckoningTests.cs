using HeroesNotIncluded.Sim.Afterlife;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>What a death costs the town, and what the reveal says of the dead (docs/DESIGN.md, "Death").</summary>
public class DeathReckoningTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private static Simulation Round(string trade = "Blacksmith")
    {
        var (sim, _) = TestWorld.Millbrook(7, 4);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(trade)], []));
        sim.ApplyNow(new StartRoundCommand());
        return sim;
    }

    private static void Kill(Simulation sim, Entity e)
    {
        ref var c = ref sim.World.Get<Creature>(e);
        NeedsSystem.Kill(sim, e, ref c, "died");
    }

    [Fact]
    public void ATownDeathRaisesDreadAndACallingBackLiftsSome()
    {
        var sim = Round();
        var season = sim.Defs.Season;
        var before = sim.Reckoning.Dread;
        var ana = Players.CharacterOf(sim, Ana);

        Kill(sim, ana);
        Assert.Equal(before + season.DreadPerTownDeath, sim.Reckoning.Dread, 3);

        Revival.Revive(sim, ana);
        Assert.Equal(before + season.DreadPerTownDeath + season.DreadPerRevival, sim.Reckoning.Dread, 3);
    }

    [Fact]
    public void ADeadSmithsWorkIsTakenUpAtTheNextDawnNotBefore()
    {
        var sim = Round("Blacksmith");
        var w = sim.World;
        var ana = Players.CharacterOf(sim, Ana);
        var smith = sim.Defs.Trade("Blacksmith");
        // Make sure there's a labourer to take it up.
        var hand = sim.Creatures().First(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human } && !w.Has<PlayerCharacter>(e));
        RoundSystem.Assign(sim, hand, sim.Defs.AllTrades.First(t => t.Unlimited));
        Kill(sim, ana);
        bool Held() => sim.Creatures().Any(e => w.TryGet<Trade>(e, out var t) && t.Def == smith);
        Assert.False(Held());

        sim.TickMany(SimClock.TicksPerHour); // the same day: it stands empty
        Assert.False(Held());

        var toDawn = (long)((24 - sim.Clock.HourOfDay + Vacancies.DawnHour + 0.1f) * SimClock.TicksPerHour);
        sim.TickMany((int)toDawn);

        Assert.True(Held());
    }

    [Fact]
    public void TheRevealSaysWhenTheDeadDiedAndWhoCameBack()
    {
        var sim = Round();
        var w = sim.World;
        var ana = Players.CharacterOf(sim, Ana);
        var start = new List<(Entity, int)> { (ana, 0) };
        var villager = sim.Creatures().First(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human } && !w.Has<PlayerCharacter>(e));
        start.Add((villager, 0));
        Kill(sim, ana);
        Kill(sim, villager);
        Revival.Revive(sim, ana);

        var rows = RoundSummary.PeopleOf(sim, start);

        var a = rows.Single(r => r.Name == w.Get<Creature>(ana).Name);
        Assert.True(a.Alive);
        Assert.True(a.CameBack);
        var v = rows.Single(r => r.Name == w.Get<Creature>(villager).Name);
        Assert.False(v.Alive);
        Assert.Equal(1, v.DiedOnDay);
    }
}
