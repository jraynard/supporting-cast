using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Favours;
using HeroesNotIncluded.Sim.Hospitality;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// The innkeeper's bot at its chores (a keg changed when the cask runs low, the slops and the jug,
/// the book struck through) and the labourer's (errands off the notice board, fetched or gathered,
/// and the festival put up).
/// </summary>
public class BotInnLabourTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bea = new("00000000-0000-0000-0000-00000000000b");

    /// <summary>A dealt round: Ana a bot in her trade, Bea (if asked for) a player at the keyboard in hers.</summary>
    private static (Simulation Sim, BotPlayer Bot) Round(string trade, string? bea = null, BotTemperament? temper = null)
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(trade)], []));
        if (bea != null)
        {
            sim.ApplyNow(new JoinLobbyCommand(Bea, "Bea", default));
            sim.ApplyNow(new SetTradePreferencesCommand(Bea, [sim.Defs.Trade(bea)], []));
        }
        sim.ApplyNow(new StartRoundCommand());
        return (sim, new BotPlayer(() => sim, Ana, c => sim.ApplyNow(c), 5) { Temper = temper ?? BotTemperament.Steady });
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

    private static Entity Inn(Simulation sim) => ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));

    private static Entity Room(Simulation sim, int n) => sim.World.Query<GuestRoom>().First(b => InnRooms.Number(sim, b) == n);

    private static void Put(Simulation sim, Entity who, int3 cell)
    {
        sim.World.Get<Position>(who).Cell = cell;
        sim.World.Get<Position>(who).Pos = Position.CenterOf(cell);
    }

    private static Entity Board(Simulation sim) => sim.World.Query<Thing>().Single(e => FavourBoard.IsBoard(sim, e));

    private static void AtBoard(Simulation sim, Entity who)
    {
        var at = sim.World.Get<Thing>(Board(sim)).Cell;
        Put(sim, who, sim.SpiralFrom(at, 1).First(c => c != at && sim.Map.Passable(c) && sim.Map.Standable(sim.Map.Idx(c))));
    }

    /// <summary>The town's own labourers kept off the board, so it's the bot's errand to fill.</summary>
    private static void NoAiLabourers(Simulation sim)
    {
        foreach (var e in sim.Creatures())
            if (!sim.World.Has<Avatar>(e) && sim.World.TryGet<Trade>(e, out var t) && t.Def.Unlimited)
                sim.World.Get<Mind>(e).WorkPriority[(int)WorkType.Haul] = 0;
    }

    private static int Count(Simulation sim, Entity who, ThingDef def) => Inventory.CarriedOf(sim, who, def).Sum(e => sim.World.Get<Stack>(e).Count);

    // ---------------- The innkeeper ----------------

    [Fact]
    public void ABotKeeperChangesTheKegOnceItsPourShowsTheCaskLow()
    {
        var (sim, bot) = Round("Innkeeper");
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        foreach (var a in party)
            sim.World.Get<Adventurer>(a).LeaveTick = sim.Clock.Tick + SimClock.TicksPerDay * 3;
        var inn = Inn(sim);
        var cask = Buildings.Parts(sim, inn).Single(p => sim.World.Get<Thing>(p).Def.Building?.Cask != null);
        var full = sim.World.Get<Thing>(cask).Def.Building!.Cask!.Servings;
        sim.World.Add(cask, new CaskDrawn { Drawn = full - BarWork.TapBelow - 1 }); // one pour from low
        var drinker = party[0];
        Inventory.Stow(sim, drinker, sim.CreateItem(sim.Thing(Coin.Silver), 20));
        var order = sim.Shops.Post(sim, drinker, sim.Defs.Service(ServiceKind.Drink), "An ale.", 2, sim.Clock.Tick + SimClock.HoursToTicks(4), Entity.Null, inn, 9);
        sim.Speech.Say(sim, drinker, order.Text, Speech.Volume.Shout); // so the keeper's heard it
        var low = full;
        Assert.True(Run(sim, bot, () => (low = Math.Min(low, PourSession.ServingsLeft(sim, cask))) <= BarWork.TapBelow
            && PourSession.ServingsLeft(sim, cask) > full / 2, SimClock.TicksPerHour * 4),
            $"cask {PourSession.ServingsLeft(sim, cask)} (low {low}), order open {order.IsOpen}, bot {bot.Doing}");
        Assert.Contains(nameof(TapSession), bot.Stats.Sessions.Keys);
    }

    [Fact]
    public void ABotKeeperEmptiesTheSlopsAndFillsTheJugOfARoomLeft()
    {
        var (sim, bot) = Round("Innkeeper");
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        var bed = Room(sim, 2);
        Lodging.Let(sim, party[0], bed);
        Lodging.CheckOut(sim, party[0]);
        var pot = RoomChores.FixtureOf(sim, bed, RoomChores.PotDef);
        var stand = RoomChores.FixtureOf(sim, bed, RoomChores.StandDef);
        var me = Players.CharacterOf(sim, Ana);
        Assert.True(Run(sim, bot, () => !sim.World.Has<Slops>(pot) && !sim.World.Has<JugEmpty>(stand) && RoomChores.SlopsOn(sim, me).IsNull,
            SimClock.TicksPerHour * 6), $"slops {sim.World.Has<Slops>(pot)}, jug empty {sim.World.Has<JugEmpty>(stand)}, bot {bot.Doing}");
        Assert.Contains(nameof(SlopSession), bot.Stats.Sessions.Keys);
        Assert.False(Lodging.IsUnmade(sim, bed), "and the bed made up");
    }

    [Fact]
    public void ABotKeeperStrikesAGuestWhosGoneOutOfTheBook()
    {
        var (sim, bot) = Round("Innkeeper");
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        var guest = party[0];
        var board = InnRooms.Board(sim, Inn(sim));
        var bed = Room(sim, 3);
        Ledger.Write(sim, board, 3, guest);
        Lodging.Let(sim, guest, bed);
        Lodging.CheckOut(sim, guest); // gone before the night, the key back on its hook
        var hook = InnRooms.OnBoard(sim, Inn(sim), InnRooms.SeedOf(sim, bed));
        Assert.False(hook.IsNull, "its key hangs on the board");
        Assert.True(Ledger.SignedIn(sim, board, guest));
        Assert.True(Run(sim, bot, () => !Ledger.SignedIn(sim, board, guest), SimClock.TicksPerHour * 4), $"bot {bot.Doing}");
    }

    // ---------------- The labourer ----------------

    [Fact]
    public void ABotLabourerReadsTheBoardAndFetchesAnErrand()
    {
        var (sim, bot) = Round("Labourer", "Healer");
        NoAiLabourers(sim);
        var w = sim.World;
        var bea = Players.CharacterOf(sim, Bea);
        AtBoard(sim, bea);
        w.Get<Mind>(bea).Drafted = true;
        var stone = sim.Defs.Thing("Stone");
        var at = w.Get<Thing>(Board(sim)).Cell;
        var pile = sim.SpiralFrom(at, 4).First(c => Maps.Map.Distance(c, at) >= 3 && sim.Map.Passable(c) && sim.Map.Standable(sim.Map.Idx(c)) && sim.Map.Item[sim.Map.Idx(c)].IsNull);
        sim.SpawnItem(stone, 6, pile);
        sim.ApplyNow(new PostErrandCommand(bea, stone, 3, 5, 8));
        var errand = sim.Favours.All.Single();
        var had = Count(sim, bea, stone);
        Assert.True(Run(sim, bot, () => !errand.IsOpen, SimClock.TicksPerHour * 4), $"bot {bot.Doing}");
        Assert.Equal(FavourStatus.Done, errand.Status);
        Assert.Equal(Players.CharacterOf(sim, Ana), errand.Assignee);
        Assert.Equal(had + 3, Count(sim, bea, stone));
    }

    [Fact]
    public void ABotLabourerGathersWhatAnErrandWantsWhenNoneIsLyingAbout()
    {
        var (sim, bot) = Round("Labourer", "Healer");
        NoAiLabourers(sim);
        var w = sim.World;
        var bea = Players.CharacterOf(sim, Bea);
        AtBoard(sim, bea);
        w.Get<Mind>(bea).Drafted = true;
        var wood = sim.Defs.Thing("Wood");
        foreach (var e in sim.AvailableItems().Where(e => w.Get<Thing>(e).Def == wood).ToList())
            sim.DestroyThing(e); // none to be had but off a tree
        sim.ApplyNow(new PostErrandCommand(bea, wood, 4, 6, 12));
        var errand = sim.Favours.All.Single();
        Assert.True(Run(sim, bot, () => !errand.IsOpen, SimClock.TicksPerHour * 8), $"bot {bot.Doing}");
        Assert.Equal(FavourStatus.Done, errand.Status);
        Assert.True(Count(sim, bea, wood) >= 4);
    }

    [Fact]
    public void ACarelessBotLabourerCanBringOneShortAndGoBackForIt()
    {
        var careless = BotTemperament.Steady with { Name = "Careless", Care = 0f };
        var slipped = false;
        for (var seed = 0; seed < 6 && !slipped; seed++)
        {
            var (sim, _) = Round("Labourer", "Healer");
            var bot = new BotPlayer(() => sim, Ana, c => sim.ApplyNow(c), (ulong)seed) { Temper = careless, Skill = 0.1f };
            NoAiLabourers(sim);
            var w = sim.World;
            var bea = Players.CharacterOf(sim, Bea);
            AtBoard(sim, bea);
            w.Get<Mind>(bea).Drafted = true;
            var stone = sim.Defs.Thing("Stone");
            var at = w.Get<Thing>(Board(sim)).Cell;
            var pile = sim.SpiralFrom(at, 4).First(c => Maps.Map.Distance(c, at) >= 3 && sim.Map.Passable(c) && sim.Map.Standable(sim.Map.Idx(c)) && sim.Map.Item[sim.Map.Idx(c)].IsNull);
            sim.SpawnItem(stone, 3, pile);
            var store = sim.SpiralFrom(at, 6).First(c => Maps.Map.Distance(c, at) >= 5 && sim.Map.Passable(c) && sim.Map.Standable(sim.Map.Idx(c)) && sim.Map.Item[sim.Map.Idx(c)].IsNull);
            sim.SpawnItem(stone, 3, store);
            sim.ApplyNow(new PostErrandCommand(bea, stone, 4, 5, 8));
            var errand = sim.Favours.All.Single();
            Assert.True(Run(sim, bot, () => !errand.IsOpen, SimClock.TicksPerHour * 4), $"bot {bot.Doing}");
            Assert.Equal(FavourStatus.Done, errand.Status);
            slipped = bot.Stats.Mistakes > 0;
        }
        Assert.True(slipped, "a careless hand counts one short now and then");
    }

    [Fact]
    public void ABotLabourerPutsUpTheFestival()
    {
        var (sim, bot) = Round("Labourer");
        NoAiLabourers(sim);
        foreach (var e in sim.Creatures())
            if (!sim.World.Has<Avatar>(e) && sim.World.TryGet<Trade>(e, out var t) && t.Def.Gathers)
                sim.World.Get<Mind>(e).WorkPriority[(int)WorkType.Construct] = 0; // it's the bot's to put up
        // To the last afternoon, when the sites go up.
        for (var i = 0; i < 400 && !sim.Festival.SettingUp; i++)
            sim.TickMany(SimClock.TicksPerHour / 4);
        Assert.True(sim.Festival.SettingUp, "the setting up began");
        var sites = sim.Festival.Sites.Where(e => !e.IsNull && sim.World.Has<Construction>(e)).ToList();
        Assert.NotEmpty(sites);
        Assert.True(Run(sim, bot, () => sites.Any(e => !sim.World.IsAlive(e) || !sim.World.Has<Construction>(e)), SimClock.TicksPerHour * 2),
            $"bot {bot.Doing}");
    }
}
