using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
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
/// the book struck through).
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

    private static Entity Board(Simulation sim) => sim.World.Query<Thing>().Single(e => Commissions.IsBoard(sim, e));

    private static void AtBoard(Simulation sim, Entity who)
    {
        var at = sim.World.Get<Thing>(Board(sim)).Cell;
        Put(sim, who, sim.SpiralFrom(at, 1).First(c => c != at && sim.Map.Passable(c) && sim.Map.Standable(sim.Map.Idx(c))));
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
}
