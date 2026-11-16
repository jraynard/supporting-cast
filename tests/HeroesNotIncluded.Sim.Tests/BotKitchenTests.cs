using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The cook bot plays the whole kitchen (<see cref="BotPlayer"/>, "Kitchen"): fires, lids, the spit, the oven, the block.</summary>
public class BotKitchenTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private static (Simulation Sim, BotPlayer Bot, Entity Me, Entity Inn) Cook(BotTemperament? temper = null, float skill = 1f)
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade("Cook")], []));
        var bot = new BotPlayer(() => sim, Ana, c => sim.ApplyNow(c), 7) { Temper = temper ?? BotTemperament.Steady, Skill = skill };
        sim.ApplyNow(new StartRoundCommand());
        return (sim, bot, Players.CharacterOf(sim, Ana), ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn")));
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

    private static string Log(Simulation sim, BotPlayer bot) =>
        $"bot {bot.Doing}; {string.Join(", ", bot.Stats.Sessions.Select(kv => $"{kv.Key} {kv.Value}"))}\n"
        + string.Join("\n", sim.Messages.Messages.TakeLast(15).Select(m => m.Text));

    private static Entity Part(Simulation sim, Entity inn, string def) =>
        Buildings.Parts(sim, inn).First(e => sim.World.Get<Thing>(e).Def.Name == def);

    private static int Have(Simulation sim, Entity me, Entity inn, string def)
    {
        var w = sim.World;
        var n = sim.StoredIn(Hatch.Of(sim, inn)).Where(e => w.Get<Thing>(e).Def.Name == def).Sum(e => w.Get<Stack>(e).Count);
        if (w.Get<Carrying>(me).Item is { IsNull: false } held && w.Get<Thing>(held).Def.Name == def)
            n += w.Get<Stack>(held).Count;
        return n + sim.StoredIn(sim.PackOf(me)).Where(e => w.Get<Thing>(e).Def.Name == def).Sum(e => w.Get<Stack>(e).Count);
    }

    /// <summary>A party in town that stays (no quest on offer), hungry: their meals are ordered at the hatch.</summary>
    private static List<Entity> HungryParty(Simulation sim, Entity inn)
    {
        if (!sim.World.Has<ShopPrices>(inn))
            sim.World.Add(inn, ShopPrices.Defaults(sim.Defs));
        sim.World.Get<ShopPrices>(inn).Offered[(int)Defs.ServiceKind.Quest] = false;
        var party = sim.Parties.Send(sim, sim.Defs.Party("IronLantern"));
        Tinkering.Wagons.Leave(sim, sim.World.Get<Adventurer>(party[0]).Party);
        foreach (var e in party)
        {
            sim.World.Get<Adventurer>(e).LeaveTick = sim.Clock.Tick + SimClock.TicksPerDay * 2;
            sim.World.Get<Needs>(e).Values[sim.Defs.Need("Food").Index] = 0.5f;
        }
        return party;
    }

    /// <summary>Run till the bot's in its kitchen and between games.</summary>
    private static void InTheKitchen(Simulation sim, BotPlayer bot, Entity me, Entity inn) =>
        Assert.True(Run(sim, bot, () => Buildings.At(sim, sim.World.Get<Position>(me).Cell) == inn && !sim.World.Has<WorkSession>(me)
            && Maps.Map.Distance(sim.World.Get<Position>(me).Cell, sim.World.Get<Thing>(Cooking.PotIn(sim, inn)).Cell) <= 3, SimClock.TicksPerHour));

    [Fact]
    public void TheCookClapsTheLidOnAFlaringPot()
    {
        var (sim, bot, me, inn) = Cook();
        InTheKitchen(sim, bot, me, inn);
        var pot = Cooking.PotIn(sim, inn);
        Cooking.Add(sim, pot, sim.CreateItem(sim.Thing("Meat"), 6));
        Cooking.PotOf(sim, pot).FlareUntil = sim.Clock.Tick + Cooking.FlareSteps;
        Run(sim, bot, () => Cooking.PotOf(sim, pot).FlareUntil == 0, Cooking.FlareSteps + 10);
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("claps the lid on the pot"));
        Assert.Empty(sim.Fire.BurningTiles);
    }

    [Fact]
    public void TheCookLidsAFlareInTheMiddleOfTendingTheStew()
    {
        var (sim, bot, me, inn) = Cook();
        InTheKitchen(sim, bot, me, inn);
        var pot = Cooking.PotIn(sim, inn);
        var at = sim.World.Get<Thing>(pot).Cell;
        FireDrill.MoveTo(sim, me, sim.SpiralFrom(at, 1).First(c => c != at && sim.Map.Passable(c) && Ai.Pathfinder.Touches(c, at)));
        Cooking.Add(sim, pot, sim.CreateItem(sim.Thing("Meat"), 6));
        Cooking.PotOf(sim, pot).FlareUntil = sim.Clock.Tick + Cooking.FlareSteps;
        // Tending it as it flares: the lid's the session's other action.
        sim.ApplyNow(new PotCommand(me, pot, PotStep.Tend));
        var s = Assert.IsType<StewSession>(sim.World.Get<WorkSession>(me));
        Assert.True(s.Flaring > 0);
        Run(sim, bot, () => s.Lidded > 0 || !sim.World.Has<WorkSession>(me), Cooking.FlareSteps + 10);
        Assert.Equal(1, s.Lidded);
        Assert.False(s.Caught);
        Assert.Empty(sim.Fire.BurningTiles);
    }

    [Fact]
    public void TheCookFeedsTheKitchenFireFromTheLogBasket()
    {
        var (sim, bot, me, inn) = Cook();
        var fire = Part(sim, inn, "Fireplace");
        sim.World.Get<Fueled>(fire).Fuel = 2;
        Assert.True(Run(sim, bot, () => sim.World.Get<Fueled>(fire).Fuel > 6, SimClock.TicksPerHour), Log(sim, bot));
    }

    [Fact]
    public void TheCookDressesWhatTheCarterBroughtWhole()
    {
        var (sim, bot, me, inn) = Cook();
        var larder = Part(sim, inn, "Larder");
        bool Whole() => sim.StoredIn(larder).Any(e => sim.World.Get<Thing>(e).Def is { Joints: not null } or { Plucks: not null });
        Assert.True(Whole());
        Assert.True(Run(sim, bot, () => !Whole() && Butchery.Side(sim, me).IsNull && Butchery.Bird(sim, me).IsNull && !sim.World.Has<WorkSession>(me),
            SimClock.TicksPerHour * 2), Log(sim, bot));
        Assert.Equal(1, bot.Stats.Sessions.GetValueOrDefault(nameof(ButcherSession)));
        Assert.Equal(2, bot.Stats.Sessions.GetValueOrDefault(nameof(PluckSession)));
        Assert.Contains(sim.Messages.Messages, m => m.Text.StartsWith("Jointed the side of pork: 7 "));
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("Not a tear in it"));
    }

    [Fact]
    public void TheCookPutsItsDishesOnTheSignAndMakesThemAhead()
    {
        var (sim, bot, me, inn) = Cook();
        Assert.True(Run(sim, bot, () => Menu.Offered(sim, inn).Any(), SimClock.TicksPerHour), Log(sim, bot));
        Assert.Equal(["Bread", "Pie", "Roast"], Menu.Offered(sim, inn).Select(d => d.Dish.Name).Order());
        Assert.True(Run(sim, bot, () => Have(sim, me, inn, Roasting.Dish) > 0 && Have(sim, me, inn, Baking.Bread) > 0 && Have(sim, me, inn, Pies.Pie) > 0,
            SimClock.TicksPerHour * 10), Log(sim, bot));
        // Made well: golden all round, risen and out on the crust, crimped tight.
        Assert.DoesNotContain(sim.Messages.Messages, m => m.Text.Contains("charred") || m.Text.Contains("burnt") || m.Text.Contains("heavy")
            || m.Text.Contains("doughy") || m.Text.Contains("carved pink") || m.Text.Contains("rim loose"));
        Assert.Contains(sim.Messages.Messages, m => m.Text.StartsWith("Lifted off a"));
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("The dough goes in, well risen"));
    }

    [Fact]
    public void TheCookLaysOnAFeast()
    {
        var (sim, bot, me, inn) = Cook();
        Assert.True(Run(sim, bot, () => Menu.Offered(sim, inn).Any(), SimClock.TicksPerHour), Log(sim, bot));
        var party = HungryParty(sim, inn);
        var b = sim.World.Get<PresetBuilding>(inn);
        var inside = Buildings.CellOf(sim.Map, b.Def, b.Anchor, b.Facing, 3, 7);
        sim.World.Get<Position>(party[0]).Cell = inside;
        sim.World.Get<Position>(party[0]).Pos = Position.CenterOf(inside);
        var feast = sim.Feasts.Call(sim, party[0], sim.World.Get<Adventurer>(party[0]).Party, inn)!;
        var named = sim.Shops.All.Where(o => feast.Orders.Contains(o.Id) && o.Dish != null).Select(o => o.Dish!.Name).ToList();
        Assert.NotEmpty(named);
        var laid = false;
        var trace = new System.Text.StringBuilder();
        var last = "";
        Run(sim, bot, () =>
        {
            if (bot.Doing != last)
            {
                last = bot.Doing;
                var hatch = Hatch.Of(sim, inn);
                trace.AppendLine($"{sim.Clock.HourOfDay:0.00} {last} task {bot.Task} hatch[{string.Join(",", sim.StoredIn(hatch).Select(e => $"{sim.World.Get<Thing>(e).Def.Name}x{sim.World.Get<Stack>(e).Count}"))}] held {(sim.World.Get<Carrying>(me).Item is { IsNull: false } h ? sim.World.Get<Thing>(h).Def.Name : "-")}");
            }
            return laid = sim.Messages.Messages.Any(m => m.Text.StartsWith("The feast's laid"));
        }, SimClock.HoursToTicks(Hospitality.FeastSystem.Hours));
        Assert.True(laid, $"dishes {string.Join(", ", named)}; " + string.Join(", ", sim.Shops.All.Where(o => feast.Orders.Contains(o.Id)).Select(o => $"{o.Dish?.Name ?? "plain"} {o.Status}")) + "\n" + trace + Log(sim, bot));
    }

    [Fact]
    public void TheCookChopsWhatGoesInThePotAndSmellsItBeforeItGoesOut()
    {
        var (sim, bot, me, inn) = Cook();
        var pot = Cooking.PotIn(sim, inn);
        HungryParty(sim, inn);
        Assert.True(Run(sim, bot, () => Cooking.PotOf(sim, pot) is { Empty: false, Cooked: true } && bot.Stats.Sessions.ContainsKey(nameof(SniffSession))
            && !sim.World.Has<WorkSession>(me), SimClock.TicksPerHour * 10), Log(sim, bot));
        Assert.True(bot.Stats.Sessions.ContainsKey(nameof(ChopSession)), Log(sim, bot));
        Assert.Contains(Cooking.PotOf(sim, pot).In, i => i.Def.Chopped);
        Assert.Equal(0, bot.Stats.Mistakes);
    }

    [Fact]
    public void ACarelessCookDoesntSmellThePot()
    {
        var careless = BotTemperament.Steady with { Care = 0.2f };
        var (sim, bot, me, inn) = Cook(careless);
        var pot = Cooking.PotIn(sim, inn);
        HungryParty(sim, inn);
        Assert.True(Run(sim, bot, () => Cooking.PotOf(sim, pot).Ladled > 0, SimClock.TicksPerHour * 10), Log(sim, bot));
        Assert.False(bot.Stats.Sessions.ContainsKey(nameof(SniffSession)));
        Assert.True(bot.Stats.Mistakes > 0);
    }

    [Fact]
    public void ACarelessHandSlipsAtTheKitchensGames()
    {
        var (sim, bot, me, inn) = Cook(BotTemperament.Steady with { Care = 0f }, skill: 0.1f);
        Run(sim, bot, () => Have(sim, me, inn, Pies.Pie) > 0, SimClock.TicksPerHour * 10);
        Assert.True(bot.Stats.Mistakes >= 3, $"{bot.Stats.Mistakes} slips; " + Log(sim, bot));
    }

    [Fact]
    public void TheCookPacksRationsForAPartyAndSetsThemOutForSale()
    {
        var (sim, bot, me, inn) = Cook();
        HungryParty(sim, inn);
        bool OnSale() => Goods.OnShow(sim).Count(x => sim.World.Has<Ration>(x.Item) && sim.World.Get<ForSale>(x.Item).Seller == me) >= 1;
        Assert.True(Run(sim, bot, OnSale, SimClock.TicksPerHour * 8), Log(sim, bot));
        Assert.True(bot.Stats.Sessions.ContainsKey(nameof(PackSession)));
        var bundle = Goods.OnShow(sim).First(x => sim.World.Has<Ration>(x.Item)).Item;
        Assert.False(sim.World.Get<Ration>(bundle).LooseWrap, "a steady hand ties it snug");
        Assert.Equal(inn, Goods.ShownIn(sim, bundle));
    }

    [Fact]
    public void TheCookGoesAfterTheRatsInAShortage()
    {
        var (sim, bot, me, inn) = Cook();
        sim.Director.Enable(sim.Defs.Difficulty("Normal"));
        Assert.IsType<Director.ShortageCrisis>(sim.Director.Send(sim, Defs.CrisisKind.Shortage));
        var rats = sim.World.Query<Hospitality.Vermin>().ToList();
        Assert.NotEmpty(rats);
        var chased = false;
        Assert.True(Run(sim, bot, () => (chased |= bot.Doing == "chasing a rat") && rats.Any(r => !sim.World.IsAlive(r) || sim.World.Get<Creature>(r).Dead),
            SimClock.TicksPerHour * 2), Log(sim, bot));
    }
}
