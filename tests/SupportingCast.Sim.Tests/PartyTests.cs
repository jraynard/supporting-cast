using SupportingCast.Sim.Adventurers;
using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Round;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Tests;

public class PartyTests
{
    private static (Simulation Sim, VillageRound.Town Town) Town(ulong seed = 1234, bool round = true)
    {
        var sim = Simulation.CreateStandard(TestWorld.LoadDefs(), 160, 160, seed, MapGenerator.RecommendedDepth);
        sim.Presets.PresetMode = true;
        var town = VillageRound.Setup(sim);
        if (round)
        {
            sim.Round.OpenLobby();
            sim.ApplyNow(new JoinLobbyCommand(Guid.Empty, "Arin", default, []));
            sim.ApplyNow(new StartRoundCommand());
        }
        return (sim, town);
    }

    private static List<Entity> Adventurers(Simulation sim) =>
        sim.Creatures().Where(e => sim.World.Has<Adventurer>(e) && !sim.World.Get<Creature>(e).Dead).ToList();

    private static int Coins(Simulation sim, Entity who) =>
        sim.StoredIn(sim.PackOf(who)).Where(i => sim.World.Get<Thing>(i).Def.Name == "SilverCoin").Sum(i => sim.World.Get<Stack>(i).Count);

    [Fact]
    public void NoPartyComesWithoutARound()
    {
        var (sim, _) = Town(round: false);
        sim.TickMany(SimClock.TicksPerDay);
        Assert.Empty(Adventurers(sim));
        Assert.Equal(0, sim.Parties.PartiesSent);
    }

    [Fact]
    public void APartyArrivesArmedAndPayingAndMakesForTheTavern()
    {
        var (sim, town) = Town();
        sim.TickMany(SimClock.HoursToTicks(PartySystem.FirstArrivalHours + 0.5f));

        var party = Adventurers(sim);
        Assert.NotEmpty(party);
        Assert.Equal(1, sim.Parties.PartiesSent);
        var w = sim.World;
        Assert.All(party, e =>
        {
            Assert.Equal(Faction.Adventurer, w.Get<Creature>(e).Faction);
            Assert.True(RaiderBrain.Armed(sim, e), $"{w.Get<Creature>(e).Name} came armed");
            Assert.True(Coins(sim, e) > 0, $"{w.Get<Creature>(e).Name} came with coin");
        });
        Assert.Single(party.Select(e => w.Get<Adventurer>(e).Party).Distinct());
        Assert.False(Factions.Hostile(Faction.Village, Faction.Adventurer));

        sim.TickMany(SimClock.HoursToTicks(3));
        var tavern = w.Get<PresetBuilding>(town.Tavern).Anchor;
        var near = party.Count(e => Map.Distance(w.Get<Position>(e).Cell, tavern) <= 10);
        Assert.True(near * 2 > party.Count, $"only {near} of {party.Count} adventurers made it to the tavern");
    }

    [Fact]
    public void APartyLeavesTheMapWhenItsStayIsUp()
    {
        var (sim, _) = Town();
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        Assert.NotEmpty(party);
        sim.TickMany(SimClock.HoursToTicks(1));
        foreach (var e in party)
            sim.World.Get<Adventurer>(e).LeaveTick = sim.Clock.Tick;

        sim.TickMany(SimClock.HoursToTicks(4));
        Assert.All(party, e => Assert.False(sim.World.IsAlive(e), "everyone should have walked off the map"));
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("moving on"));
    }

    [Fact]
    public void AdventurersAndVillagersKeepTheirCoinBetweenJobs()
    {
        var (sim, _) = Town();
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        var villager = sim.Creatures().First(e => sim.World.Get<Creature>(e).Faction == Faction.Village && !sim.World.Has<Avatar>(e));
        sim.GiveStartingPack(villager);
        Inventory.Stow(sim, villager, sim.CreateItem(sim.Defs.Thing("SilverCoin"), 7));
        int Held() => sim.Creatures().Sum(e => Coins(sim, e));
        var held = Held();

        // They'll spend some on drinks: but coin only ever changes hands, never hits the floor.
        sim.TickMany(SimClock.HoursToTicks(4));

        Assert.Equal(held, Held());
        Assert.DoesNotContain(sim.SpawnedItems(), e => sim.World.Get<Thing>(e).Def.Name == "SilverCoin");
        Assert.True(Coins(sim, villager) >= 7);
    }
}
