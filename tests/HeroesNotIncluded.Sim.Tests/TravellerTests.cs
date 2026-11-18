using HeroesNotIncluded.Sim.Adventurers;
using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Hospitality;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Lone travellers passing through (<see cref="PartyDef.Traveller"/>), and guests turning in at an
/// hour of their own (<see cref="ShopSystem.Bedtime"/>): the inn's rooms let through the evening,
/// whatever party is or isn't in town.
/// </summary>
public class TravellerTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>A round with the player as the healer: the inn's kept by its AI keeper.</summary>
    private static Simulation Round(ulong seed = 1234)
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), seed);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Arin", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade("Empath")], []));
        sim.ApplyNow(new StartRoundCommand());
        return sim;
    }

    private static List<Entity> Travellers(Simulation sim) =>
        sim.World.Query<Adventurer>().Where(e => sim.World.Get<Adventurer>(e).PartyDef.Traveller && !sim.World.Get<Creature>(e).Dead).ToList();

    private static PartyDef Kind(Simulation sim, string name = "Pedlar") => sim.Defs.Party(name);

    [Fact]
    public void TravellersComeOnlyOfAnAfternoonAndNeverTooManyAtOnce()
    {
        var sim = Round();
        sim.Parties.HoldArrivals(); // parties aside: only the travellers' own schedule
        var held = sim.Parties.NextTravellerTick;
        Assert.Equal(long.MaxValue, held);

        // A fresh round's schedule, run for two days with the parties held off.
        sim = Round();
        var seen = new HashSet<Entity>();
        var most = 0;
        for (var t = 0; t < SimClock.TicksPerDay * 3; t += SimClock.TicksPerHour / 4)
        {
            sim.TickMany(SimClock.TicksPerHour / 4);
            foreach (var e in Travellers(sim))
                if (seen.Add(e))
                {
                    var hour = SimClock.TicksToDays(sim.World.Get<Adventurer>(e).Arrived + sim.Clock.StartHour * (long)SimClock.TicksPerHour) % 1f * 24f;
                    Assert.InRange(hour, PartySystem.TravellerFromHour, PartySystem.TravellerUntilHour + 0.3f);
                }
            most = Math.Max(most, PartySystem.TravellersPresent(sim));
        }
        Assert.True(seen.Count >= 2, $"{seen.Count} travellers in three days");
        Assert.InRange(most, 1, PartySystem.MaxTravellers);
    }

    [Fact]
    public void ATravellerNeverAsksForAQuestAndCantBeSentOnOne()
    {
        var sim = Round();
        var me = Players.CharacterOf(sim, Ana);
        var pedlar = sim.Parties.Send(sim, Kind(sim)).Single();
        sim.World.Get<Adventurer>(pedlar).Arrived -= SimClock.TicksPerDay; // long enough in town that an adventurer would want work
        sim.TickMany(SimClock.TicksPerHour * 3);
        Assert.DoesNotContain(sim.Shops.All, o => o.Poster == pedlar && o.Kind.Kind == ServiceKind.Quest);
        var quest = sim.Expeditions.Quests.FirstOrDefault(q => q.Status == Expeditions.QuestStatus.Open);
        if (quest != null)
        {
            sim.World.Get<Position>(me).Cell = sim.World.Get<Position>(pedlar).Cell + new int3(1, 0, 0);
            sim.World.Get<Position>(me).Pos = Position.CenterOf(sim.World.Get<Position>(me).Cell);
            Assert.False(sim.Expeditions.Offer(sim, me, pedlar, quest.Id));
        }
    }

    [Fact]
    public void ATravellerTakesARoomAndLeavesTheMorningAfterTheirLastNight()
    {
        var sim = Round();
        var keeper = sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Innkeeper");
        Assert.NotEqual(Players.CharacterOf(sim, Ana), keeper); // the AI keeps the inn here
        // A pilgrim (one night) arrives at four in the afternoon.
        while (sim.Clock.HourOfDay < 16)
            sim.TickMany(SimClock.TicksPerHour / 4);
        var pilgrim = sim.Parties.Send(sim, Kind(sim, "Pilgrim")).Single();
        var arrivedDay = sim.Clock.Day;
        Inventory.Stow(sim, pilgrim, sim.CreateItem(sim.Thing(Coin.Silver), 10)); // whatever they spend on supper
        var leave = sim.World.Get<Adventurer>(pilgrim).LeaveTick;

        // That evening: a room asked for, and let.
        var bed = Entity.Null;
        for (var t = 0; t < SimClock.TicksPerHour * 10 && bed.IsNull; t += 60)
        {
            sim.TickMany(60);
            if (Lodging.Lodged(sim, pilgrim))
                bed = sim.World.Get<LodgeStay>(pilgrim).Bed;
        }
        Assert.False(bed.IsNull, "the pilgrim took a room");

        // Gone the next morning, between eight and ten: checked out, their bed left unmade.
        sim.TickMany((int)(leave - sim.Clock.Tick) + SimClock.TicksPerHour / 2);
        Assert.Equal(arrivedDay + 1, sim.Clock.Day);
        Assert.InRange(sim.Clock.HourOfDay, PartySystem.TravellerLeavesFrom, PartySystem.TravellerLeavesBy + 0.6f);
        Assert.True(!sim.World.IsAlive(pilgrim) || sim.World.Has<Leaving>(pilgrim), "on their way, or gone");
        Assert.False(sim.World.IsAlive(pilgrim) && sim.World.Has<LodgeStay>(pilgrim), "checked out");
        Assert.True(Lodging.IsUnmade(sim, bed), "slept in and not made up");
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("pilgrim moves on"));
    }

    [Fact]
    public void APartyStillComesWhileATravellerIsInTown()
    {
        var sim = Round();
        sim.Parties.Send(sim, Kind(sim));
        Assert.False(PartySystem.Present(sim), "a traveller holds no party back");
        for (var t = 0; t < SimClock.HoursToTicks(PartySystem.FirstArrivalHours + 1) && sim.Parties.PartiesSent == 0; t += 60)
            sim.TickMany(60);
        Assert.Equal(1, sim.Parties.PartiesSent);
        Assert.True(PartySystem.Present(sim));
    }

    [Fact]
    public void GuestsTurnInAtHoursOfTheirOwnAndAllHaveAskedByNightfall()
    {
        var sim = Round();
        var party = sim.Parties.Send(sim, sim.Defs.Party("IronLantern"));
        foreach (var a in party)
        {
            sim.World.Get<Adventurer>(a).LeaveTick = sim.Clock.Tick + SimClock.TicksPerDay * 2; // staying the night
            sim.World.Get<Adventurer>(a).Arrived = sim.Clock.Tick + SimClock.TicksPerDay; // and not after a quest today
        }
        var bedtimes = party.Select(e => ShopSystem.Bedtime(sim, e)).ToList();
        Assert.All(bedtimes, b => Assert.InRange(b, ShopSystem.LodgingFromHour, ShopSystem.LodgingFromHour + ShopSystem.BedtimeSpread));
        Assert.True(bedtimes.Distinct().Count() > 1, "not all at once");
        // The same on every peer: derived, not rolled.
        Assert.Equal(bedtimes, party.Select(e => ShopSystem.Bedtime(sim, e)));

        var asked = new Dictionary<Entity, float>();
        while (sim.Clock.HourOfDay < ShopSystem.LodgingFromHour)
            sim.TickMany(SimClock.TicksPerHour / 4);
        foreach (var e in party)
            Inventory.Stow(sim, e, sim.CreateItem(sim.Thing(Coin.Silver), 20)); // whatever they've drunk, enough for a bed
        while (sim.Clock.HourOfDay < 22.5f && sim.Clock.HourOfDay >= ShopSystem.LodgingFromHour)
        {
            sim.TickMany(60);
            foreach (var e in party)
                if (!asked.ContainsKey(e) && sim.Shops.All.Any(o => o.Poster == e && o.Kind.Kind == ServiceKind.Lodging))
                    asked[e] = sim.Clock.HourOfDay;
        }
        var alive = party.Where(e => !sim.World.Get<Creature>(e).Dead).ToList();
        Assert.All(alive, e => Assert.True(asked.ContainsKey(e), $"{sim.World.Get<Creature>(e).Name} hadn't asked by 22:30 (bedtime {ShopSystem.Bedtime(sim, e):F2}), {sim.World.Get<Mind>(e).Job?.Report}, coin {Coin.Count(sim, e)}, lodged {Lodging.Lodged(sim, e)}, leaving {sim.World.Has<Leaving>(e)}, quest {sim.World.Get<Adventurer>(e).Quest}, orders {string.Join(",", sim.Shops.All.Where(o => o.Poster == e).Select(o => o.Kind.Kind + ":" + o.Status))}"));
        Assert.All(alive, e => Assert.True(asked[e] >= ShopSystem.Bedtime(sim, e), "not before their bedtime"));
        Assert.True(asked.Values.Max() - asked.Values.Min() >= 0.5f, $"asked at {string.Join(", ", asked.Values.Select(h => h.ToString("F2")))}");
    }

    [Fact]
    public void ATravellerInTownAndTheNextOneDueSurviveASave()
    {
        var sim = Round();
        while (sim.Clock.HourOfDay < 15)
            sim.TickMany(SimClock.TicksPerHour / 4);
        var drover = sim.Parties.Send(sim, Kind(sim, "Drover")).Single();
        sim.TickMany(SimClock.TicksPerHour / 2);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(sim.Parties.NextTravellerTick, loaded.Parties.NextTravellerTick);
        Assert.True(loaded.World.Get<Adventurer>(drover).PartyDef.Traveller);
        Assert.Equal(sim.World.Get<Adventurer>(drover).LeaveTick, loaded.World.Get<Adventurer>(drover).LeaveTick);
        // And they go on the same: the next traveller comes alike in both.
        sim.TickMany(SimClock.TicksPerDay);
        loaded.TickMany(SimClock.TicksPerDay);
        Assert.Equal(sim.Parties.NextTravellerTick, loaded.Parties.NextTravellerTick);
    }
}
