using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Expeditions;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// The rare companies (<see cref="PartyDef.Rare"/>, adventurers.json): a dwarf heir's company and
/// the company of the long road. They come now and then, on their day, as big as the town can take,
/// as their own folk, and they'll go only where they came to go.
/// </summary>
public class CompanyTests
{
    private const string Dwarves = "GreyhornCompany", LongRoad = "LongRoadCompany";

    private static PartyDef Company(Simulation sim, string name) => sim.Defs.Party(name);

    [Fact]
    public void ACompanyComesAsBigAsTheTownCanTake()
    {
        var defs = LoadDefs();
        var dwarves = defs.Party(Dwarves);
        Assert.Equal(4, dwarves.SizeFor(1));
        Assert.Equal(4, dwarves.SizeFor(dwarves.CoreAt));
        Assert.InRange(dwarves.SizeFor(6), 5, 8);
        Assert.Equal(14, dwarves.SizeFor(12));
        Assert.Equal(14, dwarves.SizeFor(20));
        Assert.Equal(9, defs.Party(LongRoad).SizeFor(12));
        // The ordinary parties are as they were.
        Assert.All(defs.AllParties.Where(p => !p.Rare), p => Assert.Equal(p.MemberDefs.Count, p.SizeFor(12)));
    }

    [Fact]
    public void TheDwarvesAreDwarvesAndAnswerToTheirOwnNames()
    {
        var (sim, _, _) = ExpeditionTests.Round("Innkeeper", tillTheyAsk: false);
        var company = sim.Parties.Send(sim, Company(sim, Dwarves));
        Assert.Equal(Company(sim, Dwarves).SizeFor(PartySystemPlayers(sim)), company.Count);
        var w = sim.World;
        Assert.Equal("Brannoc", w.Get<Creature>(company[0]).Name);
        Assert.Equal("Bramble", w.Get<Creature>(company[3]).Name);
        Assert.All(company.Take(3), e => Assert.Equal(Folk.Dwarf, w.Get<Kin>(e).Folk));
        Assert.Equal(Folk.Halfling, w.Get<Kin>(company[3]).Folk);
        // An ordinary party's look is left to them.
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        Assert.All(party, e => Assert.False(w.Has<Kin>(e)));
    }

    private static int PartySystemPlayers(Simulation sim) => Adventurers.PartySystem.Players(sim);

    [Fact]
    public void ARareCompanyComesOnItsDayAndOnlyOnce()
    {
        var (sim, _, _) = ExpeditionTests.Round("Innkeeper", tillTheyAsk: false);
        var w = sim.World;
        // The test's own party moves on now, so the company has the inn to itself.
        foreach (var e in sim.Creatures().Where(e => w.Has<Adventurer>(e)).ToList())
            w.Get<Adventurer>(e).LeaveTick = 0;
        sim.Parties.SetRare(Company(sim, Dwarves), 2);
        bool Here() => sim.Creatures().Any(e => w.TryGet<Adventurer>(e, out var a) && a.PartyDef.Name == Dwarves);
        while (sim.Round.SeasonDay(sim).Day < 2)
        {
            sim.TickMany(SimClock.TicksPerHour / 4);
            Assert.False(Here(), "not before its day");
        }
        for (var t = 0; t < 4 * 16 && !Here(); t++)
            sim.TickMany(SimClock.TicksPerHour / 4);
        Assert.True(Here(), "the company comes on its day");
        Assert.Equal(1, sim.Messages.Messages.Count(m => m.Text.Contains("Greyhorn Company ride in")));
        // Its errand goes up on the board when it comes, and nowhere else.
        Assert.Single(sim.Expeditions.Quests, q => q.Def.Party == Dwarves);
        Assert.DoesNotContain(sim.Expeditions.Quests, q => q.Def.Party == LongRoad);
    }

    [Fact]
    public void AnOrdinaryRoundNeverDrawsACompanysErrand()
    {
        var (sim, _, _) = ExpeditionTests.Round("Innkeeper", tillTheyAsk: false);
        sim.TickMany(SimClock.TicksPerHour / 4);
        Assert.Equal(ExpeditionSystem.QuestsPerRound, sim.Expeditions.Quests.Count);
        Assert.All(sim.Expeditions.Quests, q => Assert.Null(q.Def.Party));
    }

    [Fact]
    public void ACompanyGoesOnlyWhereItCameToGo()
    {
        var (sim, me, _) = ExpeditionTests.Round("Innkeeper");
        var w = sim.World;
        var company = sim.Parties.Send(sim, Company(sim, Dwarves));
        var party = w.Get<Adventurer>(company[0]).Party;
        // Until they've asked for work at the bar.
        for (var t = 0; t < 60 && ExpeditionSystem.QuestOrder(sim, party) == null; t++)
            sim.TickMany(SimClock.TicksPerHour / 4);
        Assert.NotNull(ExpeditionSystem.QuestOrder(sim, party));
        var own = sim.Expeditions.OwnQuest(Company(sim, Dwarves));
        Assert.NotNull(own);
        var other = sim.Expeditions.Quests.First(q => q.Def.Party == null && q.Status == QuestStatus.Open);
        var at = w.Get<Position>(company[0]).Cell;
        var by = sim.SpiralFrom(at, 2).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z);
        w.Get<Position>(me).Cell = by;
        w.Get<Position>(me).Pos = Position.CenterOf(by);
        // Whatever it's said to be, the ordinary quest won't do.
        sim.Expeditions.Hear(me, other.Id, ExpeditionSystem.PartyStrength(sim, party) * 0.85f, null, 1f);
        Assert.False(sim.Expeditions.Offer(sim, me, company[0], other.Id));
        Assert.Contains(sim.Speech.Kept, u => u.Text.Contains("road of our own"));
        // Their own, they'll take even said to be deadly.
        sim.Expeditions.Hear(me, own!.Id, ExpeditionSystem.PartyStrength(sim, party) * 3f, null, 1f);
        Assert.True(sim.Expeditions.Offer(sim, me, company[0], own.Id));
        Assert.Equal(QuestStatus.Taken, own.Status);
    }

    [Fact]
    public void WhetherACompanyComesIsKeptInASave()
    {
        var (sim, _, _) = ExpeditionTests.Round("Innkeeper", tillTheyAsk: false);
        sim.Parties.SetRare(Company(sim, LongRoad), 3);
        var loaded = SaveLoadTests.RoundTrip(sim);
        Assert.Equal((LongRoad, 3), loaded.Parties.RareCompany(loaded) is { } rare ? (rare.Company.Name, rare.Day) : default);
    }
}
