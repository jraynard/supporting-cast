using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Expeditions;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Tinkering;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Bots at their workshops between orders (<see cref="BotPlayer"/>, "Goods", "Forge", "Shrine",
/// "Bench"): the smith's blades, the empath's charms and the tinker's contraptions made, put on
/// show and sold; a lock pinned and fitted; a curio bought by a greedy tinker.
/// </summary>
public class BotBenchTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>A dealt round with Ana in <paramref name="trade"/>, played by a bot.</summary>
    private static (Simulation Sim, Entity Me, BotPlayer Bot) Round(string trade, BotTemperament? temper = null)
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(trade)], []));
        var bot = new BotPlayer(() => sim, Ana, c => sim.ApplyNow(c), 7) { Temper = temper ?? BotTemperament.Steady };
        sim.ApplyNow(new StartRoundCommand());
        return (sim, Players.CharacterOf(sim, Ana), bot);
    }

    private static bool Run(Simulation sim, BotPlayer bot, Func<bool> done, int maxTicks)
    {
        for (var t = 0; t < maxTicks && !done(); t++)
        {
            sim.Tick();
            bot.Tick();
        }
        return done();
    }

    private static List<Entity> OnShowBy(Simulation sim, Entity seller) =>
        Goods.OnShow(sim).Where(g => sim.World.Get<ForSale>(g.Item).Seller == seller).Select(g => g.Item).ToList();

    private static string Why(Simulation sim, BotPlayer bot) =>
        $"bot: {bot.Doing}\n" + string.Join("\n", sim.Messages.Messages.TakeLast(12).Select(m => m.Text));

    /// <summary>A party in town that knows its way about, and no quest to send it off on: custom that stays.</summary>
    private static List<Entity> PartyInTown(Simulation sim, string party)
    {
        foreach (var q in sim.Expeditions.Quests)
            q.Status = QuestStatus.Cleared;
        var members = sim.Parties.Send(sim, sim.Defs.Party(party));
        Wagons.Leave(sim, sim.World.Get<Adventurer>(members[0]).Party);
        foreach (var e in members)
            Hospitality.DirectionsSystem.KnowsTheTown(sim, e);
        return members;
    }

    /// <summary>Pieces of its own making on it, not yet out.</summary>
    private static List<Entity> MadeAndWaiting(Simulation sim, Entity me) =>
        sim.StoredIn(sim.PackOf(me)).Where(e => sim.World.TryGet<Crafted>(e, out var c) && c.Maker == me
            || sim.World.TryGet<Charm>(e, out var ch) && ch.Maker == me).ToList();

    [Fact]
    public void TheSmithBotForgesForTheShelfButKeepsItBackWithNobodyAbout()
    {
        var (sim, me, bot) = Round("Blacksmith");
        Inventory.Stow(sim, me, sim.CreateItem(sim.Defs.Thing("IronBar"), 12));
        Assert.True(Run(sim, bot, () => MadeAndWaiting(sim, me).Count >= 1, SimClock.TicksPerHour * 6), Why(sim, bot));
        Run(sim, bot, () => false, SimClock.TicksPerHour * 6);
        var made = MadeAndWaiting(sim, me);
        Assert.InRange(made.Count, 1, BotPlayer.StockOnShow); // a small stock
        Assert.Equal(made.Count, made.Select(e => sim.World.Get<Thing>(e).Def).Distinct().Count()); // no two alike
        Assert.Empty(OnShowBy(sim, me)); // nobody to sell to: a party could be up the road any time
    }

    [Fact]
    public void TheSmithBotPutsABladeOnShowOnceThePartyHasSettledInAndSellsIt()
    {
        var (sim, me, bot) = Round("Blacksmith");
        Inventory.Stow(sim, me, sim.CreateItem(sim.Defs.Thing("IronBar"), 4));
        var smithy = ShopSystem.BuildingOf(sim, sim.Defs.Building("Smithy"));
        Assert.True(Run(sim, bot, () => MadeAndWaiting(sim, me).Count >= 1, SimClock.TicksPerHour * 6), Why(sim, bot));

        // A party in town: once they've had time to see the innkeeper about work, it goes out, and the
        // one with the club wants it; the smith hands it over for the price.
        PartyInTown(sim, "GildedSpurs");
        var purse = Coin.Count(sim, me);
        var seen = new HashSet<Entity>();
        Order? Sold() => sim.Shops.All.FirstOrDefault(o => o.Kind.Kind == ServiceKind.Goods && o.Status == OrderStatus.Done && o.Assignee == me);
        Assert.True(Run(sim, bot, () => { seen.UnionWith(OnShowBy(sim, me)); return Sold() != null; }, SimClock.TicksPerHour * 16),
            Why(sim, bot) + "\n" + string.Join("\n", sim.Shops.All.Where(o => o.Kind.Kind == ServiceKind.Goods).Select(o => $"{o.Text} {o.Status} {o.AssigneeName}")));
        var sale = Sold()!;
        Assert.Contains(sale.Subject, seen);
        Assert.Equal(smithy, sale.Where);
        Assert.Equal(me, sim.World.Get<Crafted>(sale.Subject).Maker);
        Assert.True(Inventory.OnPerson(sim, sale.Poster, sale.Subject));
        Assert.True(Coin.Count(sim, me) >= purse + sale.Price);
    }

    [Fact]
    public void TheHealerBotWeavesACharmAndPutsItOnShowAtTheShrine()
    {
        var (sim, me, bot) = Round("Healer");
        var shrine = ShopSystem.BuildingOf(sim, sim.Defs.Building("Shrine"));
        Assert.True(Run(sim, bot, () => Healing.Charms.Carried(sim, me).Any(c => sim.World.Get<Charm>(c).Maker == me), SimClock.TicksPerHour * 4), Why(sim, bot));
        PartyInTown(sim, "IronLantern");
        Assert.True(Run(sim, bot, () => OnShowBy(sim, me).Count > 0, SimClock.TicksPerHour * 10), Why(sim, bot));
        var charm = OnShowBy(sim, me)[0];
        Assert.Equal(shrine, Goods.ShownIn(sim, charm));
        Assert.Equal(me, sim.World.Get<Charm>(charm).Maker);
        Assert.True(sim.World.Get<Charm>(charm).Mending.Heals.Count > 0);
        var shown = OnShowBy(sim, me);
        Assert.Equal(shown.Count, shown.Select(e => (sim.World.Get<Thing>(e).Label, sim.World.Get<ForSale>(e).Price)).Distinct().Count()); // a buyer's word means one
    }

    [Fact]
    public void TheHealerBotTearsACharmItKnowsCameOutLoose()
    {
        var (sim, me, bot) = Round("Healer");
        var loose = sim.CreateItem(sim.Defs.Thing(Healing.Charms.Def), 1);
        sim.World.Add(loose, new Charm { Mending = sim.Defs.AllMendings.First(m => m.Heals.Count > 0), Quality = 0.1f, Maker = me });
        Inventory.Stow(sim, me, loose);
        Assert.True(Run(sim, bot, () => !sim.World.IsAlive(loose), SimClock.TicksPerHour), Why(sim, bot));
        Assert.DoesNotContain(loose, Goods.OnShow(sim).Select(g => g.Item));
    }

    [Fact]
    public void TheTinkerBotPinsALockFitsItToItsDoorAndPutsASpareKeyBy()
    {
        var (sim, me, bot) = Round("Tinker");
        var shop = ShopSystem.WorkplaceOf(sim, sim.World.Get<Trade>(me).Def);
        bool Fitted() => Buildings.Parts(sim, shop).Any(e => sim.World.TryGet<LockCut>(e, out var c) && c.Maker == me && Locksmithing.IsDoor(sim, e));
        Assert.True(Run(sim, bot, Fitted, SimClock.TicksPerHour * 4), Why(sim, bot));
        var door = Buildings.Parts(sim, shop).First(e => sim.World.TryGet<LockCut>(e, out var c) && c.Maker == me);
        var seed = sim.World.Get<LockCut>(door).Seed;
        Assert.False(Locksmithing.KeyFor(sim, me, seed).IsNull);
        Assert.False(sim.IsLocked(door), "fitted, not locked: nobody's way about town changes");

        var chest = Buildings.Parts(sim, shop).First(e => sim.World.Get<Thing>(e).Def.Name == "Chest");
        Assert.True(Run(sim, bot, () => sim.StoredIn(chest).Any(k => sim.World.TryGet<Key>(k, out var key) && key.Seed == seed), SimClock.TicksPerHour * 3), Why(sim, bot));
        Assert.True(bot.Stats.Sessions.GetValueOrDefault(nameof(PinSession)) >= 2, "the lock pinned and the key filed by hand");
    }

    [Fact]
    public void TheTinkerBotFitsAContraptionTogetherAndPutsItOnShow()
    {
        var (sim, me, bot) = Round("Tinker");
        Assert.True(Run(sim, bot, () => Contraptions.Carried(sim, me).Count > 0, SimClock.TicksPerHour * 8), Why(sim, bot));
        Assert.True(bot.Stats.Sessions.GetValueOrDefault(nameof(FitSession)) >= 1);
        PartyInTown(sim, "IronLantern");
        Assert.True(Run(sim, bot, () => OnShowBy(sim, me).Any(e => sim.World.Get<Thing>(e).Def.Contraption != null), SimClock.TicksPerHour * 10), Why(sim, bot));
    }

    [Fact]
    public void AGreedyTinkerBotTalksACurioDownBuysItAndCratesIt()
    {
        var greedy = BotTemperament.Steady with { Name = "Greedy", Greed = 1f };
        var (sim, me, bot) = Round("Tinker", greedy);
        foreach (var q in sim.Expeditions.Quests)
            q.Status = QuestStatus.Cleared; // nobody sent off mid-test
        var party = sim.Parties.Send(sim, sim.Defs.Party("IronLantern"));
        Wagons.Leave(sim, sim.World.Get<Adventurer>(party[0]).Party);
        foreach (var e in party)
            Hospitality.DirectionsSystem.KnowsTheTown(sim, e);
        var owner = party[0];
        Inventory.Stow(sim, owner, sim.CreateItem(sim.Defs.Thing(Coin.Silver), 20));
        var bird = sim.CreateItem(sim.Defs.Thing("ClockworkBird"), 1);
        sim.World.Add(bird, new Curio { Worth = 40, Face = 40, Genuine = true, Seed = 0x1234_5679u });
        sim.World.Add(bird, new Valued { Worth = 40, ToldBy = Entity.Null });
        Inventory.Stow(sim, owner, bird);
        Inventory.Stow(sim, me, sim.CreateItem(sim.Defs.Thing(Coin.Silver), 40));
        var crate = sim.World.Query<Thing>().Single(e => sim.World.Get<Thing>(e).Def.Name == CollectorSystem.CrateDef);

        Assert.True(Run(sim, bot, () => sim.StoredIn(crate).Contains(bird), SimClock.TicksPerHour * 10), Why(sim, bot));
        var valued = sim.World.Get<Valued>(bird);
        Assert.Equal(me, valued.ToldBy);
        Assert.True(valued.Worth < 40, $"talked down, paid {valued.Worth}");
    }
}
