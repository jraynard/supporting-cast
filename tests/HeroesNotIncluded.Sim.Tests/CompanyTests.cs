using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Expeditions;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;
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

    [Fact]
    public void TheGreyWandererComesInGreyAndAlwaysOldAndBearded()
    {
        var (sim, _, _) = ExpeditionTests.Round("Innkeeper", tillTheyAsk: false);
        var w = sim.World;
        var wanderer = sim.Parties.Send(sim, Company(sim, LongRoad))[0];
        var robe = w.Get<Thing>(Clothing.In(sim, wanderer, ApparelSlot.Tunic));
        Assert.Equal(("WizardRobe", "Ash"), (robe.Def.Name, robe.Material?.Name));
        var hat = w.Get<Thing>(Clothing.In(sim, wanderer, ApparelSlot.Head));
        Assert.Equal(("WizardHat", "Ash"), (hat.Def.Name, hat.Material?.Name));
        var kin = w.Get<Kin>(wanderer);
        Assert.Equal((Folk.Human, BodyType.Male, HairStyle.Long, BeardStyle.Full, 0xB8B8B4u), (kin.Folk, kin.Body, kin.Hair, kin.Beard, kin.HairColor));
        Assert.Equal(BodyType.Male, Clothing.BodyOf(sim, wanderer));
        var loaded = SaveLoadTests.RoundTrip(sim);
        var again = loaded.World.Get<Kin>(loaded.Creatures().Single(e => loaded.World.Get<Creature>(e).Name == w.Get<Creature>(wanderer).Name));
        Assert.Equal((kin.Folk, kin.Body, kin.Hair, kin.Beard, kin.HairColor), (again.Folk, again.Body, again.Hair, again.Beard, again.HairColor));
        // The rest of the long road come as themselves: the halflings barefoot under their hooded cloaks.
        var halflings = sim.Creatures().Where(e => w.TryGet<Adventurer>(e, out var a) && a.Kind.Name == "Burrower").ToList();
        Assert.NotEmpty(halflings);
        Assert.All(halflings, e => Assert.True(Clothing.In(sim, e, ApparelSlot.Feet).IsNull));
        Assert.All(halflings, e => Assert.Equal("HoodedCloak", w.Get<Thing>(Clothing.In(sim, e, ApparelSlot.Cloak)).Def.Name));
    }

    [Fact]
    public void DwarvesWearHoodsOrHelmsAndComeBackFromTheRoadAsThemselves()
    {
        var (sim, me, ordinary) = ExpeditionTests.Round("Innkeeper", tillTheyAsk: false);
        var w = sim.World;
        foreach (var e in ordinary) // the test's own party moves on, so the company has the board to itself
            w.Get<Adventurer>(e).LeaveTick = 0;
        // Dressing never leaves a piece lying in the road (a helm put on over a hood would; so would
        // their clothes put back on over a fresh outfit when they return).
        HashSet<Entity> Loose() => w.Query<Thing>().Where(e => w.Get<Thing>(e).Spawned && Clothing.IsClothes(w.Get<Thing>(e).Def)).ToHashSet();
        var loose = Loose();
        var company = sim.Parties.Send(sim, Company(sim, Dwarves));
        Tinkering.Wagons.Leave(sim, w.Get<Adventurer>(company[0]).Party);
        Assert.Subset(loose, Loose());
        string? Head(Entity e) => w.TryGet<Apparel>(e, out var worn) && w.IsAlive(worn[ApparelSlot.Head]) ? w.Get<Thing>(worn[ApparelSlot.Head]).Def.Name : null;
        string KindOf(Entity e) => w.Get<Adventurer>(e).Kind.Name;
        Assert.All(company.Where(e => KindOf(e) == "AxeHand"), e => Assert.Equal("DwarfHood", Head(e)));
        Assert.All(company.Where(e => KindOf(e) is "ExiledHeir" or "Graybeard"), e => Assert.Equal("IronHelm", Head(e)));
        Assert.All(company.Where(e => KindOf(e) != "Burrower"), e => Assert.NotEqual(BeardStyle.None, w.Get<Kin>(e).Beard ?? BeardStyle.None));

        var party = w.Get<Adventurer>(company[0]).Party;
        for (var t = 0; t < 60 && ExpeditionSystem.QuestOrder(sim, party) == null; t++)
            sim.TickMany(SimClock.TicksPerHour / 4);
        var kinds = company.ToDictionary(e => w.Get<Creature>(e).Name, KindOf);
        ExpeditionTests.SendAndWait(sim, me, company, sim.Expeditions.OwnQuest(Company(sim, Dwarves)));
        var back = sim.Creatures().Where(e => w.TryGet<Adventurer>(e, out var a) && a.Party == party).ToList();
        Assert.NotEmpty(back);
        Assert.All(back, e => Assert.Equal(kinds[w.Get<Creature>(e).Name] == "Burrower" ? Folk.Halfling : Folk.Dwarf, w.Get<Kin>(e).Folk));
        Assert.All(back.Where(e => KindOf(e) == "AxeHand"), e => Assert.Equal("DwarfHood", Head(e)));
        Assert.Subset(loose, Loose());
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
        // Past when the first party would come (a company due would come then, before them)...
        for (var i = 0; i < 4 * (int)(HeroesNotIncluded.Sim.Adventurers.PartySystem.FirstArrivalHours + 1); i++)
        {
            sim.TickMany(SimClock.TicksPerHour / 4);
            Assert.False(Here(), "not before its day");
        }
        // ...and whether it's due is the day alone: on to the end of the first.
        sim.Clock.Tick = sim.Round.TickAt(sim, 2, 0) - 2;
        sim.Tick();
        Assert.False(Here(), "not before its day");
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
        // No rare company of the round's own besides the one sent (its dice may bring one on day 1).
        var (sim, me, _) = ExpeditionTests.Round("Innkeeper", beforeStart: s => s.Parties.SetRare(Company(s, Dwarves), 99));
        var w = sim.World;
        var company = sim.Parties.Send(sim, Company(sim, Dwarves));
        var party = w.Get<Adventurer>(company[0]).Party;
        // Silver enough for a broker's word, whatever they spend on the way (what's tested is where they go, not their purse).
        foreach (var m in company)
            HeroesNotIncluded.Sim.Systems.Inventory.Stow(sim, m, sim.CreateItem(sim.Defs.Thing(HeroesNotIncluded.Sim.Shops.Coin.Silver), 40));
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
